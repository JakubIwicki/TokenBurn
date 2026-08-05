using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.Extensions.Logging;

namespace TokenBurn.Processor.Infrastructure.Indexing;

/// <summary>
///     Idempotently PUTs the <c>traces</c> index template. ES being down
///     degrades only indexing — the raw consumer publishes to
///     <c>telemetry.priced</c> regardless; the index consumer retries the
///     template until the cluster answers or the host is stopped.
/// </summary>
public sealed class SearchIndexTemplateInitializer(
    ElasticsearchClient client,
    ILogger<SearchIndexTemplateInitializer> logger)
{
    private const string TemplateName = "traces";
    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8)
    ];

    public async Task EnsureTemplateAsync(CancellationToken cancellationToken)
    {
        PutIndexTemplateRequest request = BuildRequest();
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                PutIndexTemplateResponse response = await client.Indices.PutIndexTemplateAsync(request, cancellationToken);
                if (response.IsValidResponse)
                    return;
                throw new InvalidOperationException($"Elasticsearch rejected the traces template: {response.DebugInformation}");
            }
            catch (Exception exception) when (attempt < RetryBackoff.Length && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Failed to ensure the traces index template; retrying in {Delay}.", RetryBackoff[attempt]);
                await Task.Delay(RetryBackoff[attempt], cancellationToken);
            }
        }
    }

    private static PutIndexTemplateRequest BuildRequest() => new(TemplateName)
    {
        IndexPatterns = new[] { "traces" },
        Priority = 200,
        Template = new IndexTemplateMapping
        {
            Settings = new IndexSettings
            {
                NumberOfShards = 1,
                NumberOfReplicas = 0
            },
            Mappings = new TypeMapping
            {
                Dynamic = DynamicMapping.False,
                Properties = new Properties
                {
                    ["id"] = new KeywordProperty(),
                    ["session_id"] = new KeywordProperty(),
                    ["agent_id"] = new KeywordProperty(),
                    ["source"] = new KeywordProperty(),
                    ["external_id"] = new KeywordProperty(),
                    ["parent_run_id"] = new KeywordProperty(),
                    ["workspace"] = new KeywordProperty(),
                    ["persona"] = new KeywordProperty(),
                    ["model_slug"] = new KeywordProperty(),
                    ["service"] = new KeywordProperty(),
                    ["status"] = new KeywordProperty(),
                    ["pricing_status"] = new KeywordProperty(),
                    ["started_at"] = new DateProperty(),
                    ["ended_at"] = new DateProperty(),
                    ["input_tokens"] = new LongNumberProperty(),
                    ["cache_read_tokens"] = new LongNumberProperty(),
                    ["cache_write_tokens"] = new LongNumberProperty(),
                    ["output_tokens"] = new LongNumberProperty(),
                    ["cost_usd"] = new ScaledFloatNumberProperty { ScalingFactor = 1000000 },
                    ["reported_cost_usd"] = new ScaledFloatNumberProperty { ScalingFactor = 1000000 },
                    ["price_multiplier"] = new ScaledFloatNumberProperty { ScalingFactor = 1000 },
                    ["version"] = new IntegerNumberProperty(),
                    ["searchable_text"] = new TextProperty()
                }
            }
        }
    };
}
