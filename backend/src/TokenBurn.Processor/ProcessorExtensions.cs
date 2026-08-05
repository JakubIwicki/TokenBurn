using Confluent.Kafka;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TokenBurn.Common.Primitives;
using TokenBurn.Common.Security;
using TokenBurn.Contracts;
using TokenBurn.Processor.Adapters;
using TokenBurn.Processor.Commands;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Features.Imports;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Pricing;

namespace TokenBurn.Processor;

public static class ProcessorExtensions
{
    public static WebApplicationBuilder AddProcessorServices(this WebApplicationBuilder builder)
    {
        string connectionString = builder.Configuration.GetConnectionString("Processor")
            ?? throw new InvalidOperationException("ConnectionStrings:Processor must be configured.");
        builder.Services.AddHealthChecks();
        builder.AddTokenBurnJwtAuth();
        builder.Services.AddDbContext<TelemetryDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(TelemetryDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__EFMigrationsHistory", "telemetry")));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<OtlpGenAiAdapter>();
        builder.Services.AddSingleton<DelegateLedgerAdapter>();
        builder.Services.AddSingleton<DelegateRunLogAdapter>();
        builder.Services.AddSingleton<ClaudeCodeTranscriptAdapter>();
        builder.Services.AddSingleton<JiCachingAdapter>();
        builder.Services.AddSingleton<SourceDispatcher>();
        builder.Services.AddScoped<PricingSeeder>();
        builder.Services.AddScoped<PricingEngine>();
        builder.Services.AddScoped<AgentRunUpserter>();
        builder.Services.AddScoped<IImportCommandExecutor, ClaudeCodeTranscriptImportExecutor>();
        builder.Services.AddHostedService<TelemetryRawConsumer>();
        builder.Services.AddHostedService<ImportCommandWorker>();
        return builder;
    }

    public static async Task<WebApplication> InitializeProcessorAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TelemetryDbContext>().Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<PricingSeeder>().SeedAsync(cancellationToken);
        return app;
    }

    public static WebApplication MapProcessorEndpoints(this WebApplication app)
    {
        app.UseTokenBurnAuth();
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready");
        app.MapImportsEndpoints();
        return app;
    }
}

internal sealed class TelemetryRawConsumer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TelemetryRawConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ConsumerConfig config = new()
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"]
                ?? throw new InvalidOperationException("Kafka:BootstrapServers must be configured."),
            GroupId = "processor-telemetry-raw",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        using IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe("telemetry.raw");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result = null!;
                try
                {
                    result = consumer.Consume(stoppingToken);
                    using IServiceScope scope = scopeFactory.CreateScope();
                    SourceDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<SourceDispatcher>();
                    PricingEngine engine = scope.ServiceProvider.GetRequiredService<PricingEngine>();
                    AgentRunUpserter upserter = scope.ServiceProvider.GetRequiredService<AgentRunUpserter>();
                    foreach (NormalizedRun envelope in dispatcher.Map(result.Message.Value))
                    {
                        AgentRun run = AgentRunEnvelopeMapper.ToAgentRun(envelope);
                        Result pricing = await engine.PriceRunAsync(run, stoppingToken);
                        if (!pricing.IsSuccess)
                            logger.LogWarning("Pricing run {RunId} failed: {Error}", run.Id, pricing.ErrorMessage);
                        await upserter.UpsertAsync(run, stoppingToken);
                    }
                    consumer.Commit(result);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // Never swallow: committing past an un-processed offset silently drops the
                    // message and it is recoverable only by full replay. Crash the host so the
                    // consumer restarts at the last committed offset; the idempotent upsert makes
                    // reprocessing safe. 'result' is null when Consume itself failed, so the
                    // offset is best-effort and must never be dereferenced unconditionally.
                    logger.LogError(exception, "Failed to process telemetry.raw message at {TopicPartitionOffset}.", result?.TopicPartitionOffset);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            consumer.Close();
        }
    }
}
