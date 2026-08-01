using System.Reflection;

namespace TokenBurn.ArchitectureTests.Conventions;

/// <summary>
///     Auto-discovers all <c>api.TokenBurn.*.dll</c> API assemblies in the output directory,
///     then scans every namespace containing the <c>.Features.</c> segment.
///     For each slice namespace that contains a *Handler class, require:
///     (a) a static *Endpoint class exposing a public static Map* method,
///     (b) a message type present — one of *Command, *Query, or *Request,
///     (c) the handler exposes a public HandleAsync method.
///     Namespaces with no handler are skipped.
///     Collects ALL violations across ALL discovered assemblies.
///     ScannedAssemblyNames reports every discovered API assembly (not only those
///     containing a .Features. namespace) so the exact-set guard stays loud
///     before the first slices land.
/// </summary>
public static class SliceAnatomyConvention
{
    public static ConventionResult CollectSliceAnatomyViolations()
    {
        var violations = new List<string>();
        const string marker = ".Features.";
        var loadFailures = new List<string>();

        var discoveredAssemblies = ProductionAssemblies.DiscoverApiAssemblies().ToList();

        var featureAssemblies = discoveredAssemblies
            .Where(asm =>
            {
                SafeGetTypesResult result = ProductionAssemblies.SafeGetTypes(asm);
                if (result.LoadFailure is not null)
                {
                    loadFailures.Add(result.LoadFailure);
                }

                return result.Types.Any(t =>
                    t.Namespace is not null
                    && t.Namespace.Contains(marker, StringComparison.Ordinal));
            })
            .DistinctBy(asm => asm.FullName)
            .ToList();

        int scannedCount = 0;

        foreach (Assembly assembly in featureAssemblies)
        {
            SafeGetTypesResult typesResult = ProductionAssemblies.SafeGetTypes(assembly);
            if (typesResult.LoadFailure is not null)
            {
                loadFailures.Add(typesResult.LoadFailure);
            }

            var featureTypes = typesResult.Types
                .Where(t => t.Namespace is not null
                            && t.Namespace.Contains(marker, StringComparison.Ordinal))
                .ToList();

            var handlerTypes = featureTypes
                .Where(t => t is { IsClass: true, IsAbstract: false }
                            && t.Name.EndsWith("Handler", StringComparison.Ordinal))
                .ToList();

            scannedCount += handlerTypes.Count;

            foreach (Type handlerType in handlerTypes)
            {
                string ns = handlerType.Namespace!;

                int markerIndex = ns.IndexOf(marker, StringComparison.Ordinal);
                string relativeFolder = ns[(markerIndex + marker.Length)..].Replace('.', '/');
                var nsTypes = featureTypes.Where(t => t.Namespace == ns).ToList();

                string? v = CheckHandlerHasHandleAsync(handlerType, relativeFolder);
                if (v is not null)
                {
                    violations.Add(v);
                }

                v = CheckEndpointClass(nsTypes, relativeFolder);
                if (v is not null)
                {
                    violations.Add(v);
                }

                v = CheckMessageType(nsTypes, relativeFolder);
                if (v is not null)
                {
                    violations.Add(v);
                }
            }
        }

        var scannedAssemblyNames = discoveredAssemblies
            .Select(a => a.GetName().Name!)
            .OrderBy(n => n)
            .ToList();

        return new ConventionResult(violations, scannedCount, scannedAssemblyNames, loadFailures);
    }

    private static string? CheckHandlerHasHandleAsync(Type handlerType, string relativeFolder)
    {
        bool hasHandleAsync = handlerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Any(m => m.Name == "HandleAsync");

        return hasHandleAsync
            ? null
            : $"{relativeFolder}/{handlerType.Name} — missing public HandleAsync method";
    }

    private static string? CheckEndpointClass(IReadOnlyList<Type> nsTypes, string relativeFolder)
    {
        Type? endpointType = nsTypes.FirstOrDefault(t =>
            t is { IsClass: true, IsAbstract: true, IsSealed: true }
            && t.Name.EndsWith("Endpoint", StringComparison.Ordinal));

        if (endpointType is null)
        {
            return $"{relativeFolder} — missing static Endpoint class";
        }

        bool hasMapMethod = endpointType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Any(m => m.Name.StartsWith("Map", StringComparison.Ordinal));

        return hasMapMethod
            ? null
            : $"{relativeFolder}/{endpointType.Name} — no public static Map* method";
    }

    private static string? CheckMessageType(IReadOnlyList<Type> nsTypes, string relativeFolder)
    {
        bool hasCommand = nsTypes.Any(t =>
            t.Name.EndsWith("Command", StringComparison.Ordinal));
        bool hasQuery = nsTypes.Any(t =>
            t.Name.EndsWith("Query", StringComparison.Ordinal));
        bool hasRequest = nsTypes.Any(t =>
            t.Name.EndsWith("Request", StringComparison.Ordinal));

        return hasCommand || hasQuery || hasRequest
            ? null
            : $"{relativeFolder} — missing message type (need at least one of *Command, *Query, or *Request)";
    }
}
