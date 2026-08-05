using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace TokenBurn.Processor.Infrastructure;

/// <summary>
///     Registers a lazy <see cref="ElasticsearchClient" /> from
///     <c>Elasticsearch:Uri</c> / <c>Username</c> / <c>Password</c>. The Uri is
///     validated at resolution time, not registration time, so a host whose
///     configuration lacks Elasticsearch still boots — endpoint-authorization
///     tests never resolve the client and stay inert.
/// </summary>
public static class ElasticsearchServiceExtensions
{
    public static IServiceCollection AddProcessorElasticsearchClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<ElasticsearchClient>(_ =>
        {
            string uri = configuration["Elasticsearch:Uri"]
                ?? throw new InvalidOperationException("Elasticsearch:Uri must be configured.");
            string? username = configuration["Elasticsearch:Username"];

            ElasticsearchClientSettings settings = new(new Uri(uri));
            if (!string.IsNullOrWhiteSpace(username))
            {
                settings = settings.Authentication(new BasicAuthentication(
                    username,
                    configuration["Elasticsearch:Password"] ?? string.Empty));
            }

            return new ElasticsearchClient(settings);
        });
        return services;
    }
}
