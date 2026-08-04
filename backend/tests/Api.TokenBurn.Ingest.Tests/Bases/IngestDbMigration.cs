using Api.TokenBurn.Ingest;
using Microsoft.EntityFrameworkCore;

namespace Api.TokenBurn.Ingest.Tests.Bases;

internal static class IngestDbMigration
{
    public static async Task RunAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<IngestDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new IngestDbContext(options);
        await db.Database.MigrateAsync();
    }
}
