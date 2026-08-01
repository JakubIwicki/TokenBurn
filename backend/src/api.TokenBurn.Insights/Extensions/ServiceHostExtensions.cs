using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TokenBurn.Common.Messaging;

namespace Api.TokenBurn.Insights.Extensions;

public static class ServiceHostExtensions
{
    public static WebApplicationBuilder AddApiServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks();
        builder.Services.AddTokenBurnMediatR(typeof(Program).Assembly);
        builder.Services.AddProblemDetails();
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready");
        return app;
    }
}
