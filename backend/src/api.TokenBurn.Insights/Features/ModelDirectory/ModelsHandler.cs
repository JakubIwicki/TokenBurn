using Api.TokenBurn.Insights.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Api.TokenBurn.Insights.Features.ModelDirectory;

public sealed class ModelsHandler(InsightsDbContext db) : IRequestHandler<ModelsQuery, ModelsDirectoryResponse>
{
    public Task<ModelsDirectoryResponse> Handle(ModelsQuery request, CancellationToken cancellationToken)
        => HandleAsync(request, cancellationToken);

    public async Task<ModelsDirectoryResponse> HandleAsync(ModelsQuery request, CancellationToken cancellationToken)
    {
        List<ModelPriceReadModel> current = await db.ModelPrices.AsNoTracking()
            .Where(model => model.EffectiveTo == null)
            .OrderBy(model => model.Slug)
            .ThenBy(model => model.Service)
            .ToListAsync(cancellationToken);

        return new ModelsDirectoryResponse
        {
            Models = current.Select(ToEntry).ToList()
        };
    }

    private static ModelDirectoryEntry ToEntry(ModelPriceReadModel model) => new()
    {
        Slug = model.Slug,
        Provider = model.Service,
        ContextWindow = model.ContextWindow,
        InputPerMtok = model.InputPerMtok,
        CacheReadPerMtok = model.CacheReadPerMtok,
        CacheWritePerMtok = model.CacheWritePerMtok,
        OutputPerMtok = model.OutputPerMtok
    };
}
