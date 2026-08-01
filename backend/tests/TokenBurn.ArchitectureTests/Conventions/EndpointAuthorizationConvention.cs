using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace TokenBurn.ArchitectureTests.Conventions;

/// <summary>
///     Walk every <see cref="RouteEndpoint" /> and flag any that lacks
///     <see cref="IAuthorizeData" /> metadata, unless its route pattern is in
///     the <paramref name="allowList" />. Every method+pattern pair is counted —
///     allow-listed pairs included — so the exact scan-count guard stays live
///     even while every mapped endpoint is allow-listed. Each HTTP method+pattern
///     pair is checked independently so that GET and POST on the same path are
///     both verified.
/// </summary>
public static class EndpointAuthorizationConvention
{
    public static ConventionResult CollectUnauthorizedEndpoints(
        IEnumerable<EndpointDataSource> dataSources,
        ISet<string> allowList)
    {
        var violations = new List<string>();
        var processed = new HashSet<string>(StringComparer.Ordinal);
        int scannedCount = 0;

        foreach (EndpointDataSource ds in dataSources)
        {
            foreach (Endpoint endpoint in ds.Endpoints)
            {
                if (endpoint is not RouteEndpoint routeEndpoint)
                {
                    continue;
                }

                string? pattern = routeEndpoint.RoutePattern.RawText;
                if (pattern is null)
                {
                    continue;
                }

                IEnumerable<string> httpMethods = routeEndpoint.Metadata
                    .OfType<HttpMethodMetadata>()
                    .SelectMany(m => m.HttpMethods)
                    .DefaultIfEmpty("?");

                bool hasAuth = endpoint.Metadata
                    .Any(m => m is IAuthorizeData);

                bool isAllowListed = allowList.Contains(pattern);

                foreach (string method in httpMethods)
                {
                    string key = $"{method} {pattern}";
                    if (!processed.Add(key))
                    {
                        continue;
                    }

                    scannedCount++;

                    if (isAllowListed || hasAuth)
                    {
                        continue;
                    }

                    violations.Add(
                        $"{method} {pattern} — no authorization metadata. Add .RequireAuthorization() to the endpoint.");
                }
            }
        }

        return new ConventionResult(violations, scannedCount);
    }
}
