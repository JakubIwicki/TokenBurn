using System.Reflection;

namespace TokenBurn.ArchitectureTests.Conventions;

public static class ProductionAssemblies
{
    public static IReadOnlyList<Assembly> Discover() =>
        [
            .. Directory.GetFiles(AppContext.BaseDirectory, "TokenBurn.*.dll")
                .Concat(Directory.GetFiles(AppContext.BaseDirectory, "api.TokenBurn.*.dll"))
                .Select(Assembly.LoadFrom)
                .Where(asm =>
                {
                    string? name = asm.GetName().Name;
                    return name is not null
                           && !name.Contains("Test", StringComparison.OrdinalIgnoreCase);
                })
        ];

    public static IReadOnlyList<Assembly> DiscoverApiAssemblies() =>
        [
            .. Discover()
                .Where(a =>
                {
                    string? name = a.GetName().Name;
                    return name is not null
                           && name.StartsWith("api.TokenBurn.", StringComparison.Ordinal);
                })
        ];

    public static SafeGetTypesResult SafeGetTypes(Assembly assembly)
    {
        try
        {
            return new SafeGetTypesResult([.. assembly.GetTypes()], null);
        }
        catch (ReflectionTypeLoadException ex)
        {
            var types = ex.Types.Where(t => t is not null).Cast<Type>().ToList();

            var failureMessages = ex.LoaderExceptions
                .Where(e => e is not null)
                .Select(e => e!.Message.Split('\n')[0])
                .Distinct()
                .ToList();

            string details = failureMessages.Count > 0
                ? failureMessages[0]
                : "no loader exception details available";

            string loadFailure = $"Assembly '{assembly.GetName().Name}' failed to load types: {details}";

            return new SafeGetTypesResult(types, loadFailure);
        }
    }
}

public readonly record struct SafeGetTypesResult(IReadOnlyList<Type> Types, string? LoadFailure);
