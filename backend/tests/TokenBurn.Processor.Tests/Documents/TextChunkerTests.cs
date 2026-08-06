using TokenBurn.Processor.Documents;

namespace TokenBurn.Processor.Tests.Documents;

public sealed class TextChunkerTests
{
    private const int ChunkMaxTokens = 10;
    private const int TokenCharsPerToken = 4;
    private static readonly DocumentsOptions Options = new(ChunkMaxTokens, TokenCharsPerToken, MaxFileBytes: 1000, EmbeddingBatchSize: 4);

    [Fact]
    public void Chunks_SameInputTwice_ReturnsIdenticalChunks()
    {
        const string text = "First paragraph.\n\nSecond paragraph with more words.\n\nThird.";

        IReadOnlyList<TextChunk> first = CreateSut().Chunk(text);
        IReadOnlyList<TextChunk> second = CreateSut().Chunk(text);

        first.Should().Equal(second);
    }

    [Fact]
    public void Chunks_EveryChunk_StaysUnderTheTokenBudget()
    {
        string text = string.Join("\n\n", Enumerable.Range(0, 30)
            .Select(i => $"Paragraph {i} with some filler words to keep it reasonable in length."));

        IReadOnlyList<TextChunk> chunks = CreateSut().Chunk(text);

        chunks.Should().NotBeEmpty();
        chunks.Should().OnlyContain(chunk => chunk.TokenCount <= ChunkMaxTokens);
    }

    [Fact]
    public void Chunks_OversizedParagraph_IsHardSplit_AndTheTailStartsAFreshPack()
    {
        string oversized = string.Join(' ', Enumerable.Repeat("word", 60));
        const string tail = "Short tail paragraph.";
        string text = oversized + "\n\n" + tail;

        IReadOnlyList<TextChunk> chunks = CreateSut().Chunk(text);

        chunks.Count.Should().BeGreaterThan(1);
        chunks.Should().OnlyContain(chunk => chunk.TokenCount <= ChunkMaxTokens);
        chunks[^1].ChunkText.Should().Be(tail);
    }

    [Fact]
    public void Chunks_TokenCount_MatchesTheCharacterEstimate()
    {
        const string text = "alpha\n\nbeta beta\n\ngamma gamma gamma";

        IReadOnlyList<TextChunk> chunks = CreateSut().Chunk(text);

        chunks.Should().OnlyContain(chunk =>
            chunk.TokenCount == (int)Math.Ceiling(chunk.ChunkText.Length / (double)TokenCharsPerToken));
    }

    [Fact]
    public void Chunks_TinyParagraphs_PackIntoOneChunk()
    {
        const string text = "A.\n\nB.\n\nC.\n\nD.";

        TextChunk chunk = CreateSut().Chunk(text).Should().ContainSingle().Which;

        chunk.ChunkText.Should().Be("A.\n\nB.\n\nC.\n\nD.");
        chunk.TokenCount.Should().Be((int)Math.Ceiling(chunk.ChunkText.Length / (double)TokenCharsPerToken));
    }

    [Fact]
    public void Chunks_ContentHashes_AreStableAcrossRuns()
    {
        const string text = "same text\n\nsame text";

        IReadOnlyList<TextChunk> first = CreateSut().Chunk(text);
        IReadOnlyList<TextChunk> second = CreateSut().Chunk(text);

        first.Select(chunk => chunk.ContentHash).Should().Equal(second.Select(chunk => chunk.ContentHash));
    }

    private static TextChunker CreateSut() => new(Options);
}
