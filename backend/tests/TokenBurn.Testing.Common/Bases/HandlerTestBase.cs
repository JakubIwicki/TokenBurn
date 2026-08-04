using Microsoft.EntityFrameworkCore;
using Npgsql;
using TokenBurn.Testing.Common.Data;

namespace TokenBurn.Testing.Common.Bases;

public abstract class HandlerTestBase<TContext> : IAsyncLifetime where TContext : DbContext
{
    private readonly string _templateName;
    private readonly Func<string, Task> _migrate;
    private string _cloneDatabaseName = null!;
    private TContext _dbContext = null!;

    protected TestDb Db { get; private set; } = null!;

    protected TContext Context => _dbContext;

    protected HandlerTestBase(string templateName, Func<string, Task> migrate)
    {
        _templateName = templateName;
        _migrate = migrate;
    }

    public async Task InitializeAsync()
    {
        await SharedPostgres.GetOrCreateTemplateAsync(_templateName, _migrate);
        var cs = await SharedPostgres.CloneAsync(_templateName);
        _cloneDatabaseName = new NpgsqlConnectionStringBuilder(cs).Database!;
        var options = new DbContextOptionsBuilder<TContext>().UseNpgsql(cs).Options;
        _dbContext = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
        Db = new TestDb(_dbContext);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        _dbContext = null!;
        await SharedPostgres.DropDatabaseAsync(_cloneDatabaseName);
    }
}
