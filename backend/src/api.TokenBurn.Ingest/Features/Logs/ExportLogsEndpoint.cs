using Google.Protobuf;
using MediatR;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using OpenTelemetry.Proto.Collector.Logs.V1;
using TokenBurn.Common.Primitives;
using TokenBurn.Common.Security;
using TokenBurn.Common.Web;

namespace Api.TokenBurn.Ingest.Features.Logs;

public static class ExportLogsEndpoint
{
    public static IEndpointRouteBuilder MapExportLogs(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/logs", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.TelemetryWrite)
            .RequireRateLimiting("v1");
        return app;
    }

    private static async Task HandleAsync(HttpContext context, IMediator mediator, CancellationToken cancellationToken)
    {
        const long maximumBodySize = 10 * 1024 * 1024;
        IHttpMaxRequestBodySizeFeature? bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is not null && bodySizeFeature.IsReadOnly is false)
            bodySizeFeature.MaxRequestBodySize = maximumBodySize;
        if (context.Request.ContentLength > maximumBodySize)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        byte[] body;
        await using (MemoryStream stream = new())
        {
            await context.Request.Body.CopyToAsync(stream, cancellationToken);
            body = stream.ToArray();
        }

        string contentType = context.Request.ContentType ?? "application/x-protobuf";
        Result result = await mediator.Send(new ExportLogsCommand(body, contentType), cancellationToken);
        if (!result.IsSuccess)
        {
            await ResultHttpMapper.Map(result).ExecuteAsync(context);
            return;
        }

        ExportLogsServiceResponse response = new();
        context.Response.StatusCode = StatusCodes.Status200OK;
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(Google.Protobuf.JsonFormatter.Default.Format(response), cancellationToken);
        }
        else
        {
            context.Response.ContentType = "application/x-protobuf";
            await context.Response.Body.WriteAsync(response.ToByteArray(), cancellationToken);
        }
    }
}
