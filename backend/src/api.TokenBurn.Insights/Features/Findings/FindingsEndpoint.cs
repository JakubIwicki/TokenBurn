using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TokenBurn.Common.Pagination;
using TokenBurn.Common.Security;

namespace Api.TokenBurn.Insights.Features.Findings;

public static class FindingsEndpoint
{
    private const int DefaultLimit = 20;

    public static IEndpointRouteBuilder MapFindingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/findings", HandleAsync)
            .WithName("Findings")
            .RequireAuthorization(AuthorizationPolicies.InsightsRead)
            .RequireRateLimiting("v1")
            .Produces<FindingsResponse>(StatusCodes.Status200OK);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] FindingsQueryParameters parameters,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            string? cursor = parameters.Cursor;
            // detected_at is NOT NULL, so a cursor whose key decodes to null is malformed
            // for this endpoint (only reachable via a hand-crafted empty-prefix cursor).
            if (cursor is not null &&
                (!CursorCodec.TryDecode(cursor, out DateTimeOffset? detectedAt, out _) || detectedAt is null))
                throw new ValidationException("Invalid query parameters.", [new ValidationFailure("cursor", "cursor is invalid.")]);

            FindingsQuery query = new(parameters.Kind, parameters.Severity, parameters.Acknowledged, cursor, parameters.Limit ?? DefaultLimit);
            FindingsResponse response = await mediator.Send(query, cancellationToken);
            return Results.Ok(response);
        }
        catch (ValidationException exception)
        {
            return Results.BadRequest(new { errors = exception.Errors.Select(e => e.ErrorMessage) });
        }
    }
}
