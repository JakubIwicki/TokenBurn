using System.Text;

namespace TokenBurn.Processor.Documents;

/// <summary>
///     One deterministic chunk of a document: its ordinal, the text, the estimated token count
///     and the content hash. <see cref="ContentHash" /> is computed here so the chunk row and
///     the Elasticsearch document share the exact identity the chunker derived.
/// </summary>
public sealed record TextChunk(int Ordinal, string ChunkText, int TokenCount, string ContentHash);

/// <summary>
///     Splits a document's text into deterministic chunks: paragraphs (blank-line separated,
///     trimmed) are greedily packed under <c>ChunkMaxTokens</c>; a single oversized paragraph
///     is hard-split on word boundaries into chunks that stand alone — they are never packed
///     with neighboring paragraphs. Token count is estimated as ceil(chars / TokenCharsPerToken).
///     Same input always yields the same chunks (stable ordering and boundaries), so content
///     hashes — and therefore the dedupe — are stable across runs.
/// </summary>
public sealed class TextChunker
{
    private readonly DocumentsOptions _options;

    public TextChunker(DocumentsOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ChunkMaxTokens, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.TokenCharsPerToken, 1);
        _options = options;
    }

    public IReadOnlyList<TextChunk> Chunk(string text)
    {
        List<TextChunk> chunks = [];
        List<string> pending = [];
        int pendingChars = 0;

        foreach (string paragraph in SplitParagraphs(text))
        {
            if (EstimateTokens(paragraph) > _options.ChunkMaxTokens)
            {
                FlushPending();
                foreach (string hardChunk in HardSplitParagraph(paragraph))
                    chunks.Add(BuildChunk(hardChunk, chunks.Count));
            }
            else
            {
                // Packing joins paragraphs with "\n\n", so the candidate's token count accounts
                // for every separator: N packed paragraphs joined by "\n\n" carry 2N chars of
                // separators once the new one is added.
                int joinedChars = pendingChars + paragraph.Length + (2 * pending.Count);
                if (EstimateTokens(joinedChars) > _options.ChunkMaxTokens)
                    FlushPending();
                pending.Add(paragraph);
                pendingChars += paragraph.Length;
            }
        }
        FlushPending();
        return chunks;

        void FlushPending()
        {
            if (pending.Count == 0)
                return;
            chunks.Add(BuildChunk(string.Join("\n\n", pending), chunks.Count));
            pending.Clear();
            pendingChars = 0;
        }
    }

    /// <summary>
    ///     Splits a paragraph that exceeds the budget into word-boundary packs under
    ///     <c>ChunkMaxTokens</c>. A single token longer than the whole budget is split on
    ///     characters — the only deterministic way to keep every yielded chunk under budget.
    /// </summary>
    private IEnumerable<string> HardSplitParagraph(string paragraph)
    {
        List<string> current = [];
        int currentChars = 0;
        foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (EstimateTokens(word) > _options.ChunkMaxTokens)
            {
                if (current.Count > 0)
                {
                    yield return string.Join(' ', current);
                    current.Clear();
                    currentChars = 0;
                }
                int maxChars = _options.ChunkMaxTokens * _options.TokenCharsPerToken;
                for (int i = 0; i < word.Length; i += maxChars)
                    yield return word.Substring(i, Math.Min(maxChars, word.Length - i));
                continue;
            }

            // string.Join(' ', words) separates N words with N-1 single spaces; adding one more
            // word to a pack of `current.Count` words contributes exactly `current.Count` spaces.
            int joinedChars = currentChars + word.Length + current.Count;
            if (current.Count > 0 && EstimateTokens(joinedChars) > _options.ChunkMaxTokens)
            {
                yield return string.Join(' ', current);
                current.Clear();
                currentChars = 0;
            }
            current.Add(word);
            currentChars += word.Length;
        }
        if (current.Count > 0)
            yield return string.Join(' ', current);
    }

    private static IEnumerable<string> SplitParagraphs(string text)
    {
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (string raw in normalized.Split("\n\n", StringSplitOptions.None))
        {
            string trimmed = raw.Trim();
            if (trimmed.Length > 0)
                yield return trimmed;
        }
    }

    private TextChunk BuildChunk(string chunkText, int ordinal)
        => new(ordinal, chunkText, EstimateTokens(chunkText), ContentHasher.Compute(chunkText));

    private int EstimateTokens(string text) => EstimateTokens(text.Length);

    // Char-count overload for the packing guards, which reason about the would-be joined text
    // without materializing it.
    private int EstimateTokens(int charCount)
        => (int)Math.Ceiling(charCount / (double)_options.TokenCharsPerToken);
}
