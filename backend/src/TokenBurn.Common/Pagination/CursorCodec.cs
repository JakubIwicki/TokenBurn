using System.Globalization;
using System.Text;

namespace TokenBurn.Common.Pagination;

/// <summary>
///     Opaque cursor for keyset pagination over <c>(started_at, id)</c> — the
///     same key the <c>agent_runs</c> index is built on, so both Elasticsearch
///     search (<c>search_after</c>) and the Postgres runs ledger can page with
///     one encoding. The cursor is a base64 <c>startedAt|id</c> pair; a NULL
///     <c>started_at</c> is encoded as the empty prefix.
/// </summary>
public static class CursorCodec
{
    public static string Encode(DateTimeOffset? startedAt, Guid id)
    {
        string raw = $"{startedAt?.ToString("O", CultureInfo.InvariantCulture)}|{id:N}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    public static bool TryDecode(string cursor, out DateTimeOffset? startedAt, out Guid id)
    {
        startedAt = null;
        id = Guid.Empty;
        try
        {
            string raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            int separator = raw.IndexOf('|');
            if (separator < 0)
                return false;

            string started = raw[..separator];
            if (started.Length > 0)
            {
                if (!DateTimeOffset.TryParseExact(started, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
                    return false;
                startedAt = parsed;
            }

            string idPart = raw[(separator + 1)..];
            return Guid.TryParseExact(idPart, "N", out id);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
