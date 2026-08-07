using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace TokenBurn.Processor.SelfTelemetry;

/// <summary>
///     Builds the OTLP/JSON trace request for one self-telemetry tick, mirroring
///     <c>TokenBurn.Collector</c>'s hand-rolled <see cref="ExportTraceServiceRequest" />
///     construction and Unix-nano arithmetic. The span is the Processor reporting its own
///     pipeline activity back through its own telemetry pipeline: the resource carries
///     <c>tokenburn.source = "tokenburn-self"</c> and a unique per-tick
///     <c>session.id</c>, so the adapter (Slice A) keys a distinct run with
///     <c>source = "tokenburn-self"</c> per tick. No <c>tokenburn.cost_usd</c> is emitted —
///     the pipeline prices the run itself; cost is a pure function of usage.
/// </summary>
public sealed class OtlpJsonBuilder
{
    private const string Source = "tokenburn-self";
    private const string Handle = "tokenburn-self";
    private const string Persona = "processor";
    private const string Status = "ok";
    private const string Model = "deepseek-v4-flash";

    /// <summary>
    ///     Builds one tick's request. The four usage counters carry the tick's WINDOW ACTIVITY as
    ///     synthetic token-equivalents (the run is a pipeline summary, not a real LLM call):
    ///     input_tokens = runs in window, cache_read_tokens = messages in window,
    ///     cache_write_tokens = waste findings in window, output_tokens = 0.
    /// </summary>
    public SelfTelemetryJson BuildFor(
        DateTimeOffset tickStart, DateTimeOffset tickEnd,
        long runsInWindow, long messagesInWindow, long findingsInWindow, long tickSequence)
    {
        string sessionId = $"tokenburn-self-{tickStart.ToUnixTimeMilliseconds()}-{tickSequence}";
        ExportTraceServiceRequest request = new();
        ResourceSpans resourceSpans = new()
        {
            Resource = new Resource
            {
                Attributes =
                {
                    Attribute("session.id", sessionId),
                    Attribute("tokenburn.source", Source)
                }
            }
        };
        ScopeSpans scopeSpans = new();
        scopeSpans.Spans.Add(CreateSpan(tickStart, tickEnd, runsInWindow, messagesInWindow, findingsInWindow));
        resourceSpans.ScopeSpans.Add(scopeSpans);
        request.ResourceSpans.Add(resourceSpans);
        return new SelfTelemetryJson(sessionId, JsonFormatter.Default.Format(request));
    }

    private static Span CreateSpan(
        DateTimeOffset tickStart, DateTimeOffset tickEnd,
        long runsInWindow, long messagesInWindow, long findingsInWindow)
    {
        long start = ToUnixNano(tickStart);
        long end = ToUnixNano(tickEnd);
        Span span = new() { Name = Handle, StartTimeUnixNano = (ulong)start, EndTimeUnixNano = (ulong)end };
        span.Attributes.Add(Attribute("tokenburn.handle", Handle));
        span.Attributes.Add(Attribute("tokenburn.persona", Persona));
        span.Attributes.Add(Attribute("gen_ai.request.model", Model));
        span.Attributes.Add(Attribute("tokenburn.status", Status));
        span.Attributes.Add(Attribute("gen_ai.usage.input_tokens", runsInWindow));
        span.Attributes.Add(Attribute("gen_ai.usage.cache_read_tokens", messagesInWindow));
        span.Attributes.Add(Attribute("gen_ai.usage.cache_write_tokens", findingsInWindow));
        span.Attributes.Add(Attribute("gen_ai.usage.output_tokens", 0L));
        return span;
    }

    private static long ToUnixNano(DateTimeOffset timestamp)
        => checked((timestamp.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) * 100);

    private static KeyValue Attribute(string key, string value) => new() { Key = key, Value = new AnyValue { StringValue = value } };
    private static KeyValue Attribute(string key, long value) => new() { Key = key, Value = new AnyValue { IntValue = value } };
}

public sealed record SelfTelemetryJson(string SessionId, string Json);
