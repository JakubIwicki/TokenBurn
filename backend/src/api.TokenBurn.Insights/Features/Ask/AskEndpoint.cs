using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TokenBurn.Common.Security;

namespace Api.TokenBurn.Insights.Features.Ask;

public static class AskEndpoint
{
    public static IEndpointRouteBuilder MapAskEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ask", HandleAsync)
            .WithName("Ask")
            .RequireAuthorization(AuthorizationPolicies.AskInvoke)
            .RequireRateLimiting("ask")
            .Accepts<AskRequest>("application/json")
            .Produces<AskResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        AskRequest request,
        ClaimsPrincipal principal,
        IMediator mediator,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        string? sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(sub))
        {
            // Fail closed: an unidentifiable principal must never be charged, retrieved for or
            // sent to a provider. AskInvoke tokens always carry a sub; this is defensive only.
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "The principal could not be identified.");
        }

        try
        {
            AskQuery query = new(request.Question, request.Model, request.Persona, request.Source, request.Status, request.From, request.To, sub);
            AskResponse response = await mediator.Send(query, cancellationToken);
            return Results.Ok(response);
        }
        catch (ValidationException exception)
        {
            return Results.BadRequest(new { errors = exception.Errors.Select(e => e.ErrorMessage) });
        }
        catch (AskBudgetExceededException)
        {
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Ask budget exceeded", detail: "The per-principal ask budget is exhausted; retry later.");
        }
        catch (Exception exception)
        {
            // Never leak prompt/context (or provider) text to the caller; the detail is logged
            // server-side for diagnosis (privacy-boundary rule 7).
            ILogger logger = loggerFactory.CreateLogger("Api.TokenBurn.Insights.Features.Ask.AskEndpoint");
            logger.LogError(exception, "Ask request failed for principal {Sub}.", sub);
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Ask failed", detail: "The request could not be processed.");
        }
    }
}
