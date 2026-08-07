using Microsoft.Extensions.Configuration;

namespace TokenBurn.Processor.SelfTelemetry;

/// <summary>
///     Tunables for the Processor's self-instrumentation emitter, read from the
///     <c>SelfTelemetry:</c> config section with raw <see cref="IConfiguration.GetValue{T}" />
///     calls (no IOptions), mirroring <c>AggregateOptions</c>. Default-off: the emitter is
///     auxiliary instrumentation and must never prevent the host from booting.
/// </summary>
public sealed record SelfTelemetryOptions(
    bool Enabled,
    int IntervalMinutes,
    string IdentityUrl,
    string IngestUrl,
    string ClientId,
    string ClientSecret)
{
    public static SelfTelemetryOptions FromConfiguration(IConfiguration configuration)
    {
        bool enabled = configuration.GetValue("SelfTelemetry:Enabled", false);
        int intervalMinutes = configuration.GetValue("SelfTelemetry:IntervalMinutes", 60);
        string identityUrl = configuration.GetValue("SelfTelemetry:IdentityUrl", "http://identity:8080") ?? "http://identity:8080";
        string ingestUrl = configuration.GetValue("SelfTelemetry:IngestUrl", "http://ingest:8080") ?? "http://ingest:8080";
        string clientId = configuration.GetValue("SelfTelemetry:ClientId", "tokenburn-self") ?? "tokenburn-self";
        string clientSecret = configuration.GetValue("SelfTelemetry:ClientSecret", "") ?? "";
        if (enabled && string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException(
                "SelfTelemetry:ClientSecret must be configured when SelfTelemetry:Enabled is true. " +
                "An enabled emitter cannot authenticate with the Identity server without a client secret.");
        if (enabled && intervalMinutes < 1)
            throw new InvalidOperationException(
                "SelfTelemetry:IntervalMinutes must be at least 1.");

        return new SelfTelemetryOptions(enabled, intervalMinutes, identityUrl, ingestUrl, clientId, clientSecret);
    }
}
