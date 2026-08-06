using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TokenBurn.Common.Pagination;
using TokenBurn.Common.Security;

namespace Api.TokenBurn.Insights.Features.Runs;

public static class RunsEndpoint
{
    private const int DefaultLimit = 20;

    public static IEndpointRouteBuilder MapRunsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/runs", HandleListAsync)
            .WithName("Runs")
            .RequireAuthorization(AuthorizationPolicies.InsightsRead)
            .RequireRateLimiting("v1")
            .Produces<RunsResponse>(StatusCodes.Status200OK);
        app.MapGet("/api/runs/{id:guid}", HandleDetailAsync)
            .WithName("RunsDetail")
            .RequireAuthorization(AuthorizationPolicies.InsightsRead)
            .RequireRateLimiting("v1")
            .Produces<RunDetailResponse>(StatusCodes.Status200OK);
        return app;
    }

    private static async Task<IResult> HandleListAsync(
        [AsParameters] RunsQueryParameters parameters,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            string? cursor = parameters.Cursor;
            if (cursor is not null && !CursorCodec.TryDecode(cursor, out _, out _))
                throw new ValidationException("Invalid query parameters.", [new ValidationFailure("cursor", "cursor is invalid.")]);

            RunsQuery query = new(parameters.From, parameters.To, parameters.Model, parameters.Persona, parameters.MinCost, cursor, parameters.Limit ?? DefaultLimit);
            RunsResponse response = await mediator.Send(query, cancellationToken);
            return Results.Ok(response);
        }
        catch (ValidationException exception)
        {
            return Results.BadRequest(new { errors = exception.Errors.Select(e => e.ErrorMessage) });
        }
    }

    private static async Task<IResult> HandleDetailAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        RunDetailResponse? response = await mediator.Send(new RunDetailQuery(id), cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }
}
