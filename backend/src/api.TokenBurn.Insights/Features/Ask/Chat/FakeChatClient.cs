using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Api.TokenBurn.Insights.Features.Ask.Chat;

/// <summary>
///     The DEFAULT chat client in every environment (privacy-boundary rule 7): deterministic
///     and offline — it never touches the network. Its answer echoes the citation identifiers
///     present in the user message (trace run_ids and document titles — the document <c>uri</c>
///     never enters the prompt, so it cannot be echoed here), so integration tests assert
///     against the real seeded ids. Provider selection happens at registration time; this class
///     is never a runtime fallback.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private static readonly Regex RunIdPattern = new(@"run_id:\s*([0-9a-fA-F-]{36})", RegexOptions.Compiled);
    private static readonly Regex TitlePattern = new(@"title:\s*([^|\n]+)", RegexOptions.Compiled);

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string userText = string.Join('\n', messages
            .Where(message => message.Role == ChatRole.User)
            .Select(message => message.Text));
        IReadOnlyList<string> runIds = RunIdPattern.Matches(userText).Select(match => match.Groups[1].Value).Distinct().ToList();
        IReadOnlyList<string> titles = TitlePattern.Matches(userText).Select(match => match.Groups[1].Value.Trim()).Distinct().ToList();

        string answer = runIds.Count == 0 && titles.Count == 0
            ? "No citations were provided."
            : $"Fake answer citing runs {string.Join(", ", runIds)} and documents {string.Join(", ", titles)}.";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Streaming chat is not supported by the fake client.");

    public void Dispose() { }
}
