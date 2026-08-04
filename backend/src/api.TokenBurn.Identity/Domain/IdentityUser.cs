using TokenBurn.Common.Primitives;

namespace Api.TokenBurn.Identity.Domain;

public sealed class IdentityUser : BaseEntity<long>
{
    private IdentityUser() { }

    public string Username { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;

    public static IdentityUser Create(string username, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        return new IdentityUser { Username = username, PasswordHash = passwordHash };
    }
}
