using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Persistence;

/// <summary>
///     Idempotent upsert of waste findings keyed on (run_id, kind, evidence_hash). The
///     <c>ON CONFLICT</c> never touches <c>detected_at</c>, <c>acknowledged_at</c> or
///     <c>version</c>: first detection wins, and a replay/redelivery updates only the mutable
///     severity/cost/evidence — row count stays one, so a replay cannot double-count a finding.
/// </summary>
public sealed class FindingsUpserter(TelemetryDbContext db)
{
    private const int BatchSize = 100;
    private const string SqlPrefix = """
        INSERT INTO telemetry.waste_findings
            (id, run_id, kind, severity, evidence, evidence_hash, wasted_cost_usd, detected_at, acknowledged_at, version)
        VALUES
        """;
    private const string SqlSuffix = """
        ON CONFLICT (run_id, kind, evidence_hash) DO UPDATE SET
            severity = EXCLUDED.severity, wasted_cost_usd = EXCLUDED.wasted_cost_usd,
            evidence = EXCLUDED.evidence
        """;

    public async Task UpsertAsync(IReadOnlyList<WasteFinding> findings, CancellationToken cancellationToken)
    {
        if (findings.Count == 0)
            return;

        NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        foreach (WasteFinding[] chunk in findings.Chunk(BatchSize))
            await UpsertChunkAsync(connection, chunk, cancellationToken);
    }

    private static async Task UpsertChunkAsync(
        NpgsqlConnection connection, IReadOnlyList<WasteFinding> chunk, CancellationToken cancellationToken)
    {
        StringBuilder sql = new(SqlPrefix);
        for (int i = 0; i < chunk.Count; i++)
        {
            if (i > 0)
                sql.Append(',');
            sql.Append("(@id_").Append(i).Append(", @run_id_").Append(i)
                .Append(", @kind_").Append(i).Append(", @severity_").Append(i)
                .Append(", @evidence_").Append(i).Append(", @evidence_hash_").Append(i)
                .Append(", @wasted_cost_usd_").Append(i).Append(", @detected_at_").Append(i)
                .Append(", @acknowledged_at_").Append(i).Append(", @version_").Append(i).Append(')');
        }
        sql.Append(SqlSuffix);

        await using NpgsqlCommand command = new(sql.ToString(), connection);
        for (int i = 0; i < chunk.Count; i++)
        {
            WasteFinding finding = chunk[i];
            command.Parameters.AddWithValue($"id_{i}", finding.Id);
            command.Parameters.AddWithValue($"run_id_{i}", finding.RunId);
            command.Parameters.AddWithValue($"kind_{i}", finding.Kind.ToString());
            command.Parameters.AddWithValue($"severity_{i}", finding.Severity.ToString());
            command.Parameters.AddWithValue($"evidence_{i}", NpgsqlDbType.Jsonb, finding.Evidence);
            command.Parameters.AddWithValue($"evidence_hash_{i}", finding.EvidenceHash);
            command.Parameters.AddWithValue($"wasted_cost_usd_{i}", (object?)finding.WastedCostUsd ?? DBNull.Value);
            command.Parameters.AddWithValue($"detected_at_{i}", finding.DetectedAt);
            command.Parameters.AddWithValue($"acknowledged_at_{i}", (object?)finding.AcknowledgedAt ?? DBNull.Value);
            command.Parameters.AddWithValue($"version_{i}", finding.Version);
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
