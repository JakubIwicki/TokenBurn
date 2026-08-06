using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TokenBurn.Desktop.Core.Services;

namespace TokenBurn.Desktop.Services;

/// <summary>
/// Windows DPAPI (<see cref="ProtectedData"/>, CurrentUser) persisted token store behind
/// <see cref="ITokenStore"/>. The JSON of a <see cref="TokenBundle"/> is encrypted at rest under
/// <c>%LocalAppData%/TokenBurn/tokens.bin</c>. A missing or corrupt file loads as null; a save always
/// overwrites (clears) any stale bytes first. Windows-only; no unit tests (tests fake the store).
/// </summary>
public sealed class DpapiTokenStore : ITokenStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TokenBurn",
        "tokens.bin");

    public async Task<TokenBundle?> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            var encrypted = await File.ReadAllBytesAsync(FilePath, cancellationToken);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plain);
            var dto = JsonSerializer.Deserialize<TokenBundleDto>(json);
            if (dto is null || string.IsNullOrEmpty(dto.AccessToken))
                return null;

            return new TokenBundle(
                dto.AccessToken,
                dto.RefreshToken,
                dto.ExpiresAt,
                dto.Scopes ?? []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or CryptographicException or JsonException or NotSupportedException)
        {
            // Missing/corrupt/unreadable file — treat as signed out.
            return null;
        }
    }

    public async Task SaveAsync(TokenBundle bundle, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        // Clear any stale/corrupt bytes so a failed write never leaves a half-baked bundle behind.
        if (File.Exists(FilePath))
            File.Delete(FilePath);

        var dto = new TokenBundleDto
        {
            AccessToken = bundle.AccessToken,
            RefreshToken = bundle.RefreshToken,
            ExpiresAt = bundle.ExpiresAt,
            Scopes = bundle.Scopes,
        };

        var json = JsonSerializer.Serialize(dto);
        var plain = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(FilePath, encrypted, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
        return Task.CompletedTask;
    }

    private sealed class TokenBundleDto
    {
        public string AccessToken { get; set; } = "";
        public string? RefreshToken { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public IReadOnlyList<string>? Scopes { get; set; }
    }
}
