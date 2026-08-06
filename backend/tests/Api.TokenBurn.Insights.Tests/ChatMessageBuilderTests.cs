using Api.TokenBurn.Insights.Features.Ask.Chat;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace Api.TokenBurn.Insights.Tests;

public sealed class ChatMessageBuilderTests
{
    private static readonly RedactedTraceContext Trace = new(
        Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
        "sess-cite",
        "engineer",
        "claude-opus-5",
        "Completed",
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        150,
        0.01m,
        "redacted trace excerpt");

    private static readonly RedactedDocumentContext Document = new(
        "https://docs.acme.example/guide",
        "Acme Guide",
        2,
        "redacted document excerpt");

    [Fact]
    public void BuildsSystemAndUserMessages()
    {
        IList<ChatMessage> messages = new ChatMessageBuilder().Build("acme", [Trace], [Document]);

        messages.Should().HaveCount(2);
        messages[0].Role.Should().Be(ChatRole.System);
        messages[1].Role.Should().Be(ChatRole.User);
        messages[1].Text.Should().Contain("acme");
    }

    [Fact]
    public void UserMessage_CarriesOnlyAllowListedFields()
    {
        IList<ChatMessage> messages = new ChatMessageBuilder().Build("acme", [Trace], [Document]);

        string user = messages[1].Text!;
        user.Should().Contain("run_id: 01234567-89ab-cdef-0123-456789abcdef");
        user.Should().Contain("session_id: sess-cite");
        // The document uri is an absolute filesystem path in real corpora and must NEVER leave
        // to the model — it appears only on the authed API surface, not in the prompt.
        user.Should().NotContain("https://docs.acme.example/guide");
        user.Should().NotContain("uri:");
        user.Should().Contain("title: Acme Guide");
        user.Should().Contain("ordinal: 2");
        user.Should().Contain("excerpt: redacted document excerpt");
        user.Should().NotContain("external_id");
        user.Should().NotContain("workspace");
        user.Should().NotContain("agent_id");
    }
}
