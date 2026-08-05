using TokenBurn.Contracts;

namespace TokenBurn.Processor.Infrastructure.Indexing;

/// <summary>
///     Writes a priced run into Elasticsearch. Implementations must be
///     idempotent on <c>_id = run.Id</c>: indexing the same run twice leaves
///     exactly one document.
/// </summary>
public interface IRunIndexer
{
    Task IndexAsync(PricedRun run, CancellationToken cancellationToken);
}
