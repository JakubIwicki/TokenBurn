using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using IdentityModel.OidcClient.Browser;

namespace TokenBurn.Desktop.Core.Services;

/// <summary>
/// UI-free loopback browser for the OIDC authorization-code + PKCE redirect. IdentityModel.OidcClient
/// removed its bundled SystemBrowser in 6.0, so Core provides a replacement that opens the authorize
/// URL in the OS default browser and waits for the callback on 127.0.0.1:&lt;port&gt;. Only exercised
/// during an interactive sign-in on a Windows host; never touched by the headless test suite.
/// </summary>
public sealed class LoopbackBrowser : IBrowser
{
    private readonly int _port;

    public LoopbackBrowser(int port) => _port = port;

    public async Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();
        try
        {
            OpenInDefaultBrowser(options.StartUrl);

            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            await using var stream = client.GetStream();
            var requestTarget = await ReadRequestTargetAsync(stream, cancellationToken).ConfigureAwait(false);
            var callbackUrl = BuildResponseUrl(_port, requestTarget);
            await WriteClosingPageAsync(stream, cancellationToken).ConfigureAwait(false);

            return new BrowserResult { ResultType = BrowserResultType.Success, Response = callbackUrl };
        }
        catch (OperationCanceledException)
        {
            return new BrowserResult { ResultType = BrowserResultType.UserCancel };
        }
        catch
        {
            return new BrowserResult { ResultType = BrowserResultType.UnknownError, Error = "loopback callback failed" };
        }
    }

    public static string BuildResponseUrl(int port, string requestTarget) =>
        requestTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        requestTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? requestTarget
            : $"http://127.0.0.1:{port}{requestTarget}";

    private static void OpenInDefaultBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // no default browser registered — the operator can paste the authorize URL manually
        }
    }

    private static async Task<string> ReadRequestTargetAsync(Stream stream, CancellationToken cancellationToken)
    {
        var line = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : "/";
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var sb = new StringBuilder();
        while (await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) > 0)
        {
            if (buffer[0] == (byte)'\n')
                break;
            if (buffer[0] != (byte)'\r')
                sb.Append((char)buffer[0]);
        }
        return sb.ToString();
    }

    private static async Task WriteClosingPageAsync(Stream stream, CancellationToken cancellationToken)
    {
        const string body = "<html><body style=\"font-family:monospace\">sign-in complete — you may close this window.</body></html>";
        var bytes = Encoding.UTF8.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}");
        await stream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
    }
}
