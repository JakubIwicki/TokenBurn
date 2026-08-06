using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TokenBurn.Processor.Infrastructure.Embeddings;

/// <summary>
///     Calls a Text Embeddings Inference endpoint — <c>POST {Uri}/embed?truncate=true</c>
///     with <c>{"inputs":[...]}</c> — through the <c>embeddings</c> named HttpClient.
///     A non-2xx response or an empty/mis-sized payload raises
///     <see cref="EmbeddingException" />; no retry is performed here because the
///     consumer crashes and Kafka redelivery drives the retry, matching the other
///     telemetry consumers.
/// </summary>
public sealed class TextEmbeddingsInferenceClient(
    IHttpClientFactory factory,
    ILogger<TextEmbeddingsInferenceClient> logger) : IEmbeddingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http = factory.CreateClient("embeddings");

    public async Task<IReadOnlyList<float>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "embed?truncate=true")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new EmbeddingRequest(texts), JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string detail = $"Text embeddings inference returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) for {texts.Count} input(s).";
            logger.LogWarning("Embedding request failed: {Detail}", detail);
            throw new EmbeddingException(detail);
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        float[][]? vectors;
        try
        {
            vectors = JsonSerializer.Deserialize<float[][]>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            string detail = $"Text embeddings inference returned an unparseable payload: {exception.Message}";
            logger.LogWarning(exception, "Embedding response was not valid JSON.");
            throw new EmbeddingException(detail);
        }
        if (vectors is null || vectors.Length == 0)
            throw new EmbeddingException("Text embeddings inference returned an empty response.");

        // The embedder sends a single run summary; TEI echoes one vector per input, so the
        // caller's vector is the first row. A count mismatch means the upstream dropped part
        // of the batch — surface it rather than return a silently wrong vector.
        if (vectors.Length != texts.Count)
            throw new EmbeddingException(
                $"Text embeddings inference returned {vectors.Length} vector(s) for {texts.Count} input(s).");
        return vectors[0];
    }

    private sealed record EmbeddingRequest(IReadOnlyList<string> inputs);
}
