namespace TokenBurn.Testing.Common.Data;

public static class SharedElasticsearch
{
    private static readonly Lazy<Task<ElasticsearchFixture>> LazyFixture = new(async () =>
    {
        var fixture = new ElasticsearchFixture();
        await fixture.InitializeAsync();
        return fixture;
    });

    public static Task<ElasticsearchFixture> GetFixtureAsync() => LazyFixture.Value;
}
