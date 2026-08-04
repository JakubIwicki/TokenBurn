using TokenBurn.Common.Primitives;

namespace TokenBurn.Testing.Common.Assertions;

public static class ResultAssertions
{
    public static void AssertSuccess(this Result result) =>
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);

    public static T AssertSuccess<T>(this Result<T> result)
    {
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        return result.Value!;
    }

    public static string AssertFailure(this Result result, ResultStatus status)
    {
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(status);
        return result.ErrorMessage!;
    }

    public static string AssertFailure<T>(this Result<T> result, ResultStatus status)
    {
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(status);
        return result.ErrorMessage!;
    }

    public static string AssertInvalid(this Result result) => AssertFailure(result, ResultStatus.Invalid);
    public static string AssertInvalid<T>(this Result<T> result) => AssertFailure(result, ResultStatus.Invalid);
    public static string AssertNotFound(this Result result) => AssertFailure(result, ResultStatus.NotFound);
    public static string AssertNotFound<T>(this Result<T> result) => AssertFailure(result, ResultStatus.NotFound);
    public static string AssertConflict(this Result result) => AssertFailure(result, ResultStatus.Conflict);
    public static string AssertConflict<T>(this Result<T> result) => AssertFailure(result, ResultStatus.Conflict);
    public static string AssertUnauthorized(this Result result) => AssertFailure(result, ResultStatus.Unauthorized);
    public static string AssertUnauthorized<T>(this Result<T> result) => AssertFailure(result, ResultStatus.Unauthorized);
    public static string AssertForbidden(this Result result) => AssertFailure(result, ResultStatus.Forbidden);
    public static string AssertForbidden<T>(this Result<T> result) => AssertFailure(result, ResultStatus.Forbidden);
    public static string AssertUnavailable(this Result result) => AssertFailure(result, ResultStatus.Unavailable);
    public static string AssertUnavailable<T>(this Result<T> result) => AssertFailure(result, ResultStatus.Unavailable);
}
