using MediatR;

namespace Api.TokenBurn.Insights.Features.ModelDirectory;

/// <summary>
///     Per-model usage totals over the public aggregate cache, summed across
///     all bucket days — anonymous (privacy-boundary rule 8).
/// </summary>
public sealed record ModelsStatsQuery() : IRequest<ModelsStatsResponse>;
