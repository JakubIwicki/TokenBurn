using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TokenBurn.Processor.Persistence;

public sealed class TelemetryDbContextFactory : IDesignTimeDbContextFactory<TelemetryDbContext>
{
    public TelemetryDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Processor")
            ?? "Host=localhost;Database=tokenburn;Username=postgres";
        DbContextOptionsBuilder<TelemetryDbContext> options = new();
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(TelemetryDbContext).Assembly.FullName)
            .MigrationsHistoryTable("__EFMigrationsHistory", "telemetry"));
        return new TelemetryDbContext(options.Options);
    }
}
