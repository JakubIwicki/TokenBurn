namespace Api.TokenBurn.Insights.Extensions.Embeddings;

/// <summary>
///     Computes the dense vector for a set of texts via the configured embedding
///     service. Hybrid search embeds the user's query and receives its vector;
///     the list-shaped input mirrors the upstream wire format (TEI embeds an
///     array of inputs), which keeps a future batched embedder a drop-in change.
/// </summary>
public interface IEmbeddingClient
{
    Task<IReadOnlyList<float>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
