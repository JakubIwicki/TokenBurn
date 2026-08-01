using System.Reflection;

namespace TokenBurn.ArchitectureTests.Conventions;

/// <summary>
///     TokenBurn's kernel boundary: the shared kernel (TokenBurn.Common,
///     TokenBurn.Contracts) must stay dependency-free, and every host
///     (api.TokenBurn.*, TokenBurn.Processor, TokenBurn.Collector) may
///     reference only the kernel. Enforced on the assembly-reference graph,
///     so the convention is immune to source-layout drift. A new src project
///     must be added to <see cref="AllowedReferences" /> (its allowed-references
///     row) or its name is missing from the scan entirely and the test fails.
/// </summary>
public static class DependencyDirectionConvention
{
    private static readonly Dictionary<string, HashSet<string>> AllowedReferences = new(StringComparer.Ordinal)
    {
        ["TokenBurn.Common"] = [],
        ["TokenBurn.Contracts"] = [],
        ["api.TokenBurn.Identity"] = ["TokenBurn.Common", "TokenBurn.Contracts"],
        ["api.TokenBurn.Ingest"] = ["TokenBurn.Common", "TokenBurn.Contracts"],
        ["api.TokenBurn.Insights"] = ["TokenBurn.Common", "TokenBurn.Contracts"],
        ["TokenBurn.Processor"] = ["TokenBurn.Common", "TokenBurn.Contracts"],
        ["TokenBurn.Collector"] = ["TokenBurn.Common", "TokenBurn.Contracts"],
    };

    public static ConventionResult CollectForbiddenDependencyDirections()
    {
        var violations = new List<string>();
        var loadFailures = new List<string>();
        var scannedAssemblyNames = new List<string>();

        foreach (string assemblyName in AllowedReferences.Keys.OrderBy(n => n))
        {
            Assembly? assembly = LoadAssembly(assemblyName, loadFailures);
            if (assembly is null)
            {
                continue;
            }

            scannedAssemblyNames.Add(assembly.GetName().Name!);

            IEnumerable<string> referencedNames = assembly.GetReferencedAssemblies()
                .Where(r => r.Name is not null
                            && r.Name != assemblyName
                            && (r.Name.StartsWith("TokenBurn.", StringComparison.Ordinal)
                                || r.Name.StartsWith("api.TokenBurn.", StringComparison.Ordinal)))
                .Select(r => r.Name!)
                .Distinct()
                .OrderBy(n => n);

            foreach (string referencedName in referencedNames)
            {
                if (!AllowedReferences[assemblyName].Contains(referencedName))
                {
                    violations.Add($"{assemblyName} → {referencedName}");
                }
            }
        }

        return new ConventionResult(violations, scannedAssemblyNames.Count, scannedAssemblyNames, loadFailures);
    }

    private static Assembly? LoadAssembly(string assemblyName, List<string> loadFailures)
    {
        try
        {
            return Assembly.Load(assemblyName);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            loadFailures.Add($"Assembly '{assemblyName}' failed to load: {ex.Message.Split('\n')[0]}");
            return null;
        }
    }
}
