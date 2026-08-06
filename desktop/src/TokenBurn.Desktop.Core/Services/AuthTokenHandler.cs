using System.Net;
using System.Net.Http.Headers;

namespace TokenBurn.Desktop.Core.Services;

/// <summary>
/// Attaches the bearer access token to every outbound request and implements the 401 → refresh →
/// retry-once → sign-out policy. Refresh is single-flight at the handler level: concurrent 401s
/// share one <see cref="IAuthSession.RefreshTokenAsync"/> call, and the shared task is cleared once
/// the wave completes so a later 401 starts a fresh refresh. Never retries more than once.
/// </summary>
public sealed class AuthTokenHandler : DelegatingHandler
{
    private readonly IAuthSession _session;
    private Task<TokenBundle?>? _inflightRefresh;

    public AuthTokenHandler(IAuthSession session) => _session = session;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var bundle = await _session.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        if (bundle is null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bundle.AccessToken);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        var refreshed = await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);
        if (refreshed is null)
            return response; // the refresh leader already signed out; surface the 401

        response.Dispose();
        using var retryRequest = await CloneForRetryAsync(request, cancellationToken).ConfigureAwait(false);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        var retry = await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
        if (retry.StatusCode == HttpStatusCode.Unauthorized)
            await _session.SignOutAsync(cancellationToken).ConfigureAwait(false);
        return retry;
    }

    private Task<TokenBundle?> RefreshOnceAsync(CancellationToken cancellationToken)
    {
        var existing = Volatile.Read(ref _inflightRefresh);
        if (existing is not null)
            return existing;

        var shared = new TaskCompletionSource<TokenBundle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var raced = Interlocked.CompareExchange(ref _inflightRefresh, shared.Task, null);
        if (raced is not null)
            return raced;

        _ = PumpRefreshAsync(shared, cancellationToken);
        return shared.Task;
    }

    private async Task PumpRefreshAsync(TaskCompletionSource<TokenBundle?> shared, CancellationToken cancellationToken)
    {
        try
        {
            var refreshed = await _session.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            if (refreshed is null)
                await _session.SignOutAsync(cancellationToken).ConfigureAwait(false);
            shared.TrySetResult(refreshed);
        }
        catch (OperationCanceledException)
        {
            shared.TrySetCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            shared.TrySetException(ex);
        }
        finally
        {
            _ = Interlocked.Exchange(ref _inflightRefresh, null);
        }
    }

    /// <summary>
    /// Rebuilds a request for the single retry. HttpRequestMessage can only be sent once, so the
    /// retry needs a fresh instance; headers carry over (Authorization is overwritten by the caller).
    /// The first send consumed the content stream, so the retry re-buffers the body via
    /// <c>ReadAsByteArrayAsync</c> — StringContent is buffered, so the full body is still available.
    /// A fresh <see cref="ByteArrayContent"/> plus an explicit copy of the content headers
    /// (Content-Type, Content-Length — which the previous clone lost) makes POST retries work. The
    /// GET path (<c>request.Content is null</c>) behaves byte-for-byte as before.
    /// </summary>
    private static async Task<HttpRequestMessage> CloneForRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
        foreach (var (key, value) in request.Headers)
            clone.Headers.TryAddWithoutValidation(key, value);
        if (request.Content is not null)
        {
            byte[] body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            clone.Content = new ByteArrayContent(body);
            foreach (var (key, value) in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(key, value);
        }
        return clone;
    }
}
