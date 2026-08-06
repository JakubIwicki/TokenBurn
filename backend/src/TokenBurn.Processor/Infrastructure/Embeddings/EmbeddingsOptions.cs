using Microsoft.Extensions.Configuration;

namespace TokenBurn.Processor.Infrastructure.Embeddings;

/// <summary>
///     Tunables for the embedding chain. Read from the <c>Embeddings:</c> config
///     section with raw <see cref="IConfiguration.GetValue{T}" /> calls (no
///     IOptions), mirroring <c>WasteDetectionOptions</c>. <see cref="Dims" /> must
///     match the model served by the TEI endpoint — the <c>traces</c> template maps
///     <c>embedding</c> as a dense_vector of exactly this many dimensions.
///     <see cref="BatchSize" /> is reserved for a future batched embedder; the
///     current embedder sends one summary per run.
/// </summary>
public sealed record EmbeddingsOptions(
    string? Uri,
    int BatchSize,
    int Dims,
    TimeSpan Timeout,
    int MaxRunChars)
{
    public static EmbeddingsOptions FromConfiguration(IConfiguration configuration) => new(
        configuration["Embeddings:Uri"],
        configuration.GetValue("Embeddings:BatchSize", 64),
        configuration.GetValue("Embeddings:Dims", 384),
        TimeSpan.FromSeconds(configuration.GetValue("Embeddings:TimeoutSeconds", 120)),
        configuration.GetValue("Embeddings:MaxRunChars", 4000));
}
