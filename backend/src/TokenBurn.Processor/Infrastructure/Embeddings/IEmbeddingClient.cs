namespace TokenBurn.Processor.Infrastructure.Embeddings;

/// <summary>
///     Computes the dense vector for a set of texts via the configured embedding
///     service. The embedder sends a single run summary and receives its vector;
///     the list-shaped input mirrors the upstream wire format (TEI embeds an
///     array of inputs), which keeps a future batched embedder a drop-in change.
/// </summary>
public interface IEmbeddingClient
{
    Task<IReadOnlyList<float>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
