namespace TokenBurn.Processor.Infrastructure.Embeddings;

/// <summary>
///     A non-success response (or an unusable payload) from the text-embeddings
///     endpoint. Deliberately typed — mirroring
///     <see cref="TokenBurn.Processor.Persistence.RunPersistenceException" /> — so
///     a caller can distinguish an upstream embedding failure from the generic
///     operation failures the rest of the pipeline raises.
/// </summary>
public sealed class EmbeddingException(string message) : Exception(message);
