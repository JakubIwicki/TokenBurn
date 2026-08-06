using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Api.TokenBurn.Insights.Extensions.Embeddings;

/// <summary>
///     Registers the embedding options, the <c>embeddings</c> named HttpClient
///     (BaseAddress + timeout from <see cref="EmbeddingsOptions" />) and the
///     <see cref="IEmbeddingClient" />. The Uri is validated at client-creation
///     time, not registration time, so a host with embeddings disabled — the
///     default — still boots without an endpoint configured; hybrid search
///     resolves the client lazily and skips the vector leg in that state.
/// </summary>
public static class EmbeddingServiceExtensions
{
    public static IServiceCollection AddEmbeddingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        EmbeddingsOptions options = EmbeddingsOptions.FromConfiguration(configuration);
        services.AddSingleton(options);
        services.AddHttpClient("embeddings", client =>
        {
            if (string.IsNullOrWhiteSpace(options.Uri))
                throw new InvalidOperationException("Embeddings:Uri must be configured when embeddings are enabled.");
            client.BaseAddress = new System.Uri(options.Uri);
            client.Timeout = options.Timeout;
        });
        services.AddSingleton<IEmbeddingClient, TextEmbeddingsInferenceClient>();
        return services;
    }
}
