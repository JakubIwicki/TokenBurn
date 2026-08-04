using Microsoft.AspNetCore.Http;
using TokenBurn.Common.Primitives;

namespace TokenBurn.Common.Web;

public static class ResultHttpMapper
{
    public static IResult Map(Result result) =>
        result.Status switch
        {
            ResultStatus.Ok => Results.NoContent(),
            ResultStatus.Created => Results.NoContent(),
            _ => MapFailure(result)
        };

    public static IResult Map<T>(Result<T> result) =>
        result.Status switch
        {
            ResultStatus.Ok => Results.Ok(result.Value),
            ResultStatus.Created => Results.Created(string.Empty, result.Value),
            _ => MapFailure(result)
        };

    private static IResult MapFailure(Result result)
    {
        var statusCode = result.Status switch
        {
            ResultStatus.NotFound => StatusCodes.Status404NotFound,
            ResultStatus.Invalid => StatusCodes.Status400BadRequest,
            ResultStatus.Conflict => StatusCodes.Status409Conflict,
            ResultStatus.Unauthorized => StatusCodes.Status401Unauthorized,
            ResultStatus.Forbidden => StatusCodes.Status403Forbidden,
            ResultStatus.Unavailable => StatusCodes.Status503ServiceUnavailable,
            ResultStatus.Error => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };

        if (result.ValidationErrors is null)
            return Results.Problem(
                statusCode: statusCode,
                title: result.Status.ToString(),
                detail: result.ErrorMessage);

        return Results.ValidationProblem(
            result.ValidationErrors,
            statusCode: statusCode,
            title: result.Status.ToString(),
            detail: result.ErrorMessage);
    }
}
