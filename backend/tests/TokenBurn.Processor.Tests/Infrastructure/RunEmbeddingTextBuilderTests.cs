using System.Buffers;
using System.Text;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Infrastructure.Indexing;

namespace TokenBurn.Processor.Tests.Infrastructure;

public sealed class RunEmbeddingTextBuilderTests
{
    private const int MaxRunChars = 100;
    private const string SearchableText = "acme explore sess-1";
    private static readonly DateTimeOffset OccurredAt = new(2026, 7, 24, 21, 57, 51, TimeSpan.Zero);

    [Fact]
    public void BuildsRolePrefixedLines_FromMessagesInSequenceOrder()
    {
        AgentMessage second = CreateMessage(sequence: 2, role: "assistant", content: "hi");
        AgentMessage first = CreateMessage(sequence: 1, role: "user", content: "hello");

        string text = Act([second, first], MaxRunChars, SearchableText);

        text.Should().Be("user: hello\nassistant: hi");
    }

    [Fact]
    public void SkipsMessages_WithoutContent()
    {
        AgentMessage withoutContent = CreateMessage(sequence: 1, role: "tool", content: null);
        AgentMessage withContent = CreateMessage(sequence: 2, role: "assistant", content: "hi");

        string text = Act([withContent, withoutContent], MaxRunChars, SearchableText);

        text.Should().Be("assistant: hi");
    }

    [Fact]
    public void Truncates_ToMaxRunChars()
    {
        AgentMessage message = CreateMessage(sequence: 1, role: "user", content: new string('x', 150));

        string text = Act([message], maxRunChars: 10, SearchableText);

        // The role prefix is part of the line, so the first 10 characters are "user: " + four x's.
        text.Should().Be("user: xxxx");
    }

    [Fact]
    public void Truncates_WithoutSplittingASurrogatePair()
    {
        // "u: " + 6 a's + 😀 is 10 runes but 11 UTF-16 code units, so a code-unit cut at 10
        // would sever the emoji's surrogate pair and leave a lone high surrogate.
        AgentMessage message = CreateMessage(sequence: 1, role: "u", content: "aaaaaa😀");

        string text = Act([message], maxRunChars: 10, SearchableText);

        text.Should().Be("u: aaaaaa😀");
        for (int i = 0; i < text.Length;)
        {
            Rune.DecodeFromUtf16(text.AsSpan(i), out _, out int consumed).Should().Be(OperationStatus.Done);
            i += consumed;
        }
    }

    [Fact]
    public void FallsBackToSearchableText_WhenNoMessages()
    {
        string text = Act([], MaxRunChars, SearchableText);

        text.Should().Be(SearchableText);
    }

    [Fact]
    public void TruncatesFallback_ToMaxRunChars()
    {
        string text = Act([], maxRunChars: 4, SearchableText);

        text.Should().Be("acme");
    }

    private static string Act(IReadOnlyList<AgentMessage> messages, int maxRunChars, string searchableText)
        => CreateSut().Build(messages, maxRunChars, searchableText);

    private static RunEmbeddingTextBuilder CreateSut() => new();

    private static AgentMessage CreateMessage(int sequence, string role, string? content)
        => AgentMessage.Create(
            Guid.NewGuid(), sequence, role, content, toolName: null, modelSlug: "deepseek-v4-flash",
            inputTokens: 0, cacheReadTokens: 0, cacheWriteTokens: 0, outputTokens: 0, occurredAt: OccurredAt);
}
