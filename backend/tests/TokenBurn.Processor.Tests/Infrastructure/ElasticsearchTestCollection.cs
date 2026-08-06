namespace TokenBurn.Processor.Tests.Infrastructure;

/// <summary>
///     Serializes the tests that share the single Elasticsearch container and the
///     <c>traces</c> index. xUnit runs test classes in parallel by default; the
///     indexer and embedder tests both rewrite the same index, so they must not
///     interleave. DisableParallelization also keeps this collection from running
///     alongside the rest of the suite.
/// </summary>
[CollectionDefinition("elasticsearch", DisableParallelization = true)]
public sealed class ElasticsearchTestCollection;
