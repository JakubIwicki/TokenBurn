using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.TokenBurn.Ingest;

public sealed class IngestDbContextFactory : IDesignTimeDbContextFactory<IngestDbContext>
{
    public IngestDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Ingest")
            ?? "Host=localhost;Database=tokenburn;Username=postgres";
        DbContextOptionsBuilder<IngestDbContext> options = new();
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(IngestDbContext).Assembly.FullName)
            .MigrationsHistoryTable("__EFMigrationsHistory", "ingest"));
        return new IngestDbContext(options.Options);
    }
}
