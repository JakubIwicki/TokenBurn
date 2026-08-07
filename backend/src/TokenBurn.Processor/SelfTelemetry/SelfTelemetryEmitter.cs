using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TokenBurn.Processor.Persistence;

namespace TokenBurn.Processor.SelfTelemetry;

/// <summary>
///     Emits one OTLP/JSON trace per interval reporting the Processor's own pipeline activity
///     back through its own telemetry pipeline (source <c>tokenburn-self</c>), so the Processor
///     appears in its own console. Gated on <c>SelfTelemetry:Enabled</c> (default-off) — the
///     emitter is auxiliary instrumentation and a silent no-op when disabled. The first tick
///     waits <see cref="FirstEmitDelay" /> so the pipeline is warm. A failed tick is logged and
///     the loop continues; only cancellation stops it (a failed self-tick must never kill the
///     host).
///     <para>
///         The window query columns: <c>agent_runs.started_at</c> (runs in window),
///         <c>agent_messages.occurred_at</c> (messages in window), and
///         <c>waste_findings.detected_at</c> (findings in window) — each counted with
///         <c>started_at/occurred_at/detected_at &gt;= windowStart</c>.
///     </para>
/// </summary>
public sealed class SelfTelemetryEmitter(
    IServiceScopeFactory scopeFactory,
    SelfTelemetryOptions options,
    SelfTelemetryTokenClient tokenClient,
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    ILogger<SelfTelemetryEmitter> logger) : BackgroundService
{
    private static readonly TimeSpan FirstEmitDelay = TimeSpan.FromSeconds(15);
    private const string IngestClientName = "self-telemetry";
    private readonly OtlpJsonBuilder _builder = new();

    // The window the next tick reports; initialized to process start (the emitter is
    // constructed at host startup) so the first tick summarizes everything since boot.
    private DateTimeOffset _lastWindowStart = timeProvider.GetUtcNow();
    private long _tickSequence;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
            return;

        try
        {
            await Task.Delay(FirstEmitDelay, timeProvider, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await EmitOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Self-telemetry tick failed; continuing with the next interval.");
                }
                await Task.Delay(TimeSpan.FromMinutes(options.IntervalMinutes), timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    /// <summary>
    ///     Runs ONE tick: queries the window activity since the last tick, builds the OTLP/JSON
    ///     payload, acquires a bearer token, and POSTs it to the ingest endpoint. The window
    ///     advances to the tick end whether the POST succeeds or fails — a failed POST's counts
    ///     are dropped, not retried. On the token-fetch failure path the window does NOT advance:
    ///     the tick aborts before <c>_lastWindowStart</c> moves, so the next tick covers the merged
    ///     period and the counts are not lost.
    /// </summary>
    public async Task EmitOnceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset tickStart = _lastWindowStart;
        DateTimeOffset tickEnd = timeProvider.GetUtcNow();

        using IServiceScope scope = scopeFactory.CreateScope();
        TelemetryDbContext db = scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();
        long runsInWindow = await db.AgentRuns.CountAsync(
            run => run.StartedAt != null && run.StartedAt.Value >= tickStart, cancellationToken);
        long messagesInWindow = await db.AgentMessages.CountAsync(
            message => message.OccurredAt >= tickStart, cancellationToken);
        long findingsInWindow = await db.WasteFindings.CountAsync(
            finding => finding.DetectedAt >= tickStart, cancellationToken);

        SelfTelemetryJson payload = _builder.BuildFor(
            tickStart, tickEnd, runsInWindow, messagesInWindow, findingsInWindow, _tickSequence);
        _tickSequence++;

        string token = await tokenClient.GetTokenAsync(cancellationToken);
        HttpClient client = httpClientFactory.CreateClient(IngestClientName);
        using StringContent content = new(payload.Json, Encoding.UTF8, "application/json");
        using HttpRequestMessage message = new(HttpMethod.Post, $"{options.IngestUrl.TrimEnd('/')}/v1/traces")
        {
            Content = content
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Self-telemetry POST failed: session={SessionId} exception={Exception}: {Message}",
                payload.SessionId, exception.GetType().Name, exception.Message);
            _lastWindowStart = tickEnd;
            return;
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
                logger.LogInformation("Self-telemetry emitted: session={SessionId} runs={Runs} messages={Messages} findings={Findings}",
                    payload.SessionId, runsInWindow, messagesInWindow, findingsInWindow);
            else
                logger.LogWarning("Self-telemetry POST failed: session={SessionId} status={Status} {Reason}",
                    payload.SessionId, (int)response.StatusCode, response.ReasonPhrase);
        }
        _lastWindowStart = tickEnd;
    }
}
