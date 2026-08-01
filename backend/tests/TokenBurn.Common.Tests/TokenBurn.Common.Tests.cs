using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using TokenBurn.Common;

namespace TokenBurn.Common.Tests;

public sealed class TokenBurnCommonTests
{
    [Fact]
    public void Result_SuccessAndNotFoundMapping_AreCorrect()
    {
        var success = Result.Success();
        var notFound = Result.NotFound("missing");

        Assert.True(success.IsSuccess);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ProblemHttpResult>(ResultHttpMapper.Map(notFound)).StatusCode);
    }
}
