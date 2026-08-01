using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace TokenBurn.Common.Messaging;

public sealed class TimingBehavior<TRequest, TResponse>(ILogger<TimingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await next(cancellationToken);
        stopwatch.Stop();
        logger.LogInformation(
            "Handled {RequestName} in {ElapsedMilliseconds} ms",
            typeof(TRequest).Name,
            stopwatch.Elapsed.TotalMilliseconds);
        return response;
    }
}
