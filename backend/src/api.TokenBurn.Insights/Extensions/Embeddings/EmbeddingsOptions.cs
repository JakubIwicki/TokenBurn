using Microsoft.Extensions.Configuration;

namespace Api.TokenBurn.Insights.Extensions.Embeddings;

/// <summary>
///     Tunables for the embedding chain. Read from the <c>Embeddings:</c> config
///     section with raw <see cref="IConfiguration.GetValue{T}" /> calls (no
///     IOptions), mirroring the search options. <c>Uri</c> is the TEI endpoint and
///     <c>Timeout</c> bounds a single embed call; the dims invariant lives in the
///     Processor's <c>traces</c> template, and hybrid search embeds one query at a
///     time, so the batch-oriented fields of the Processor copy are not needed here.
/// </summary>
public sealed record EmbeddingsOptions(
    string? Uri,
    TimeSpan Timeout)
{
    public static EmbeddingsOptions FromConfiguration(IConfiguration configuration) => new(
        configuration["Embeddings:Uri"],
        TimeSpan.FromSeconds(configuration.GetValue("Embeddings:TimeoutSeconds", 120)));
}
