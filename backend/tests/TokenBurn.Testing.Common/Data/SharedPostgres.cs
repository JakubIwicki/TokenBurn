using System.Collections.Concurrent;

namespace TokenBurn.Testing.Common.Data;

public static class SharedPostgres
{
    private static readonly Lazy<Task<PostgresFixture>> LazyFixture = new(async () =>
    {
        var fixture = new PostgresFixture();
        await fixture.InitializeAsync();
        return fixture;
    });

    private static readonly ConcurrentDictionary<string, Task<string>> Templates = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TemplateLocks = new();

    public static Task<PostgresFixture> GetFixtureAsync() => LazyFixture.Value;

    public static async Task<string> GetOrCreateTemplateAsync(string name, Func<string, Task> migrate)
    {
        if (Templates.TryGetValue(name, out var existing)) return await existing;

        var semaphore = TemplateLocks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
            if (Templates.TryGetValue(name, out existing)) return await existing;

            var fixture = await LazyFixture.Value;
            var template = await fixture.CreateTemplateAsync(name, migrate);
            Templates[name] = Task.FromResult(template);
            return template;
        }
        finally { semaphore.Release(); }
    }

    public static async Task<string> CloneAsync(string templateName)
    {
        var fixture = await LazyFixture.Value;
        return await fixture.CloneAsync(templateName);
    }

    public static async Task DropDatabaseAsync(string databaseName)
    {
        var fixture = await LazyFixture.Value;
        await fixture.DropAsync(databaseName);
    }
}
