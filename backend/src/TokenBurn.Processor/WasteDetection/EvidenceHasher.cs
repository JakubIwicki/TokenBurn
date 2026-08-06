using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TokenBurn.Processor.WasteDetection;

/// <summary>
///     Computes the dedupe identity for a finding's evidence. Determinism is the contract: the
///     same evidence object must produce the same hash on every process and every replay, because
///     Slice C keys findings on (run_id, kind, evidence_hash). The evidence is serialized with a
///     fixed camelCase, null-omitting configuration over a plain object (never a Dictionary), so
///     the key order is stable. Evidence must never contain message content or timestamps.
/// </summary>
public static class EvidenceHasher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(object evidence)
        => JsonSerializer.Serialize(evidence, SerializerOptions);

    /// <summary>
    ///     SHA256 hex of the pre-jsonb serialization of the evidence object. The stored jsonb
    ///     <c>evidence</c> text is re-sorted by Postgres and must NEVER be re-hashed: always
    ///     re-derive from a fresh evidence object so the hash matches the (run_id, kind,
    ///     evidence_hash) dedupe key computed at insert time.
    /// </summary>
    public static string Compute(object evidence)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(evidence)))).ToLowerInvariant();
}
