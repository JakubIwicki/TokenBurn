using IdentityServiceHost = Api.TokenBurn.Identity.Extensions.ServiceHostExtensions;
using IngestServiceHost = Api.TokenBurn.Ingest.Extensions.ServiceHostExtensions;
using InsightsServiceHost = Api.TokenBurn.Insights.Extensions.ServiceHostExtensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProcessorServiceHost = TokenBurn.Processor.ProcessorExtensions;
using TokenBurn.ArchitectureTests.Conventions;

namespace TokenBurn.ArchitectureTests;

public sealed class EndpointAuthorizationTests
{
    [Fact]
    public void AllIdentityEndpoints_AreAuthorizedOrExplicitlyAllowListed()
    {
        WebApplication app = BuildHostWithoutHostedServices(
            b => IdentityServiceHost.AddApiServices(b),
            a => IdentityServiceHost.MapDefaultEndpoints(a));

        // `/api/models` (anonymous model directory, privacy-boundary.md rule 8)
        // joins this list in Phase 3 when the endpoint exists.
        var allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/health", // liveness probe consumed anonymously by container healthchecks (docker-compose.yml)
            "/health/ready" // readiness probe consumed anonymously by container healthchecks (docker-compose.yml)
        };

        ICollection<EndpointDataSource> dataSources = ((IEndpointRouteBuilder)app).DataSources;

        ConventionResult result = EndpointAuthorizationConvention.CollectUnauthorizedEndpoints(dataSources, allowList);

        result.Violations.Should().BeEmpty(
            $"every endpoint must require authorization unless explicitly allow-listed.\n" +
            $"Allow-listed: {string.Join(", ", allowList)}.\n" +
            $"Scanned {result.ScannedCount} endpoint(s).\n" +
            $"Violations:\n{string.Join('\n', result.Violations)}");

        // Bump this const deliberately when an endpoint is added — a silently
        // shrinking count is the exact failure mode that lets an endpoint
        // escape the authorization scan.
        const int expectedScannedPairCount = 2;
        result.ScannedCount.Should().Be(expectedScannedPairCount,
            $"the authorization convention must scan exactly {expectedScannedPairCount} method+pattern pairs " +
            $"(one per mapped endpoint); a count drop means endpoints escaped the scan");

