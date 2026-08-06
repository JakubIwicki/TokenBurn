using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TokenBurn.Common.Security;

namespace Api.TokenBurn.Insights.Features.Costs;

public static class CostsEndpoint
{
    private const int DefaultLimit = 30;

    public static IEndpointRouteBuilder MapCostSummaryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/costs/summary", HandleAsync)
            .WithName("CostsSummary")
            .RequireAuthorization(AuthorizationPolicies.InsightsRead)
            .RequireRateLimiting("v1")
            .Produces<CostSummaryResponse>(StatusCodes.Status200OK);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] CostsQueryParameters parameters,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            CostsQuery query = new(parameters.From, parameters.To, parameters.GroupBy, parameters.Limit ?? DefaultLimit);
            CostSummaryResponse response = await mediator.Send(query, cancellationToken);
            return Results.Ok(response);
        }
        catch (ValidationException exception)
        {
            return Results.BadRequest(new { errors = exception.Errors.Select(e => e.ErrorMessage) });
        }
    }
}
