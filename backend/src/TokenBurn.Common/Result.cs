using System.Collections.ObjectModel;

namespace TokenBurn.Common;

public class Result
{
    protected Result(
        bool isSuccess,
        ResultStatus status,
        string? error,
        IReadOnlyDictionary<string, string[]>? validationErrors)
    {
        IsSuccess = isSuccess;
        Status = status;
        ErrorMessage = error;
        ValidationErrors = validationErrors;
    }

    public bool IsSuccess { get; }

    public ResultStatus Status { get; }

    public string? ErrorMessage { get; }


    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }

    public static Result Success() => new(true, ResultStatus.Ok, null, null);

    public static Result Created() => new(true, ResultStatus.Created, null, null);

    public static Result NotFound(string message) => Failure(ResultStatus.NotFound, message);

    public static Result Invalid(
        string message,
        IReadOnlyDictionary<string, string[]>? validationErrors = null) =>
        Failure(ResultStatus.Invalid, message, validationErrors);

    public static Result Conflict(string message) => Failure(ResultStatus.Conflict, message);

    public static Result Unauthorized(string message) => Failure(ResultStatus.Unauthorized, message);

    public static Result Forbidden(string message) => Failure(ResultStatus.Forbidden, message);

    public static Result Unavailable(string message) => Failure(ResultStatus.Unavailable, message);

    public static Result Error(string message) => Failure(ResultStatus.Error, message);

    private static Result Failure(
        ResultStatus status,
        string message,
        IReadOnlyDictionary<string, string[]>? validationErrors = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var errors = validationErrors is null
            ? null
            : new ReadOnlyDictionary<string, string[]>(
                new Dictionary<string, string[]>(validationErrors, StringComparer.Ordinal));

        return new Result(false, status, message, errors);
    }
}

public sealed class Result<T> : Result
{
    private Result(
        bool isSuccess,
        ResultStatus status,
        T? value,
        string? error,
        IReadOnlyDictionary<string, string[]>? validationErrors)
        : base(isSuccess, status, error, validationErrors)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value) => new(true, ResultStatus.Ok, value, null, null);

    public static Result<T> Created(T value) => new(true, ResultStatus.Created, value, null, null);

    public new static Result<T> NotFound(string message) => Failure(ResultStatus.NotFound, message);

    public new static Result<T> Invalid(
        string message,
        IReadOnlyDictionary<string, string[]>? validationErrors = null) =>
        Failure(ResultStatus.Invalid, message, validationErrors);

    public new static Result<T> Conflict(string message) => Failure(ResultStatus.Conflict, message);

    public new static Result<T> Unauthorized(string message) => Failure(ResultStatus.Unauthorized, message);

    public new static Result<T> Forbidden(string message) => Failure(ResultStatus.Forbidden, message);

    public new static Result<T> Unavailable(string message) => Failure(ResultStatus.Unavailable, message);

    public new static Result<T> Error(string message) => Failure(ResultStatus.Error, message);

    private static Result<T> Failure(
        ResultStatus status,
        string message,
        IReadOnlyDictionary<string, string[]>? validationErrors = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var errors = validationErrors is null
            ? null
            : new ReadOnlyDictionary<string, string[]>(
                new Dictionary<string, string[]>(validationErrors, StringComparer.Ordinal));

        return new Result<T>(false, status, default, message, errors);
    }
}
