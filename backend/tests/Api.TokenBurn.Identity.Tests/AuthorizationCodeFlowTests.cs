using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Testcontainers.PostgreSql;

namespace Api.TokenBurn.Identity.Tests;

/// <summary>
///     Full-pipeline authorization-code + PKCE flow for the public desktop client:
///     metadata advertisement, the anonymous authorize→login challenge, and the
///     browser round-trip ending in a code exchange at /connect/token.
/// </summary>
public sealed class AuthorizationCodeFlowTests : IAsyncLifetime
{
    private const string DesktopClientId = "tokenburn-desktop";
    private const string DesktopRedirectUri = "http://127.0.0.1:7891/callback";
    private const string DesktopRequestedScope = "openid insights.read ask.invoke admin";
    private const string DevUsername = "test-user";
    private const string DevPassword = "test-password";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder().Build();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        await _database.StartAsync(timeout.Token);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // The host gates the OpenIddict development signing certificate and the HTTPS
            // transport-security relaxation behind IsDevelopment(); the test host must opt in so
            // token issuance over HTTP keeps working under the test scheme.
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Identity", _database.GetConnectionString());
            builder.UseSetting("Jwt:Authority", "http://localhost/connect");
            builder.UseSetting("Identity:CollectorClientSecret", "collector-secret");
            builder.UseSetting("Identity:DevUser:Username", DevUsername);
            builder.UseSetting("Identity:DevUser:Password", DevPassword);
        });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task AdvertisesAuthorizationEndpointInMetadata()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using HttpResponseMessage response = await _client.GetAsync("/.well-known/openid-configuration", timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal(
            "http://localhost/connect/authorize",
            document.RootElement.GetProperty("authorization_endpoint").GetString());
    }

    [Fact]
    public async Task RedirectsAnonymousAuthorizeToLogin()
    {
        (_, string challenge) = CreatePkce();
        string authorizeUrl = BuildAuthorizeUrl(challenge, "state", DesktopRequestedScope);
        using HttpClient browser = CreateBrowserClient();

        using HttpResponseMessage response = await browser.GetAsync(authorizeUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/connect/login?returnUrl=", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task IssuesDesktopToken_WhenAuthorizingWithPkce()
    {
        (string verifier, string challenge) = CreatePkce();
        string authorizeUrl = BuildAuthorizeUrl(challenge, "state", DesktopRequestedScope);
        using HttpClient browser = CreateBrowserClient();

        using HttpResponseMessage anonymousAuthorize = await browser.GetAsync(authorizeUrl);
        Assert.Equal(HttpStatusCode.Redirect, anonymousAuthorize.StatusCode);
        string loginUrl = anonymousAuthorize.Headers.Location!.ToString();
        Assert.Contains("/connect/login?returnUrl=", loginUrl);
        string returnUrl = ReadQueryParameter(loginUrl, "returnUrl");

        using HttpResponseMessage loginPage = await browser.GetAsync(loginUrl);
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        string loginHtml = await loginPage.Content.ReadAsStringAsync();
        Assert.Contains("<form", loginHtml);
        Assert.Contains("name=\"returnUrl\"", loginHtml);

        using FormUrlEncodedContent loginForm = new(
        [
            new KeyValuePair<string, string>("username", DevUsername),
            new KeyValuePair<string, string>("password", DevPassword),
            new KeyValuePair<string, string>("returnUrl", returnUrl)
        ]);
        using HttpResponseMessage loginPost = await browser.PostAsync("/connect/login", loginForm);
        Assert.Equal(HttpStatusCode.Redirect, loginPost.StatusCode);
        // The client's Location Uri re-encodes the already-encoded query ('?' -> '%3F',
        // '%' -> '%25'); the browser receives the raw single-encoded Location. Assert only
        // the target path — the authorize round-trip that follows proves the URL survived.
        Assert.StartsWith("/connect/authorize", loginPost.Headers.Location!.ToString());

        using HttpResponseMessage authorized = await browser.GetAsync(returnUrl);
        Assert.Equal(HttpStatusCode.Redirect, authorized.StatusCode);
        string callbackLocation = authorized.Headers.Location!.ToString();
        Assert.StartsWith(DesktopRedirectUri, callbackLocation);
        string code = ReadQueryParameter(callbackLocation, "code");

        using HttpResponseMessage tokenResponse = await ExchangeCodeAsync(browser, code, verifier);
        string responseBody = await tokenResponse.Content.ReadAsStringAsync();
        Assert.True(tokenResponse.StatusCode == HttpStatusCode.OK, responseBody);
        using JsonDocument document = JsonDocument.Parse(responseBody);
        string token = document.RootElement.GetProperty("access_token").GetString()!;
        JsonElement tokenPayload = ReadJwtPayload(token);
        string[] scopes = tokenPayload.GetProperty("scope").GetString()!.Split(' ');
        Assert.Contains("ask.invoke", scopes);
        Assert.Contains("insights.read", scopes);
        Assert.Equal("tokenburn-api", ReadAudience(tokenPayload));
    }

    [Fact]
    public async Task RejectsTokenExchange_WhenCodeVerifierMissing()
    {
        using HttpClient browser = CreateBrowserClient();
        string code = await ObtainCodeAsync(browser);

        using HttpResponseMessage tokenResponse = await ExchangeCodeWithoutVerifierAsync(browser, code);
        using JsonDocument document = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, tokenResponse.StatusCode);
        Assert.Equal("invalid_request", document.RootElement.GetProperty("error").GetString());
        Assert.False(document.RootElement.TryGetProperty("access_token", out _));
    }

    [Fact]
    public async Task IssuesNewAccessToken_WhenRefreshingDesktopTokenWithOfflineAccessScope()
    {
        (string verifier, string challenge) = CreatePkce();
        string authorizeUrl = BuildAuthorizeUrl(challenge, "refresh-state", DesktopRequestedScope + " offline_access");
        using HttpClient browser = CreateBrowserClient();

        string returnUrl = await GetLoginRedirectTargetAsync(browser, authorizeUrl);
        await SubmitLoginFormAsync(browser, returnUrl);
        string code = await GetAuthorizationCodeAsync(browser, returnUrl);

        using HttpResponseMessage tokenResponse = await ExchangeCodeAsync(browser, code, verifier);
        string tokenBody = await tokenResponse.Content.ReadAsStringAsync();
        Assert.True(tokenResponse.StatusCode == HttpStatusCode.OK, tokenBody);
        using JsonDocument tokenDocument = JsonDocument.Parse(tokenBody);
        string refreshToken = tokenDocument.RootElement.GetProperty("refresh_token").GetString()!;

        using HttpResponseMessage refreshResponse = await RefreshTokenAsync(browser, refreshToken);
        string refreshBody = await refreshResponse.Content.ReadAsStringAsync();
        Assert.True(refreshResponse.StatusCode == HttpStatusCode.OK, refreshBody);
        using JsonDocument refreshDocument = JsonDocument.Parse(refreshBody);
        string newAccessToken = refreshDocument.RootElement.GetProperty("access_token").GetString()!;

        JsonElement tokenPayload = ReadJwtPayload(newAccessToken);
        string[] scopes = tokenPayload.GetProperty("scope").GetString()!.Split(' ');
        Assert.Contains("ask.invoke", scopes);
        Assert.Contains("insights.read", scopes);
        Assert.Equal("tokenburn-api", ReadAudience(tokenPayload));
    }

    private HttpClient CreateBrowserClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<string> ObtainCodeAsync(HttpClient browser)
    {
        (_, string challenge) = CreatePkce();
        string authorizeUrl = BuildAuthorizeUrl(challenge, "negative-state", DesktopRequestedScope);
        string returnUrl = await GetLoginRedirectTargetAsync(browser, authorizeUrl);
        await SubmitLoginFormAsync(browser, returnUrl);
        return await GetAuthorizationCodeAsync(browser, returnUrl);
    }

    private static async Task<string> GetLoginRedirectTargetAsync(HttpClient browser, string authorizeUrl)
    {
        using HttpResponseMessage response = await browser.GetAsync(authorizeUrl);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        string location = response.Headers.Location!.ToString();
        Assert.Contains("/connect/login?returnUrl=", location);
        return ReadQueryParameter(location, "returnUrl");
    }

    private static async Task SubmitLoginFormAsync(HttpClient browser, string returnUrl)
    {
        using FormUrlEncodedContent loginForm = new(
        [
            new KeyValuePair<string, string>("username", DevUsername),
            new KeyValuePair<string, string>("password", DevPassword),
            new KeyValuePair<string, string>("returnUrl", returnUrl)
        ]);
        using HttpResponseMessage response = await browser.PostAsync("/connect/login", loginForm);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // The client's Location Uri re-encodes the already-encoded query string ('?' ->
        // '%3F', '%' -> '%25'); the browser receives the raw single-encoded Location. Assert
        // only the target path — the authorize round-trip that follows proves the URL stayed
        // intact.
        Assert.StartsWith("/connect/authorize", response.Headers.Location!.ToString());
    }

    private static async Task<string> GetAuthorizationCodeAsync(HttpClient browser, string returnUrl)
    {
        using HttpResponseMessage response = await browser.GetAsync(returnUrl);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        string location = response.Headers.Location!.ToString();
        Assert.StartsWith(DesktopRedirectUri, location);
        return ReadQueryParameter(location, "code");
    }

    private static string BuildAuthorizeUrl(string codeChallenge, string state, string scope)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = DesktopClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = DesktopRedirectUri,
            ["scope"] = scope,
            ["state"] = state,
            ["nonce"] = "nonce-" + state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };
        return "/connect/authorize?" + string.Join("&",
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    private async Task<HttpResponseMessage> ExchangeCodeAsync(HttpClient client, string code, string codeVerifier)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using FormUrlEncodedContent content = new(
        [
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("code_verifier", codeVerifier),
            new KeyValuePair<string, string>("redirect_uri", DesktopRedirectUri),
            new KeyValuePair<string, string>("client_id", DesktopClientId)
        ]);
        return await client.PostAsync("/connect/token", content, timeout.Token);
    }

    private async Task<HttpResponseMessage> ExchangeCodeWithoutVerifierAsync(HttpClient client, string code)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using FormUrlEncodedContent content = new(
        [
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", DesktopRedirectUri),
            new KeyValuePair<string, string>("client_id", DesktopClientId)
        ]);
        return await client.PostAsync("/connect/token", content, timeout.Token);
    }

    private async Task<HttpResponseMessage> RefreshTokenAsync(HttpClient client, string refreshToken)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using FormUrlEncodedContent content = new(
        [
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("client_id", DesktopClientId)
        ]);
        return await client.PostAsync("/connect/token", content, timeout.Token);
    }

    private static (string Verifier, string Challenge) CreatePkce()
    {
        byte[] random = RandomNumberGenerator.GetBytes(32);
        string verifier = Base64UrlEncode(random);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
        return (verifier, Base64UrlEncode(digest));
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string ReadQueryParameter(string url, string name)
    {
        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(new Uri(url, UriKind.RelativeOrAbsolute).Query);
        return query[name].ToString();
    }

    private static string ReadAudience(JsonElement tokenPayload)
    {
        JsonElement audience = tokenPayload.GetProperty("aud");
        return audience.ValueKind == JsonValueKind.String
            ? audience.GetString()!
            : string.Join(",", audience.EnumerateArray().Select(value => value.GetString()));
    }

    private static JsonElement ReadJwtPayload(string token)
    {
        string[] segments = token.Split('.');
        Assert.Equal(3, segments.Length);
        string payload = segments[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(payload));
        return document.RootElement.Clone();
    }
}
