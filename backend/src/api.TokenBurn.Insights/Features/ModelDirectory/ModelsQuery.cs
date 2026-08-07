using MediatR;

namespace Api.TokenBurn.Insights.Features.ModelDirectory;

/// <summary>
///     The anonymous model directory (privacy-boundary rule 8): every currently
///     effective price-registry row (effective_to is null) projected to
///     allow-listed public fields only.
/// </summary>
public sealed record ModelsQuery() : IRequest<ModelsDirectoryResponse>;
