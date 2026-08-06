using Api.TokenBurn.Insights.Extensions.Embeddings;
using Api.TokenBurn.Insights.Features.Ask;
using Api.TokenBurn.Insights.Features.Ask.Chat;
using Api.TokenBurn.Insights.Features.Ask.Retrieval;
using Api.TokenBurn.Insights.Features.Costs;
using Api.TokenBurn.Insights.Features.Findings;
using Api.TokenBurn.Insights.Features.Runs;
using Api.TokenBurn.Insights.Features.Search;
using Api.TokenBurn.Insights.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        builder.Services.AddOpenApi();
        builder.Services.AddDbContext<InsightsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(InsightsDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__EFMigrationsHistory", "telemetry")));
        builder.Services.AddInsightsElasticsearchClient(builder.Configuration);
        // Embeddings chain + hybrid search. The client is resolved through a Lazy so a host
        // without Embeddings:Uri still boots and keyword search never constructs it; the hybrid
        // service resolves it only when a hybrid request reaches the vector leg, and degrades to
        // keyword-only if that resolution fails.
        builder.Services.AddEmbeddingServices(builder.Configuration);
        builder.Services.AddSingleton(sp => new Lazy<IEmbeddingClient>(sp.GetRequiredService<IEmbeddingClient>));
        builder.Services.AddSingleton(HybridRetrievalOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddSingleton<HybridTracesRetrievalService>();
        AddAskServices(builder);
        AddRateLimiting(builder);
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
        app.MapFindingsEndpoints();
        app.MapCostSummaryEndpoints();
        app.MapAskEndpoints();
        app.MapOpenApi().WithName("OpenApiDocument").RequireAuthorization(AuthorizationPolicies.InsightsRead).RequireRateLimiting("v1");
        return app;
    }

    private static void AddAskServices(WebApplicationBuilder builder)
    {
        AskOptions options = AskOptions.FromConfiguration(builder.Configuration);
        builder.Services.AddSingleton(options);
        // The default host already registers TimeProvider.System; the explicit TryAdd keeps the
        // contract visible and lets tests override it with a FakeTimeProvider.
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(sp => new AskBudget(sp.GetRequiredService<AskOptions>().MaxRequestsPerHour));
        builder.Services.AddSingleton<ContextRedactor>();
        builder.Services.AddSingleton<ChatMessageBuilder>();
        builder.Services.AddSingleton<HybridDocumentsRetrievalService>();
        builder.Services.AddSingleton<AskRetrievalService>();
        RegisterChatClient(builder, options);
        // Named client registered unconditionally but resolved only by the DeepSeek client,
        // mirroring the embeddings client: a fake-only host never creates it, so the
        // missing-endpoint guard stays lazy at client-creation time.
        builder.Services.AddHttpClient("deepseek", client =>
        {
            if (string.IsNullOrWhiteSpace(options.DeepSeekEndpoint))
                throw new InvalidOperationException("Ask:DeepSeekEndpoint must be configured when the deepseek client is used.");
            client.BaseAddress = new Uri(options.DeepSeekEndpoint);
            client.Timeout = TimeSpan.FromSeconds(120);
        }).AddStandardResilienceHandler(options =>
        {
            // A generative completion POST is NOT idempotent: retrying a lost/slow request
            // re-sends the same prompt — double billing AND double egress of the redacted
            // context. Never treat any outcome as retryable; the circuit breaker and timeouts
            // stay.
            options.Retry.ShouldHandle = _ => new ValueTask<bool>(false);
        });
    }

    private static void RegisterChatClient(WebApplicationBuilder builder, AskOptions options)
    {
        // Provider selection happens HERE, at registration time — there is no runtime fallback
        // to the fake client (privacy-boundary rule 7: opt-in per environment, never default).
        string provider = string.IsNullOrWhiteSpace(options.Provider) ? "fake" : options.Provider;
        switch (provider)
        {
            case "fake":
                builder.Services.AddSingleton<IChatClient, FakeChatClient>();
                break;
            case "deepseek":
                if (string.IsNullOrWhiteSpace(options.DeepSeekApiKey))
                    throw new InvalidOperationException("Ask:DeepSeekApiKey must be configured when Ask:Provider=deepseek; refusing to silently fall back to the fake client.");
                if (string.IsNullOrWhiteSpace(options.DeepSeekEndpoint))
                    throw new InvalidOperationException("Ask:DeepSeekEndpoint must be configured when Ask:Provider=deepseek.");
                builder.Services.AddSingleton<IChatClient, DeepSeekChatClient>();
                break;
            default:
                throw new InvalidOperationException($"Ask:Provider '{provider}' is not supported. Use 'fake' (default) or 'deepseek'.");
        }
    }

    private static void AddRateLimiting(WebApplicationBuilder builder)
    {
        int tokenLimit = ReadRateLimit(builder.Configuration, "Insights:RateLimit:TokenLimit", 100);
        int tokensPerPeriod = ReadRateLimit(builder.Configuration, "Insights:RateLimit:TokensPerPeriod", 100);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("v1", context => RateLimitPartition.GetTokenBucketLimiter(
                PartitionKey(context),
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = tokenLimit,
                    TokensPerPeriod = tokensPerPeriod,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
            // Ask spends provider money per request, so it gets its own smaller bucket (default
            // 5/s) under Insights:AskRateLimit:*; the per-hour per-principal budget lives in
            // Ask:Budget:* and is enforced by the handler, not this limiter.
            options.AddPolicy("ask", context => RateLimitPartition.GetTokenBucketLimiter(
                PartitionKey(context),
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = ReadRateLimit(builder.Configuration, "Insights:AskRateLimit:TokenLimit", 5),
                    TokensPerPeriod = ReadRateLimit(builder.Configuration, "Insights:AskRateLimit:TokensPerPeriod", 5),
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        });
    }

    private static string PartitionKey(HttpContext context)
        => context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

    private static int ReadRateLimit(IConfiguration configuration, string key, int fallback)
        => int.TryParse(configuration[key], out int value) ? value : fallback;
}
