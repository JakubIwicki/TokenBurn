using Api.TokenBurn.Ingest.Features.Logs;
using Api.TokenBurn.Ingest.Features.Traces;
using Api.TokenBurn.Ingest.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using TokenBurn.Common.Messaging;
using TokenBurn.Common.Security;

namespace Api.TokenBurn.Ingest.Extensions;

public static class ServiceHostExtensions
{
    public static WebApplicationBuilder AddApiServices(this WebApplicationBuilder builder)
    {
        string connectionString = builder.Configuration.GetConnectionString("Ingest")
            ?? throw new InvalidOperationException("ConnectionStrings:Ingest must be configured.");
        builder.Services.AddHealthChecks();
        builder.AddTokenBurnJwtAuth();
        builder.Services.AddTokenBurnMediatR(typeof(Program).Assembly);
        builder.Services.AddProblemDetails();
        builder.Services.AddDbContext<IngestDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(IngestDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__EFMigrationsHistory", "ingest")));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IEnvelopeInbox, EnvelopeInbox>();
        builder.Services.AddHostedService<OutboxPublisher>();
        int tokenLimit = ReadRateLimit("Ingest:RateLimit:TokenLimit", 100);
        int tokensPerPeriod = ReadRateLimit("Ingest:RateLimit:TokensPerPeriod", 100);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("v1", context => RateLimitPartition.GetTokenBucketLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? context.User.FindFirstValue("sub")
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown",
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = tokenLimit,
                    TokensPerPeriod = tokensPerPeriod,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        });

        int ReadRateLimit(string key, int fallback) =>
            int.TryParse(builder.Configuration[key], out int value) ? value : fallback;

        return builder;
    }

    public static async Task<WebApplication> InitializeIngestAsync(this WebApplication app)
    {
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        IngestDbContext db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        await db.Database.MigrateAsync();
        return app;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.UseTokenBurnAuth();
        app.UseRateLimiter();
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready");
        app.MapExportTraces();
        app.MapExportLogs();
        return app;
    }
}
