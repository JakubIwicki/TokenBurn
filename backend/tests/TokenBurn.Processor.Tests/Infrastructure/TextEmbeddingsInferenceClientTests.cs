using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TokenBurn.Processor.Infrastructure.Embeddings;

namespace TokenBurn.Processor.Tests.Infrastructure;

public sealed class TextEmbeddingsInferenceClientTests
{
    [Fact]
    public async Task ReturnsVector_FromSingleText_WithEmbedPathAndTruncateFlag()
    {
        Fixture fixture = Fixture.Init().WithResponse(HttpStatusCode.OK, "[[0.12, 0.34, 0.56]]");

        IReadOnlyList<float> vector = await Act(fixture, ["what is the weather?"]);

        vector.Should().Equal(0.12f, 0.34f, 0.56f);
        fixture.Handler.Captured!.Method.Should().Be(HttpMethod.Post);
        fixture.Handler.Captured!.RequestUri!.AbsolutePath.Should().Be("/embed");
        fixture.Handler.Captured!.RequestUri!.Query.Should().Be("?truncate=true");
    }

    [Fact]
    public async Task SendsInputsArray_AsJsonBody()
    {
        Fixture fixture = Fixture.Init().WithResponse(HttpStatusCode.OK, "[[0.1],[0.2]]");

        await Act(fixture, ["hello", "world"]);

        using JsonDocument json = JsonDocument.Parse(fixture.Handler.CapturedBody!);
        json.RootElement.GetProperty("inputs").EnumerateArray().Select(element => element.GetString())
            .Should().Equal("hello", "world");
    }

    [Fact]
    public async Task ThrowsEmbeddingException_WhenNonSuccessResponse()
    {
        Fixture fixture = Fixture.Init().WithResponse(HttpStatusCode.BadGateway, "upstream unavailable");

        Func<Task> act = () => Act(fixture, ["hello"]);

        await act.Should().ThrowAsync<EmbeddingException>()
            .WithMessage("*HTTP 502*");
    }

    [Fact]
    public async Task ThrowsEmbeddingException_WhenResponseIsEmpty()
    {
        Fixture fixture = Fixture.Init().WithResponse(HttpStatusCode.OK, "[]");

        Func<Task> act = () => Act(fixture, ["hello"]);

        await act.Should().ThrowAsync<EmbeddingException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public async Task ThrowsEmbeddingException_WhenVectorCountMismatchesInputs()
    {
        Fixture fixture = Fixture.Init().WithResponse(HttpStatusCode.OK, "[[0.1],[0.2]]");

        Func<Task> act = () => Act(fixture, ["hello"]);

        await act.Should().ThrowAsync<EmbeddingException>()
            .WithMessage("*2 vector(s) for 1 input(s)*");
    }

    private static Task<IReadOnlyList<float>> Act(Fixture fixture, IReadOnlyList<string> texts)
        => fixture.Sut.EmbedAsync(texts, CancellationToken.None);

    private sealed class Fixture
    {
        private readonly CapturingHandler _handler;

        private Fixture(CapturingHandler handler)
        {
            _handler = handler;
            Sut = new TextEmbeddingsInferenceClient(
                new StubHttpClientFactory(new HttpClient(_handler) { BaseAddress = new Uri("http://tei.test") }),
                NullLogger<TextEmbeddingsInferenceClient>.Instance);
        }

        public TextEmbeddingsInferenceClient Sut { get; }
        public CapturingHandler Handler => _handler;

        public static Fixture Init() => new(new CapturingHandler());

        public Fixture WithResponse(HttpStatusCode status, string json)
        {
            _handler.Response = new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return this;
        }
    }

    private sealed class CapturingHandler : DelegatingHandler
    {
        public CapturingHandler() => Response = new HttpResponseMessage(HttpStatusCode.OK);

        public HttpRequestMessage? Captured { get; private set; }
        public string? CapturedBody { get; private set; }
        public HttpResponseMessage Response { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured = request;
            // The client disposes the request once EmbedAsync returns, so read the body here
            // while it is still live.
            if (request.Content is not null)
                CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return Response;
        }
    }

    private sealed class StubHttpClientFactory(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => http;
    }
}
