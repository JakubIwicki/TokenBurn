using MediatR;
using Microsoft.Extensions.Logging;

namespace TokenBurn.Common.Messaging;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogInformation("Handling {RequestName}", requestName);
        var response = await next(cancellationToken);
        logger.LogInformation("Handled {RequestName}", requestName);
        return response;
    }
}
