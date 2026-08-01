using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TokenBurn.Processor;

public static class ProcessorExtensions
{
    public static WebApplicationBuilder AddProcessorServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks();
        builder.Services.AddHostedService<ConsumerPlaceholderWorker>();
        return builder;
    }

    public static WebApplication MapProcessorEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready");
        return app;
    }
}

internal sealed class ConsumerPlaceholderWorker(
    ILogger<ConsumerPlaceholderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Processor started — consumers land in Phase 2");
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
