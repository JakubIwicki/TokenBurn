namespace Api.TokenBurn.Insights.Extensions.Embeddings;

/// <summary>
///     A non-success response (or an unusable payload) from the text-embeddings
///     endpoint. Deliberately typed so a caller can distinguish an upstream
///     embedding failure from the generic operation failures the rest of the
///     search pipeline raises — hybrid search degrades to keyword-only on it.
/// </summary>
public sealed class EmbeddingException(string message) : Exception(message);
