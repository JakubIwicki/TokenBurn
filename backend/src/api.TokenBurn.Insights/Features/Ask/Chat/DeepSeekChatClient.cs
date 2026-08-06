using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Api.TokenBurn.Insights.Features.Ask.Chat;

/// <summary>
///     The opt-in DeepSeek chat client (<c>Ask:Provider=deepseek</c> only — never a runtime
///     fallback). Speaks the OpenAI-compatible <c>POST /chat/completions</c> protocol through
///     the named <c>deepseek</c> HttpClient (resilience pipeline from
///     Microsoft.Extensions.Http.Resilience). The request body is built by hand so the
///     OUTBOUND payload is fully visible to the egress leak test, and it carries ONLY the
///     allow-listed messages produced by <see cref="ChatMessageBuilder" />. The API key rides
///     the Authorization header and is never logged.
/// </summary>
public sealed class DeepSeekChatClient : IChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly AskOptions _options;
    private readonly ILogger<DeepSeekChatClient> _logger;

    public DeepSeekChatClient(IHttpClientFactory factory, AskOptions options, ILogger<DeepSeekChatClient> logger)
    {
        _options = options;
        _http = factory.CreateClient("deepseek");
        _logger = logger;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        DeepSeekChatRequest body = new(
            _options.DeepSeekModel ?? "deepseek-chat",
            messages.Select(message => new DeepSeekMessage(RoleFor(message.Role), message.Text ?? string.Empty)).ToList(),
            _options.DeepSeekMaxOutputTokens);

        using HttpRequestMessage request = new(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.DeepSeekApiKey);

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // No prompt/context and no key in the log line.
            string detail = $"DeepSeek chat returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
            _logger.LogWarning("DeepSeek chat request failed: {Detail}", detail);
            throw new InvalidOperationException(detail);
        }

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        DeepSeekChatResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<DeepSeekChatResponse>(responseBody, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"DeepSeek chat returned an unparseable completion: {exception.Message}");
        }
        if (parsed is null || parsed.Choices.Count == 0 || string.IsNullOrWhiteSpace(parsed.Choices[0].Message.Content))
            throw new InvalidOperationException("DeepSeek chat returned an empty completion.");

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, parsed.Choices[0].Message.Content));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Streaming chat is not supported by the DeepSeek client.");

    public void Dispose() { }

    private static string RoleFor(ChatRole role)
        => role == ChatRole.System ? "system" : role == ChatRole.Assistant ? "assistant" : "user";

    private sealed record DeepSeekChatRequest(string Model, IReadOnlyList<DeepSeekMessage> Messages, [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record DeepSeekMessage(string Role, string Content);

    private sealed record DeepSeekChatResponse(IReadOnlyList<DeepSeekChoice> Choices);

    private sealed record DeepSeekChoice(DeepSeekMessage Message);
}