        // Vacuity guard: verify every allow-listed pattern is actually mapped.
        // This catches "nothing got mapped" and "the allow-list has a stale entry",
        // which is stronger than a count check when all patterns are public.
        var mappedPatterns = dataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .Where(p => p is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string pattern in allowList)
        {
            mappedPatterns.Should().Contain(pattern,
                $"the allow-listed pattern '{pattern}' must be mapped by the Identity endpoints");
        }
    }

    [Fact]
    public void AllIngestEndpoints_AreAuthorizedOrExplicitlyAllowListed()
    {
        WebApplication app = BuildHostWithoutHostedServices(
            b => IngestServiceHost.AddApiServices(b),
            a => IngestServiceHost.MapDefaultEndpoints(a));

        // `/api/models` (anonymous model directory, privacy-boundary.md rule 8)
        // joins this list in Phase 3 when the endpoint exists.
        var allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/health", // liveness probe consumed anonymously by container healthchecks (docker-compose.yml)
            "/health/ready" // readiness probe consumed anonymously by container healthchecks (docker-compose.yml)
        };

        ICollection<EndpointDataSource> dataSources = ((IEndpointRouteBuilder)app).DataSources;

        ConventionResult result = EndpointAuthorizationConvention.CollectUnauthorizedEndpoints(dataSources, allowList);

        result.Violations.Should().BeEmpty(
            $"every endpoint must require authorization unless explicitly allow-listed.\n" +
            $"Allow-listed: {string.Join(", ", allowList)}.\n" +
            $"Scanned {result.ScannedCount} endpoint(s).\n" +
            $"Violations:\n{string.Join('\n', result.Violations)}");

        // Bump this const deliberately when an endpoint is added — a silently
        // shrinking count is the exact failure mode that lets an endpoint
        // escape the authorization scan.
        const int expectedScannedPairCount = 2;
        result.ScannedCount.Should().Be(expectedScannedPairCount,
            $"the authorization convention must scan exactly {expectedScannedPairCount} method+pattern pairs " +
            $"(one per mapped endpoint); a count drop means endpoints escaped the scan");

        // Vacuity guard: verify every allow-listed pattern is actually mapped.
        // This catches "nothing got mapped" and "the allow-list has a stale entry",
        // which is stronger than a count check when all patterns are public.
        var mappedPatterns = dataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .Where(p => p is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string pattern in allowList)
        {
            mappedPatterns.Should().Contain(pattern,
                $"the allow-listed pattern '{pattern}' must be mapped by the Ingest endpoints");
        }
    }

    [Fact]
    public void AllInsightsEndpoints_AreAuthorizedOrExplicitlyAllowListed()
    {
        WebApplication app = BuildHostWithoutHostedServices(
            b => InsightsServiceHost.AddApiServices(b),
            a => InsightsServiceHost.MapDefaultEndpoints(a));

        // `/api/models` (anonymous model directory, privacy-boundary.md rule 8)
        // joins this list in Phase 3 when the endpoint exists.
        var allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/health", // liveness probe consumed anonymously by container healthchecks (docker-compose.yml)
            "/health/ready" // readiness probe consumed anonymously by container healthchecks (docker-compose.yml)
        };

        ICollection<EndpointDataSource> dataSources = ((IEndpointRouteBuilder)app).DataSources;

        ConventionResult result = EndpointAuthorizationConvention.CollectUnauthorizedEndpoints(dataSources, allowList);

        result.Violations.Should().BeEmpty(
            $"every endpoint must require authorization unless explicitly allow-listed.\n" +
            $"Allow-listed: {string.Join(", ", allowList)}.\n" +
            $"Scanned {result.ScannedCount} endpoint(s).\n" +
            $"Violations:\n{string.Join('\n', result.Violations)}");

        // Bump this const deliberately when an endpoint is added — a silently
        // shrinking count is the exact failure mode that lets an endpoint
        // escape the authorization scan.
        const int expectedScannedPairCount = 2;
        result.ScannedCount.Should().Be(expectedScannedPairCount,
            $"the authorization convention must scan exactly {expectedScannedPairCount} method+pattern pairs " +
            $"(one per mapped endpoint); a count drop means endpoints escaped the scan");

        // Vacuity guard: verify every allow-listed pattern is actually mapped.
        // This catches "nothing got mapped" and "the allow-list has a stale entry",
        // which is stronger than a count check when all patterns are public.
        var mappedPatterns = dataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .Where(p => p is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string pattern in allowList)
        {
            mappedPatterns.Should().Contain(pattern,
                $"the allow-listed pattern '{pattern}' must be mapped by the Insights endpoints");
        }
    }

    [Fact]
    public void AllProcessorEndpoints_AreAuthorizedOrExplicitlyAllowListed()
    {
        WebApplication app = BuildHostWithoutHostedServices(
            b => ProcessorServiceHost.AddProcessorServices(b),
            a => ProcessorServiceHost.MapProcessorEndpoints(a));

        // `/api/models` (anonymous model directory, privacy-boundary.md rule 8)
        // joins this list in Phase 3 when the endpoint exists.
        var allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/health", // liveness probe consumed anonymously by container healthchecks (docker-compose.yml)
            "/health/ready" // readiness probe consumed anonymously by container healthchecks (docker-compose.yml)
        };

        ICollection<EndpointDataSource> dataSources = ((IEndpointRouteBuilder)app).DataSources;

        ConventionResult result = EndpointAuthorizationConvention.CollectUnauthorizedEndpoints(dataSources, allowList);

        result.Violations.Should().BeEmpty(
            $"every endpoint must require authorization unless explicitly allow-listed.\n" +
            $"Allow-listed: {string.Join(", ", allowList)}.\n" +
            $"Scanned {result.ScannedCount} endpoint(s).\n" +
            $"Violations:\n{string.Join('\n', result.Violations)}");

        // Bump this const deliberately when an endpoint is added — a silently
        // shrinking count is the exact failure mode that lets an endpoint
        // escape the authorization scan.
        const int expectedScannedPairCount = 2;
        result.ScannedCount.Should().Be(expectedScannedPairCount,
            $"the authorization convention must scan exactly {expectedScannedPairCount} method+pattern pairs " +
            $"(one per mapped endpoint); a count drop means endpoints escaped the scan");

        // Vacuity guard: verify every allow-listed pattern is actually mapped.
        // This catches "nothing got mapped" and "the allow-list has a stale entry",
        // which is stronger than a count check when all patterns are public.
        var mappedPatterns = dataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .Where(p => p is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string pattern in allowList)
        {
            mappedPatterns.Should().Contain(pattern,
                $"the allow-listed pattern '{pattern}' must be mapped by the Processor endpoints");
        }
    }

    // Belt-and-braces: strips every IHostedService so no future hosted service
    // could start if the boot pattern ever changes. These tests never start the
    // host, so registered hosted services are inert today.
    private static WebApplication BuildHostWithoutHostedServices(
        Action<WebApplicationBuilder> addServices,
        Action<WebApplication> mapEndpoints)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        addServices(builder);

        builder.Services.RemoveAll<IHostedService>();

        WebApplication app = builder.Build();
        mapEndpoints(app);
        return app;
    }
}
