using Api.TokenBurn.Insights.Features.Runs;
using Api.TokenBurn.Insights.Features.Search;
using Api.TokenBurn.Insights.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using TokenBurn.Common.Messaging;
using TokenBurn.Common.Security;

namespace Api.TokenBurn.Insights.Extensions;

public static class ServiceHostExtensions
{
    public static WebApplicationBuilder AddApiServices(this WebApplicationBuilder builder)
    {
        string connectionString = builder.Configuration.GetConnectionString("Insights")
            ?? throw new InvalidOperationException("ConnectionStrings:Insights must be configured.");
        builder.Services.AddHealthChecks();
        builder.AddTokenBurnJwtAuth();
        builder.Services.AddTokenBurnMediatR(typeof(Program).Assembly);
        builder.Services.AddProblemDetails();
        builder.Services.AddDbContext<InsightsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(InsightsDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__EFMigrationsHistory", "telemetry")));
        builder.Services.AddInsightsElasticsearchClient(builder.Configuration);
        int tokenLimit = ReadRateLimit(builder.Configuration, "Insights:RateLimit:TokenLimit", 100);
        int tokensPerPeriod = ReadRateLimit(builder.Configuration, "Insights:RateLimit:TokensPerPeriod", 100);
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
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.UseTokenBurnAuth();
        app.UseRateLimiter();
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready");
        app.MapSearchEndpoints();
        app.MapRunsEndpoints();
        return app;
    }

    private static int ReadRateLimit(IConfiguration configuration, string key, int fallback)
        => int.TryParse(configuration[key], out int value) ? value : fallback;
}
