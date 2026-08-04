namespace Api.TokenBurn.Ingest.Infrastructure;

public interface IEnvelopeInbox
{
    Task WriteAsync(string source, string contentHash, string canonicalJson, string sessionKey, CancellationToken cancellationToken);
}
