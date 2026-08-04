using Microsoft.AspNetCore.Http;
using TokenBurn.Common.Primitives;
using TokenBurn.Common.Web;
using TokenBurn.Testing.Common.Assertions;

namespace TokenBurn.Common.Tests;

public sealed class TokenBurnCommonTests
{
    public static TheoryData<ResultStatus, int> MappingCases { get; } = new()
    {
        { ResultStatus.Ok, StatusCodes.Status204NoContent },
        { ResultStatus.NotFound, StatusCodes.Status404NotFound },
        { ResultStatus.Invalid, StatusCodes.Status400BadRequest },
        { ResultStatus.Conflict, StatusCodes.Status409Conflict },
        { ResultStatus.Unauthorized, StatusCodes.Status401Unauthorized },
        { ResultStatus.Forbidden, StatusCodes.Status403Forbidden },
        { ResultStatus.Unavailable, StatusCodes.Status503ServiceUnavailable },
        { ResultStatus.Error, StatusCodes.Status500InternalServerError }
    };

    [Theory]
    [MemberData(nameof(MappingCases))]
    public void MapsResultStatus_ToHttpStatusCode(ResultStatus status, int expectedStatusCode)
    {
        Result result = CreateResult(status);

        IResult mapped = ResultHttpMapper.Map(result);

        AssertStatusCode(mapped).StatusCode.Should().Be(expectedStatusCode);
    }

    [Fact]
    public void ReturnsValue_OnSuccess()
    {
        Result<long> result = Result<long>.Success(42);

        long value = result.AssertSuccess();

        value.Should().Be(42);
    }

    private static Result CreateResult(ResultStatus status) => status switch
    {
        ResultStatus.Ok => Result.Success(),
        ResultStatus.NotFound => Result.NotFound("not found"),
        ResultStatus.Invalid => Result.Invalid("invalid"),
        ResultStatus.Conflict => Result.Conflict("conflict"),
        ResultStatus.Unauthorized => Result.Unauthorized("unauthorized"),
        ResultStatus.Forbidden => Result.Forbidden("forbidden"),
        ResultStatus.Unavailable => Result.Unavailable("unavailable"),
        ResultStatus.Error => Result.Error("error"),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static IStatusCodeHttpResult AssertStatusCode(IResult mapped)
        => mapped.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
}
