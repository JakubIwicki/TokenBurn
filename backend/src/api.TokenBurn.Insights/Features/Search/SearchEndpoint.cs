using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TokenBurn.Common.Pagination;
using TokenBurn.Common.Security;

namespace Api.TokenBurn.Insights.Features.Search;

public static class SearchEndpoint
{
    private const int DefaultLimit = 20;

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/search", HandleAsync)
            .WithName("Search")
            .RequireAuthorization(AuthorizationPolicies.InsightsRead)
            .RequireRateLimiting("v1")
            .Produces<SearchResponse>(StatusCodes.Status200OK);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] SearchQueryParameters parameters,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            string? cursor = parameters.Cursor;
            bool cursorIsValid = cursor is null || (parameters.Mode == "hybrid"
                ? HybridCursorCodec.TryParse(cursor, out _)
                : CursorCodec.TryDecode(cursor, out _, out _));
            if (!cursorIsValid)
                throw new ValidationException("Invalid query parameters.", [new ValidationFailure("cursor", "cursor is invalid.")]);

            SearchQuery query = new(parameters.Q, parameters.Mode, parameters.Model, parameters.Persona, parameters.Source, parameters.Status, parameters.From, parameters.To, cursor, parameters.Limit ?? DefaultLimit);
            SearchResponse response = await mediator.Send(query, cancellationToken);
            return Results.Ok(response);
        }
        catch (ValidationException exception)
        {
            return Results.BadRequest(new { errors = exception.Errors.Select(e => e.ErrorMessage) });
        }
    }
}
