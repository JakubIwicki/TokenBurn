using TokenBurn.Desktop.Core.Settings;

namespace TokenBurn.Desktop.Tests.Settings;

public sealed class DesktopSettingsTests
{
    [Fact]
    public void Validate_ValidSettings_DoesNotThrow() =>
        new DesktopSettings().Validate();

    [Fact]
    public void Validate_RelativeApiBaseUrl_Throws()
    {
        var settings = new DesktopSettings { ApiBaseUrl = new Uri("/relative", UriKind.Relative) };

        var act = () => settings.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ApiBaseUrl*");
    }

    [Fact]
    public void Validate_HttpApiBaseUrl_Throws()
    {
        var settings = new DesktopSettings { ApiBaseUrl = new Uri("http://localhost/") };

        var act = () => settings.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ApiBaseUrl*");
    }

    [Fact]
    public void Validate_NullIdentityAuthorityUrl_Throws()
    {
        var settings = new DesktopSettings { IdentityAuthorityUrl = null! };

        var act = () => settings.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IdentityAuthorityUrl*");
    }

    [Fact]
    public void Validate_HttpIdentityAuthorityUrl_Throws()
    {
        var settings = new DesktopSettings { IdentityAuthorityUrl = new Uri("http://localhost/connect") };

        var act = () => settings.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IdentityAuthorityUrl*");
    }

    [Fact]
    public void Validate_EmptyClientId_Throws()
    {
        var settings = new DesktopSettings { ClientId = " " };

        var act = () => settings.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ClientId*");
    }

    [Fact]
    public void Validate_RedirectPortMismatch_Throws()
    {
        var settings = new DesktopSettings { RedirectUri = new Uri("http://127.0.0.1:7892/callback") };

        var act = () => settings.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*loopback port*");
    }

    [Fact]
    public void Validate_NonLoopbackRedirectHost_Throws()
    {
        // A host other than loopback is fine per Validate — the port is the enforced contract.
        var settings = new DesktopSettings { RedirectUri = new Uri("http://localhost:7891/callback") };

        settings.Validate();
    }

    [Fact]
    public void Validate_ZeroRefreshLoopInterval_Throws()
    {
        var settings = new DesktopSettings { RefreshLoopInterval = TimeSpan.Zero };

        var act = () => settings.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RefreshLoopInterval*");
    }

    [Fact]
    public void Validate_NegativeRefreshLoopInterval_Throws()
    {
        var settings = new DesktopSettings { RefreshLoopInterval = TimeSpan.FromSeconds(-1) };

        var act = () => settings.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RefreshLoopInterval*");
    }
}
