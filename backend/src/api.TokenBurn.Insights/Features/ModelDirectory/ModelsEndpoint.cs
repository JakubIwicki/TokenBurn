using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.TokenBurn.Insights.Features.ModelDirectory;

public static class ModelsEndpoint
{
    public static IEndpointRouteBuilder MapModelsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/models", HandleDirectoryAsync)
            .WithName("ModelsDirectory")
            .RequireRateLimiting("v1")
            .Produces<ModelsDirectoryResponse>(StatusCodes.Status200OK);
        app.MapGet("/api/models/stats", HandleStatsAsync)
            .WithName("ModelsStats")
            .RequireRateLimiting("v1")
            .Produces<ModelsStatsResponse>(StatusCodes.Status200OK);
        return app;
    }

    private static async Task<IResult> HandleDirectoryAsync(
        HttpContext httpContext,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        // Public model directory: allow-listed fields only (privacy-boundary rule 8),
        // cached for five minutes so anonymous callers never hammer the registry table.
        httpContext.Response.Headers.CacheControl = "public, max-age=300";
        ModelsDirectoryResponse response = await mediator.Send(new ModelsQuery(), cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> HandleStatsAsync(
        HttpContext httpContext,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        // Same five-minute public cache as the directory so anonymous callers never hammer the
        // in-memory aggregate cache the consumer feeds from the metrics.aggregate projection.
        httpContext.Response.Headers.CacheControl = "public, max-age=300";
        ModelsStatsResponse response = await mediator.Send(new ModelsStatsQuery(), cancellationToken);
        return Results.Ok(response);
    }
}
