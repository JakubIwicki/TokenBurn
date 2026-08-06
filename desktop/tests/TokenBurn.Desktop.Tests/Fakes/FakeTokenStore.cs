using TokenBurn.Desktop.Core.Services;

namespace TokenBurn.Desktop.Tests.Fakes;

/// <summary>In-memory token store. The WPF app ships the real DPAPI-backed implementation.</summary>
public sealed class FakeTokenStore : ITokenStore
{
    private TokenBundle? _stored;

    public TokenBundle? Stored => _stored;

    public Task<TokenBundle?> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_stored);

    public Task SaveAsync(TokenBundle bundle, CancellationToken cancellationToken)
    {
        _stored = bundle;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        _stored = null;
        return Task.CompletedTask;
    }
}
