using Microsoft.Extensions.DependencyInjection;
using TokenBurn.Desktop.Core.Composition;
using TokenBurn.Desktop.Core.Features.Ask;
using TokenBurn.Desktop.Core.Services;
using TokenBurn.Desktop.Core.Services.Generated;
using TokenBurn.Desktop.Core.Settings;

namespace TokenBurn.Desktop.Tests.Composition;

/// <summary>
/// The Linux-CI wiring check: every UI-free service and ViewModel registered by
/// <see cref="DesktopCompositionRoot.ConfigureDesktopServices"/> must resolve, and the generated
/// client must target the configured API base URL.
/// </summary>
public sealed class DesktopCompositionRootTests
{
    [Fact]
    public void ConfigureDesktopServices_ResolvesEveryServiceAndViewModel()
    {
        var settings = new DesktopSettings();
        var services = new ServiceCollection();
        services.AddSingleton<ITokenStore>(new FakeTokenStore());
        services.AddSingleton<IDispatcher>(new FakeDispatcher());
        services.ConfigureDesktopServices(settings);

        using var provider = services.BuildServiceProvider();

        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType.Assembly != typeof(DesktopCompositionRoot).Assembly)
                continue;
            if (descriptor.ServiceType.ContainsGenericParameters)
                continue;
            provider.GetRequiredService(descriptor.ServiceType);
        }

        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
        provider.GetRequiredService<IAuthSession>().Should().NotBeNull();
        provider.GetRequiredService<IRefreshLoop>().Should().BeOfType<PeriodicTimerRefreshLoop>();
        provider.GetRequiredService<ShellViewModel>().Should().NotBeNull();
        provider.GetRequiredService<DashboardViewModel>().Should().NotBeNull();
        provider.GetRequiredService<RunsViewModel>().Should().NotBeNull();
        provider.GetRequiredService<RunDetailViewModel>().Should().NotBeNull();
        provider.GetRequiredService<SearchViewModel>().Should().NotBeNull();
        provider.GetRequiredService<FindingsViewModel>().Should().NotBeNull();
        provider.GetRequiredService<AskViewModel>().Should().NotBeNull();
        provider.GetRequiredService<BurnTickerViewModel>().Should().NotBeNull();

        var client = provider.GetRequiredService<IInsightsApiClient>();
        client.Should().BeOfType<InsightsApiClient>();
        ((InsightsApiClient)client).BaseUrl.Should().Be(settings.ApiBaseUrl.AbsoluteUri);
    }

    [Fact]
    public void ConfigureDesktopServices_InvalidSettings_ThrowsBeforeAnyRegistration()
    {
        var settings = new DesktopSettings { ApiBaseUrl = new Uri("http://localhost/") };
        var services = new ServiceCollection();
        services.AddSingleton<ITokenStore>(new FakeTokenStore());
        services.AddSingleton<IDispatcher>(new FakeDispatcher());

        var act = () => services.ConfigureDesktopServices(settings);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ApiBaseUrl*");
    }
}
