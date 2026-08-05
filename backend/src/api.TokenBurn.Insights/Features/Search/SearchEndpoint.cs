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
            .RequireAuthorization(AuthorizationPolicies.InsightsRead)
            .RequireRateLimiting("v1");
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            SearchQuery query = Bind(context.Request.Query);
            SearchResponse response = await mediator.Send(query, cancellationToken);
            return Results.Ok(response);
        }
        catch (ValidationException exception)
        {
            return Results.BadRequest(new { errors = exception.Errors.Select(e => e.ErrorMessage) });
        }
    }

    private static SearchQuery Bind(IQueryCollection query)
    {
        int limit = ParseInt(query["limit"].FirstOrDefault(), DefaultLimit);
        DateTimeOffset? from = ParseDateTimeOffset(query["from"].FirstOrDefault(), "from");
        DateTimeOffset? to = ParseDateTimeOffset(query["to"].FirstOrDefault(), "to");
        string? cursor = query["cursor"].FirstOrDefault();
        if (cursor is not null && !CursorCodec.TryDecode(cursor, out _, out _))
            throw new ValidationException("Invalid query parameters.", [new ValidationFailure("cursor", "cursor is invalid.")]);

        return new SearchQuery(
            query["q"].FirstOrDefault(),
            query["mode"].FirstOrDefault(),
            query["model"].FirstOrDefault(),
            query["persona"].FirstOrDefault(),
            query["source"].FirstOrDefault(),
            query["status"].FirstOrDefault(),
            from,
            to,
            cursor,
            limit);
    }

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out int parsed) ? parsed : fallback;

    private static DateTimeOffset? ParseDateTimeOffset(string? value, string paramName)
    {
        if (value is null)
            return null;
        if (!DateTimeOffset.TryParse(value, out DateTimeOffset parsed))
            throw new ValidationException("Invalid query parameters.", [new ValidationFailure(paramName, $"{paramName} is invalid.")]);
        return parsed;
    }
}
