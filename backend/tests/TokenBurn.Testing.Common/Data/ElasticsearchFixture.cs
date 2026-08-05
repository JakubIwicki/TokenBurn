using Elastic.Clients.Elasticsearch;
using Testcontainers.Elasticsearch;

namespace TokenBurn.Testing.Common.Data;

/// <summary>
///     Shares one real Elasticsearch 9.0.0 container for integration tests.
///     Mirrors the compose stack's working environment — single-node discovery,
///     security disabled, bounded heap — so the cluster boots deterministically
///     over plain HTTP without TLS or credentials.
/// </summary>
public sealed class ElasticsearchFixture : IAsyncLifetime
{
    private const string Image = "docker.elastic.co/elasticsearch/elasticsearch:9.0.0";
    private const string EsJavaOpts = "-Xms512m -Xmx512m";
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);

    public const string TracesIndex = "traces";

    private readonly ElasticsearchContainer _container;

    public ElasticsearchFixture()
    {
        _container = new ElasticsearchBuilder(Image)
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("xpack.security.enabled", "false")
            .WithEnvironment("xpack.security.http.ssl.enabled", "false")
            .WithEnvironment("ES_JAVA_OPTS", EsJavaOpts)
            .Build();
    }

    public Uri Uri { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartTimeout);
        await _container.StartAsync(timeout.Token);
        Uri = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(9200)}");
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public ElasticsearchClient CreateClient() =>
        new(new ElasticsearchClientSettings(Uri));
}
