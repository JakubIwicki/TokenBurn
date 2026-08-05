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
            b =>
            {
                b.Configuration["ConnectionStrings:Identity"] = "Host=localhost;Database=identity;Username=test;Password=test";
                b.Configuration["Jwt:Authority"] = "http://localhost/connect";
                b.Configuration["Identity:CollectorClientSecret"] = "test-secret";
                b.Configuration["Identity:DevUser:Username"] = "dev";
                b.Configuration["Identity:DevUser:Password"] = "test-password";
                IdentityServiceHost.AddApiServices(b);
                b.Services.AddControllers().AddApplicationPart(typeof(Api.TokenBurn.Identity.Extensions.ServiceHostExtensions).Assembly);
            },
            a => IdentityServiceHost.MapDefaultEndpoints(a));

        // OpenIddict protocol and metadata endpoints are intentionally anonymous (dotnet-security-baseline).
        var allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/health",
            "/health/ready",
            // MVC exposes these OpenIddict protocol routes without a leading slash.
            "connect/token",
            "connect/authorize",
            "connect/login",
            "connect/logout"
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
        const int expectedScannedPairCount = 7;
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
            b =>
            {
                b.Configuration["Jwt:Authority"] = "http://localhost/connect";
                b.Configuration["ConnectionStrings:Ingest"] = "Host=localhost;Database=ingest;Username=test;Password=test";
                IngestServiceHost.AddApiServices(b);
            },
            a => IngestServiceHost.MapDefaultEndpoints(a));

        // Ingest maps two authenticated OTLP endpoints in addition to health probes.

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
        const int expectedScannedPairCount = 4;
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
            b =>
            {
                b.Configuration["Jwt:Authority"] = "http://localhost/connect";
                b.Configuration["ConnectionStrings:Insights"] =
                    "Host=localhost;Database=tokenburn;Username=insights_role;Password=test";
                InsightsServiceHost.AddApiServices(b);
            },
            a => InsightsServiceHost.MapDefaultEndpoints(a));

        // `/api/models` (anonymous model directory, privacy-boundary.md rule 8)
        // joins this list in Phase 8 when the endpoint exists.
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
        const int expectedScannedPairCount = 5;
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
            b =>
            {
                b.Configuration["ConnectionStrings:Processor"] =
                    "Host=localhost;Database=processor;Username=test;Password=test";
                b.Configuration["Jwt:Authority"] = "http://localhost/connect";
                ProcessorServiceHost.AddProcessorServices(b);
            },
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
        const int expectedScannedPairCount = 4;
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
