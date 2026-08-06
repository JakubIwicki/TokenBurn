namespace TokenBurn.Processor.Persistence;

/// <summary>
///     A run row that must exist after an upsert could not be found. Deliberately NOT an
///     <see cref="InvalidOperationException" />: the transcript executor's per-file catch
///     treats that type as a skippable file, which would silently count this invariant
///     failure as a skipped import. This type escapes that filter and fails the command.
/// </summary>
public sealed class RunPersistenceException(string message) : Exception(message);
