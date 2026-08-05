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
            .RequireAuthorization(AuthorizationPolicies.InsightsRead)
            .RequireRateLimiting("v1");
        app.MapGet("/api/runs/{id:guid}", HandleDetailAsync)
            .RequireAuthorization(AuthorizationPolicies.InsightsRead)
            .RequireRateLimiting("v1");
        return app;
    }

    private static async Task<IResult> HandleListAsync(
        HttpContext context,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            RunsQuery query = Bind(context.Request.Query);
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

    private static RunsQuery Bind(IQueryCollection query)
    {
        int limit = ParseInt(query["limit"].FirstOrDefault(), DefaultLimit);
        DateTimeOffset? from = ParseDateTimeOffset(query["from"].FirstOrDefault(), "from");
        DateTimeOffset? to = ParseDateTimeOffset(query["to"].FirstOrDefault(), "to");
        decimal? minCost = ParseDecimal(query["minCost"].FirstOrDefault());
        string? cursor = query["cursor"].FirstOrDefault();
        if (cursor is not null && !CursorCodec.TryDecode(cursor, out _, out _))
            throw new ValidationException("Invalid query parameters.", [new ValidationFailure("cursor", "cursor is invalid.")]);

        return new RunsQuery(
            from,
            to,
            query["model"].FirstOrDefault(),
            query["persona"].FirstOrDefault(),
            minCost,
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

    private static decimal? ParseDecimal(string? value)
        => decimal.TryParse(value, out decimal parsed) ? parsed : null;
}
