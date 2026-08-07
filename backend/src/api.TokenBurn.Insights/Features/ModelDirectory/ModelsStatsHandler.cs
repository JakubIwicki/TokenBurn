using MediatR;

namespace Api.TokenBurn.Insights.Features.ModelDirectory;

public sealed class ModelsStatsHandler(PublicAggregateCache cache) : IRequestHandler<ModelsStatsQuery, ModelsStatsResponse>
{
    public Task<ModelsStatsResponse> Handle(ModelsStatsQuery request, CancellationToken cancellationToken)
        => HandleAsync(request, cancellationToken);

    public Task<ModelsStatsResponse> HandleAsync(ModelsStatsQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ModelsStatsResponse { Stats = cache.GetStats() });
}
