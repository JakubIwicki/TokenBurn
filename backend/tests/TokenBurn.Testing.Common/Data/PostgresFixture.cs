using Npgsql;
using Testcontainers.PostgreSql;

namespace TokenBurn.Testing.Common.Data;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;
    private string _adminConnectionString = null!;

    public PostgresFixture()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("postgres")
            .WithTmpfsMount("/var/lib/postgresql/data")
            .WithCreateParameterModifier(parameters =>
            {
                parameters.Cmd = ["postgres", "-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off"];
            })
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var csb = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Pooling = false };
        _adminConnectionString = csb.ConnectionString;
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public async Task<string> CreateTemplateAsync(string templateName, Func<string, Task> migrate)
    {
        await ExecuteAdminAsync($"CREATE DATABASE \"{templateName}\"");
        var templateCs = BuildConnectionString(templateName, false);
        await migrate(templateCs);
        NpgsqlConnection.ClearAllPools();
        await ExecuteAdminAsync($"ALTER DATABASE \"{templateName}\" WITH IS_TEMPLATE = true");
        return templateName;
    }

    public async Task<string> CloneAsync(string templateName)
    {
        var cloneName = $"t_{Guid.NewGuid():N}";
        await ExecuteAdminAsync($"CREATE DATABASE \"{cloneName}\" TEMPLATE \"{templateName}\"");
        return BuildConnectionString(cloneName, true);
    }

    public async Task DropAsync(string databaseName)
    {
        try { await ExecuteAdminAsync($"ALTER DATABASE \"{databaseName}\" WITH IS_TEMPLATE = false"); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName) { return; }
        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_adminConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private string BuildConnectionString(string database, bool pooling)
    {
        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database, Pooling = pooling };
        return builder.ConnectionString;
    }
}
