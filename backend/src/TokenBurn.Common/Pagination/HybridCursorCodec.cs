using System.Globalization;
using System.Text;

namespace TokenBurn.Common.Pagination;

/// <summary>
///     An opaque cursor for paginating fused hybrid results — a base64
///     <c>score|id</c> pair encoding the fused position of the last returned
///     hit. <c>score</c> is the reciprocal-rank-fusion score and <c>id</c> the
///     document id string, both culture-invariant. Unlike
///     <see cref="CursorCodec" /> this is not a search_after key: hybrid pages
///     re-run both retrieval legs and re-fuse, so the cursor marks a position in
///     the fused ordering, not a point in the index.
/// </summary>
/// <param name="Score">The reciprocal-rank-fusion score of the last returned hit.</param>
/// <param name="Id">The document id of the last returned hit.</param>
public readonly record struct HybridCursorPosition(double Score, string Id);

public static class HybridCursorCodec
{
    public static string Encode(double score, string id)
    {
        string raw = $"{score.ToString(CultureInfo.InvariantCulture)}|{id}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    public static bool TryParse(string? cursor, out HybridCursorPosition position)
    {
        position = default;
        if (cursor is null)
            return false;
        try
        {
            string raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            int separator = raw.IndexOf('|');
            if (separator < 0)
                return false;

            if (!double.TryParse(raw.AsSpan(0, separator), NumberStyles.Float, CultureInfo.InvariantCulture, out double score))
                return false;
            if (!double.IsFinite(score))
                return false;
            string id = raw[(separator + 1)..];
            if (id.Length == 0)
                return false;

            position = new HybridCursorPosition(score, id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
