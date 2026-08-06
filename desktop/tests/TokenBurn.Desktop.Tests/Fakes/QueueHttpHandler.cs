namespace TokenBurn.Desktop.Tests.Fakes;

/// <summary>Inner handler for auth-handler tests: returns queued responses in order and records the requests it saw.</summary>
public sealed class QueueHttpHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;
    private readonly object _gate = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public QueueHttpHandler(params HttpResponseMessage[] responses) =>
        _responses = new Queue<HttpResponseMessage>(responses);

    public static QueueHttpHandler Sequence(params HttpStatusCode[] statusCodes) =>
        new(statusCodes.Select(s => new HttpResponseMessage(s)).ToArray());

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Requests.Add(request);
            var response = _responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK);
            return Task.FromResult(response);
        }
    }
}
