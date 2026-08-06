using System.Security.Claims;
using System.Text.Encodings.Web;
using Api.TokenBurn.Insights.Extensions.Embeddings;
using Api.TokenBurn.Insights.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TokenBurn.Common.Security;
using TokenBurn.Processor.Persistence;

namespace Api.TokenBurn.Insights.Tests;

/// <summary>
///     Shared host bootstrap for the Insights integration suites. The real host
///     requires ConnectionStrings:Insights at build time, so every factory points
///     at a live cloned telemetry database; Elasticsearch is configured only by the
///     search/ask suites, which resolve the client. Auth is stubbed with a controllable
///     scheme that grants <c>insights.read</c> and <c>ask.invoke</c> and can be forced
///     anonymous per request — the <c>[Authorize]</c> policies stay live either way. A
///     request may drop a scope (e.g. <c>ask.invoke</c>) to exercise a 403.
/// </summary>
internal static class InsightsTestHost
{
    public const string NoAuthHeader = "X-Test-No-Auth";
    public const string DropScopeHeader = "X-Test-Drop-Scope";
    public const string DropSubHeader = "X-Test-Drop-Sub";

    public static WebApplicationFactory<InsightsDbContext> Create(
        string connectionString,
        string? elasticsearchUri = null,
        IEmbeddingClient? embeddingClient = null,
        TimeProvider? timeProvider = null,
        IReadOnlyDictionary<string, string?>? extraSettings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        return new WebApplicationFactory<InsightsDbContext>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Insights", connectionString);
            builder.UseSetting("Jwt:Authority", "http://localhost/connect");
            // Ask tests must never trip the per-second rate limiter; the per-hour budget is
            // exercised through Ask:Budget:MaxRequestsPerHour, not this limiter.
            builder.UseSetting("Insights:AskRateLimit:TokenLimit", "100000");
            builder.UseSetting("Insights:AskRateLimit:TokensPerPeriod", "100000");
            if (elasticsearchUri is not null)
                builder.UseSetting("Elasticsearch:Uri", elasticsearchUri);
            if (extraSettings is not null)
                foreach ((string key, string? value) in extraSettings)
                    builder.UseSetting(key, value);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.AddSingleton<IConfigureOptions<AuthenticationOptions>>(
                    new ConfigureNamedOptions<AuthenticationOptions>(Options.DefaultName, options =>
                    {
                        options.DefaultScheme = "Test";
                        options.DefaultAuthenticateScheme = "Test";
                        options.DefaultChallengeScheme = "Test";
                        options.DefaultForbidScheme = "Test";
                    }));
                if (embeddingClient is not null)
                    services.AddSingleton(embeddingClient);
                if (timeProvider is not null)
                    services.AddSingleton(timeProvider);
                configureServices?.Invoke(services);
            });
        });
    }

    /// <summary>
    ///     Applies the Processor's telemetry migrations onto a fresh database, so
    ///     Insights reads the same schema production creates. The shared template is
    ///     created once per process and cloned per suite/test.
    /// </summary>
    public static async Task MigrateTelemetryAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "telemetry"))
            .Options;
        await using TelemetryDbContext db = new(options);
        await db.Database.MigrateAsync();
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.ContainsKey(NoAuthHeader))
                return Task.FromResult(AuthenticateResult.NoResult());

            var scopes = new List<string> { TokenBurnScopes.InsightsRead, TokenBurnScopes.AskInvoke };
            string dropScopes = Request.Headers[DropScopeHeader].ToString();
            if (!string.IsNullOrWhiteSpace(dropScopes))
            {
                foreach (string scope in dropScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    scopes.Remove(scope);
            }

            var claims = new List<Claim> { new("scope", string.Join(' ', scopes)) };
            if (!Request.Headers.ContainsKey(DropSubHeader))
                claims.Add(new Claim("sub", "test-client"));
            ClaimsIdentity identity = new(claims, Scheme.Name);
            ClaimsPrincipal principal = new(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
