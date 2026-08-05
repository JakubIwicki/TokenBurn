using System.Security.Claims;
using System.Text.Encodings.Web;
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
///     search suite, which resolves the client. Auth is stubbed with a controllable
///     scheme that grants <c>insights.read</c> and can be forced anonymous per
///     request — the <c>[Authorize]</c> policy stays live either way.
/// </summary>
internal static class InsightsTestHost
{
    public const string NoAuthHeader = "X-Test-No-Auth";

    public static WebApplicationFactory<InsightsDbContext> Create(string connectionString, string? elasticsearchUri = null)
    {
        return new WebApplicationFactory<InsightsDbContext>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Insights", connectionString);
            builder.UseSetting("Jwt:Authority", "http://localhost/connect");
            if (elasticsearchUri is not null)
                builder.UseSetting("Elasticsearch:Uri", elasticsearchUri);
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

            ClaimsIdentity identity = new(
                [new Claim("scope", TokenBurnScopes.InsightsRead), new Claim("sub", "test-client")],
                Scheme.Name);
            ClaimsPrincipal principal = new(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
