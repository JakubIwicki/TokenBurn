namespace TokenBurn.Processor.Commands;

/// <summary>
///     A file the documents import cannot read as text (oversize or binary). The only
///     exception the executor's per-file catch treats as a skippable file — deliberately NOT
///     an <see cref="InvalidOperationException" />, so an infrastructure failure (embedding,
///     persistence, Elasticsearch) is never mislabeled as a bad file and swallowed.
/// </summary>
public sealed class UnreadableDocumentException(string message) : Exception(message);
