using TokenBurn.Desktop.Core.Services;

namespace TokenBurn.Desktop.Tests.Services;

public sealed class AuthTokenHandlerTests
{
    private static readonly TokenBundle ValidBundle = new("old-access", "old-refresh", new DateTimeOffset(2030, 2, 1, 0, 0, 0, TimeSpan.Zero), ["openid"]);
    private static readonly TokenBundle RefreshedBundle = new("new-access", "new-refresh", new DateTimeOffset(2030, 3, 1, 0, 0, 0, TimeSpan.Zero), ["openid"]);

    private static HttpRequestMessage Get(string url = "https://localhost/api/runs") =>
        new(HttpMethod.Get, url);

    private static HttpClient Client(HttpMessageHandler inner, Mock<IAuthSession> session) =>
        new(new AuthTokenHandler(session.Object) { InnerHandler = inner });

    [Fact]
    public async Task SendAsync_WithToken_AttachesBearerAndDoesNotRefresh()
    {
        var session = new Mock<IAuthSession>();
        session.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ValidBundle);
        var inner = QueueHttpHandler.Sequence(HttpStatusCode.OK);
        using var client = Client(inner, session);

        var response = await client.SendAsync(Get(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Requests.Should().HaveCount(1);
        inner.Requests[0].Headers.Authorization.Should().NotBeNull();
        inner.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        inner.Requests[0].Headers.Authorization!.Parameter.Should().Be("old-access");
        session.Verify(s => s.RefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task SendAsync_When401_RefreshesOnceAndRetriesWithNewToken()
    {
        var session = new Mock<IAuthSession>();
        session.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ValidBundle);
        session.Setup(s => s.RefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync(RefreshedBundle);
        var inner = QueueHttpHandler.Sequence(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        using var client = Client(inner, session);

        var response = await client.SendAsync(Get(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Requests.Should().HaveCount(2);
        inner.Requests[0].Headers.Authorization!.Parameter.Should().Be("old-access");
        inner.Requests[1].Headers.Authorization!.Parameter.Should().Be("new-access");
        session.Verify(s => s.RefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once());
        session.Verify(s => s.SignOutAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task SendAsync_WhenRefreshFails_SignsOutAndRaisesUnauthenticated()
    {
        var session = new Mock<IAuthSession>();
        session.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ValidBundle);
        session.Setup(s => s.RefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync((TokenBundle?)null);
        session.Setup(s => s.SignOutAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Raises(s => s.Unauthenticated += null, EventArgs.Empty);
        var inner = QueueHttpHandler.Sequence(HttpStatusCode.Unauthorized);
        using var client = Client(inner, session);
        var unauthenticated = false;
        session.Object.Unauthenticated += (_, _) => unauthenticated = true;

        var response = await client.SendAsync(Get(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        session.Verify(s => s.SignOutAsync(It.IsAny<CancellationToken>()), Times.Once());
        unauthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_WhenRetryStill401_SignsOut()
    {
        var session = new Mock<IAuthSession>();
        session.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ValidBundle);
        session.Setup(s => s.RefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync(RefreshedBundle);
        session.Setup(s => s.SignOutAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var inner = QueueHttpHandler.Sequence(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized);
        using var client = Client(inner, session);

        var response = await client.SendAsync(Get(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        inner.Requests.Should().HaveCount(2);
        session.Verify(s => s.RefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once());
        session.Verify(s => s.SignOutAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task SendAsync_WithoutToken_PassesThroughWithoutAuth()
    {
        var session = new Mock<IAuthSession>();
        session.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync((TokenBundle?)null);
        var inner = QueueHttpHandler.Sequence(HttpStatusCode.Unauthorized);
        using var client = Client(inner, session);

        var response = await client.SendAsync(Get(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        inner.Requests.Single().Headers.Authorization.Should().BeNull();
        session.Verify(s => s.RefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never());
        session.Verify(s => s.SignOutAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Concurrent401s_SingleFlight_RefreshExactlyOnce()
    {
        const int requestCount = 5;
        var session = new Mock<IAuthSession>();
        session.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ValidBundle);
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshResult = new TaskCompletionSource<TokenBundle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Setup(s => s.RefreshTokenAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                refreshStarted.TrySetResult();
                return refreshResult.Task;
            });

        var inner = new Barrier401Handler(requestCount);
        using var client = Client(inner, session);

        var tasks = Enumerable.Range(0, requestCount)
            .Select(_ => Task.Run(() => client.SendAsync(Get(), CancellationToken.None)))
            .ToArray();

        // The first request to hit the 401 path starts the single refresh; the rest join it. Hold the
        // refresh open until all five requests are inside the shared task, then release it.
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        refreshResult.TrySetResult(RefreshedBundle);
        var responses = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        session.Verify(s => s.RefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Once());
        session.Verify(s => s.SignOutAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    /// <summary>
    /// Deterministic concurrent-401 inner handler: the first <c>participants</c> sends with the old
    /// bearer token synchronize on a barrier and return 401 together, so every request reaches the
    /// single-flight refresh before any retry; retries (new token) return 200 without the barrier.
    /// </summary>
    private sealed class Barrier401Handler : HttpMessageHandler
    {
        private readonly Barrier _barrier;

        public Barrier401Handler(int participants) => _barrier = new Barrier(participants);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization?.Parameter == "old-access")
            {
                _barrier.SignalAndWait(TimeSpan.FromSeconds(5));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
