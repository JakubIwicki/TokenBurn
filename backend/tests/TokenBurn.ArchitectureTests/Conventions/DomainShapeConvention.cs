using System.Reflection;
using TokenBurn.Common.Primitives;

namespace TokenBurn.ArchitectureTests.Conventions;

/// <summary>
///     Scans every production assembly for types deriving
///     <see cref="BaseEntity{TKey}" /> that live in a *.Domain.* namespace and
///     requires the aggregate shape: sealed, a private parameterless constructor
///     (for the ORM), and at least one public static factory returning the type.
///     Collects ALL violations across ALL discovered assemblies.
///     ScannedAssemblyNames reports every discovered production assembly so the
///     exact-set guard stays loud before the first domain aggregates land.
/// </summary>
public static class DomainShapeConvention
{
    public static ConventionResult CollectDomainShapeViolations()
    {
        var violations = new List<string>();
        var loadFailures = new List<string>();
        var scannedAssemblyNames = new List<string>();
        int scannedCount = 0;

        foreach (Assembly assembly in ProductionAssemblies.Discover())
        {
            SafeGetTypesResult typesResult = ProductionAssemblies.SafeGetTypes(assembly);
            if (typesResult.LoadFailure is not null)
            {
                loadFailures.Add(typesResult.LoadFailure);
            }

            scannedAssemblyNames.Add(assembly.GetName().Name!);

            var domainEntities = typesResult.Types
                .Where(t => t.Namespace is not null
                            && (t.Namespace.EndsWith(".Domain", StringComparison.Ordinal)
                                || t.Namespace.Contains(".Domain.", StringComparison.Ordinal))
                            && DerivesFromBaseEntity(t))
                .ToList();

            scannedCount += domainEntities.Count;

            foreach (Type entity in domainEntities)
            {
                string? v = CheckSealed(entity);
                if (v is not null)
                {
                    violations.Add(v);
                }

                v = CheckPrivateParameterlessCtor(entity);
                if (v is not null)
                {
                    violations.Add(v);
                }

                v = CheckStaticFactory(entity);
                if (v is not null)
                {
                    violations.Add(v);
                }
            }
        }

        var scannedAssemblyNamesOrdered = scannedAssemblyNames.OrderBy(n => n).ToList();

        return new ConventionResult(violations, scannedCount, scannedAssemblyNamesOrdered, loadFailures);
    }

    private static bool DerivesFromBaseEntity(Type type)
    {
        Type? current = type.BaseType;
        while (current is not null)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(BaseEntity<>))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static string? CheckSealed(Type type)
        => type.IsSealed ? null : $"{type.FullName} — domain aggregate must be sealed";

    private static string? CheckPrivateParameterlessCtor(Type type)
    {
        bool hasPrivateParameterlessCtor = type
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Any(ctor => ctor.GetParameters().Length == 0);

        return hasPrivateParameterlessCtor
            ? null
            : $"{type.FullName} — missing private parameterless constructor (required by the ORM)";
    }

    private static string? CheckStaticFactory(Type type)
    {
        bool hasStaticFactory = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Any(m => m.ReturnType == type);

        return hasStaticFactory
            ? null
            : $"{type.FullName} — missing public static factory returning the aggregate";
    }
}
