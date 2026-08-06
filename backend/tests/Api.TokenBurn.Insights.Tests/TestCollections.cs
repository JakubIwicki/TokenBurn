namespace Api.TokenBurn.Insights.Tests;

/// <summary>
///     Serializes the hybrid search suite against the shared Elasticsearch
///     container and the <c>traces</c> index, so it cannot interleave with the
///     keyword search suite that rewrites the same index.
/// </summary>
[CollectionDefinition("insights-search", DisableParallelization = true)]
public sealed class InsightsSearchTestCollection;

/// <summary>
///     Serializes tests that mutate the process-global current culture, so they
///     cannot run beside anything reading it.
/// </summary>
[CollectionDefinition("culture", DisableParallelization = true)]
public sealed class CultureTestCollection;
