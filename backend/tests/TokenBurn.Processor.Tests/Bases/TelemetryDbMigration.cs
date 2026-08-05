using Microsoft.EntityFrameworkCore;
using TokenBurn.Processor.Persistence;

namespace TokenBurn.Processor.Tests.Bases;

internal static class TelemetryDbMigration
{
    public static async Task RunAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "telemetry"))
            .Options;
        await using var db = new TelemetryDbContext(options);
        await db.Database.MigrateAsync();
    }
}
