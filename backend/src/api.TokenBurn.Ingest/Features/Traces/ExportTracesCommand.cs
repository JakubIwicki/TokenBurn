using MediatR;
using TokenBurn.Common.Primitives;

namespace Api.TokenBurn.Ingest.Features.Traces;

public sealed record ExportTracesCommand(byte[] Body, string ContentType) : IRequest<Result>;
