using Api.TokenBurn.Ingest.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Api.TokenBurn.Ingest.Infrastructure;

public sealed class EnvelopeInbox(IngestDbContext db, TimeProvider timeProvider) : IEnvelopeInbox
{
    public async Task WriteAsync(string source, string contentHash, string canonicalJson, string sessionKey, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        Guid envelopeId = Guid.NewGuid();
        int inserted = await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ingest.envelopes (id, source, payload, content_hash, received_at, status) VALUES ({0}, {1}, {2}, {3}, {4}, {5}) ON CONFLICT (content_hash) DO NOTHING",
            [
                new NpgsqlParameter("p_id", NpgsqlDbType.Uuid) { Value = envelopeId },
                new NpgsqlParameter("p_source", NpgsqlDbType.Text) { Value = source },
                new NpgsqlParameter("p_payload", NpgsqlDbType.Jsonb) { Value = canonicalJson },
                new NpgsqlParameter("p_hash", NpgsqlDbType.Text) { Value = contentHash },
                new NpgsqlParameter("p_received", NpgsqlDbType.TimestampTz) { Value = now },
                new NpgsqlParameter("p_status", NpgsqlDbType.Text) { Value = EnvelopeStatus.Received.ToString() }
            ], cancellationToken);

        if (inserted == 1)
        {
            db.OutboxMessages.Add(OutboxMessage.Create("telemetry.raw", sessionKey, canonicalJson, now));
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
