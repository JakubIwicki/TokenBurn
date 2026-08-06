using System.Text;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Infrastructure.Indexing;

/// <summary>
///     Builds the <c>embedding_text</c> for a run from its agent messages: one
///     <c>role: content</c> line per message, ordered by sequence, truncated on a
///     <see cref="Rune" /> boundary to <c>Embeddings.MaxRunChars</c> runes. Runs with
///     no message content fall back to the indexed <c>searchable_text</c>, so every
///     run embeds to something non-empty even when the transcript is blank.
/// </summary>
public sealed class RunEmbeddingTextBuilder
{
    public string Build(IReadOnlyList<AgentMessage> messages, int maxRunChars, string searchableText)
    {
        string text = JoinMessages(messages);
        if (string.IsNullOrWhiteSpace(text))
            text = searchableText ?? string.Empty;
        return TruncateToRunes(text, maxRunChars);
    }

    private static string TruncateToRunes(string text, int maxRunChars)
    {
        if (text.Length <= maxRunChars)
            return text;

        // Slice on a Rune boundary: stop before a rune whose inclusion would exceed the limit,
        // so the cut never splits a UTF-16 surrogate pair (an emoji or CJK-ext-B rune is one
        // Rune of two code units, advanced together).
        int charIndex = 0;
        int runeCount = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (runeCount == maxRunChars)
                break;
            charIndex += rune.Utf16SequenceLength;
            runeCount++;
        }
        return text[..charIndex];
    }

    private static string JoinMessages(IReadOnlyList<AgentMessage> messages)
        => string.Join('\n', messages
            .OrderBy(message => message.Sequence)
            .Where(message => !string.IsNullOrWhiteSpace(message.Content))
            .Select(message => $"{message.Role}: {message.Content}"));
}
