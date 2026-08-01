using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace TokenBurn.Common.Messaging;

public static class TokenBurnMediatRExtensions
{
    public static IServiceCollection AddTokenBurnMediatR(
        this IServiceCollection services,
        params Assembly[] handlerAssemblies)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssemblies(handlerAssemblies);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(TimingBehavior<,>));
        });
        services.AddValidatorsFromAssemblies(handlerAssemblies);
        return services;
    }
}
