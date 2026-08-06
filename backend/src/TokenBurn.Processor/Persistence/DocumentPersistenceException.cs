namespace TokenBurn.Processor.Persistence;

/// <summary>
///     A search document row that must exist after an upsert could not be found. Deliberately
///     NOT an <see cref="InvalidOperationException" />: the documents executor's per-file catch
///     treats that type as a skippable file, which would silently count this invariant failure
///     as a skipped import. This type escapes that filter and fails the command.
/// </summary>
public sealed class DocumentPersistenceException(string message) : Exception(message);
