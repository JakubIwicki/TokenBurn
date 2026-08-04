using Google.Protobuf;
using MediatR;
using OpenTelemetry.Proto.Collector.Trace.V1;
using Api.TokenBurn.Ingest.Infrastructure;
using TokenBurn.Common.Primitives;

namespace Api.TokenBurn.Ingest.Features.Traces;

public sealed class ExportTracesHandler(IEnvelopeInbox inbox) : IRequestHandler<ExportTracesCommand, Result>
{
    public Task<Result> Handle(ExportTracesCommand request, CancellationToken cancellationToken)
        => HandleAsync(request, cancellationToken);

    public async Task<Result> HandleAsync(ExportTracesCommand request, CancellationToken cancellationToken)
    {
        try
        {
            ExportTraceServiceRequest parsed = request.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                ? new JsonParser(JsonParser.Settings.Default.WithIgnoreUnknownFields(true)).Parse<ExportTraceServiceRequest>(ByteString.CopyFrom(request.Body).ToStringUtf8())
                : ExportTraceServiceRequest.Parser.ParseFrom(request.Body);
            string sessionKey = parsed.ResourceSpans.FirstOrDefault()?.Resource?.Attributes
                .FirstOrDefault(x => x.Key is "session.id" or "session_id")?.Value?.StringValue ?? string.Empty;
            string json = JsonFormatter.Default.Format(parsed);
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(request.Body)).ToLowerInvariant();
            await inbox.WriteAsync("otlp-traces", hash, json, sessionKey, cancellationToken);
            return Result.Success();
        }
        catch (InvalidProtocolBufferException)
        {
            return Result.Invalid("malformed OTLP payload");
        }
        catch (InvalidJsonException)
        {
            return Result.Invalid("malformed OTLP payload");
        }
    }
}
