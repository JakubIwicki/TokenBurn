namespace TokenBurn.Desktop.Core.Services;

public sealed record TokenBundle(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Scopes);
