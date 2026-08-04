using Google.Protobuf;
using MediatR;
using OpenTelemetry.Proto.Collector.Logs.V1;
using Api.TokenBurn.Ingest.Infrastructure;
using TokenBurn.Common.Primitives;

namespace Api.TokenBurn.Ingest.Features.Logs;

public sealed class ExportLogsHandler(IEnvelopeInbox inbox) : IRequestHandler<ExportLogsCommand, Result>
{
    public Task<Result> Handle(ExportLogsCommand request, CancellationToken cancellationToken)
        => HandleAsync(request, cancellationToken);

    public async Task<Result> HandleAsync(ExportLogsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            ExportLogsServiceRequest parsed = request.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                ? new JsonParser(JsonParser.Settings.Default.WithIgnoreUnknownFields(true)).Parse<ExportLogsServiceRequest>(ByteString.CopyFrom(request.Body).ToStringUtf8())
                : ExportLogsServiceRequest.Parser.ParseFrom(request.Body);
            string sessionKey = parsed.ResourceLogs.FirstOrDefault()?.Resource?.Attributes
                .FirstOrDefault(x => x.Key is "session.id" or "session_id")?.Value?.StringValue ?? string.Empty;
            string json = JsonFormatter.Default.Format(parsed);
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(request.Body)).ToLowerInvariant();
            await inbox.WriteAsync("otlp-logs", hash, json, sessionKey, cancellationToken);
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
