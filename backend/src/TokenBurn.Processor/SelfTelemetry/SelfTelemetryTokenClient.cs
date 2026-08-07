using System.Text.Json;

namespace TokenBurn.Processor.SelfTelemetry;

/// <summary>
///     Acquires a client_credentials access token from the Identity server for the
///     <c>telemetry.write</c> scope, mirroring <c>TokenBurn.Collector</c>'s
///     <c>GetTokenAsync</c> exactly. Registered as a typed client via
///     <c>AddHttpClient&lt;SelfTelemetryTokenClient&gt;</c>.
/// </summary>
public sealed class SelfTelemetryTokenClient(HttpClient httpClient, SelfTelemetryOptions options)
{
    private const string Scope = "telemetry.write";

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        using FormUrlEncodedContent form = new([
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", options.ClientId),
            new KeyValuePair<string, string>("client_secret", options.ClientSecret),
            new KeyValuePair<string, string>("scope", Scope)
        ]);
        using HttpResponseMessage response = await httpClient.PostAsync($"{options.IdentityUrl.TrimEnd('/')}/connect/token", form, cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        return document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Identity returned an empty access token.");
    }
}
