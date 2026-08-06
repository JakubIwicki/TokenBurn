using Microsoft.Extensions.Configuration;

namespace TokenBurn.Processor.Documents;

/// <summary>
///     Tunables for the documents import pipeline. Read from the <c>Documents:</c> config
///     section with raw <see cref="IConfiguration.GetValue{T}" /> calls (no IOptions),
///     mirroring <c>WasteDetectionOptions</c>. <see cref="ChunkMaxTokens" /> and
///     <see cref="TokenCharsPerToken" /> drive the deterministic chunker;
///     <see cref="MaxFileBytes" /> is the oversize guard; <see cref="EmbeddingBatchSize" />
///     bounds the Elasticsearch bulk write (the embedding client embeds one text per call).
/// </summary>
public sealed record DocumentsOptions(
    int ChunkMaxTokens,
    int TokenCharsPerToken,
    long MaxFileBytes,
    int EmbeddingBatchSize)
{
    public static DocumentsOptions FromConfiguration(IConfiguration configuration) => new(
        configuration.GetValue("Documents:ChunkMaxTokens", 512),
        configuration.GetValue("Documents:TokenCharsPerToken", 4),
        configuration.GetValue("Documents:MaxFileBytes", 5_000_000L),
        configuration.GetValue("Documents:EmbeddingBatchSize", 64));
}
