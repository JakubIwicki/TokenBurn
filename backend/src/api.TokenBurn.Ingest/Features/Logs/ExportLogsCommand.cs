using MediatR;
using TokenBurn.Common.Primitives;

namespace Api.TokenBurn.Ingest.Features.Logs;

public sealed record ExportLogsCommand(byte[] Body, string ContentType) : IRequest<Result>;
