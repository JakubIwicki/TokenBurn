using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TokenBurn.Desktop.Core.Composition;
using TokenBurn.Desktop.Core.Features.Shell;
using TokenBurn.Desktop.Core.Services;
using TokenBurn.Desktop.Core.Settings;
using TokenBurn.Desktop.Services;

namespace TokenBurn.Desktop;

/// <summary>
/// Composition root. Registers the WPF-only services (token store, dispatcher), delegates the UI-free
/// registrations to <see cref="DesktopCompositionRoot.ConfigureDesktopServices"/>, then resolves and
/// shows the shell with the <see cref="ShellViewModel"/> as DataContext.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<ITokenStore, DpapiTokenStore>();
        services.AddSingleton<IDispatcher, WpfDispatcher>();
        services.ConfigureDesktopServices(LoadSettings());
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        var window = _services.GetRequiredService<MainWindow>();
        window.DataContext = _services.GetRequiredService<ShellViewModel>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static DesktopSettings LoadSettings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            return new DesktopSettings();

        // Only a missing file falls back to defaults. A present-but-malformed config (bad JSON, a
        // non-absolute URL) propagates out of OnStartup — the composition root dies loudly rather
        // than silently running against defaults.
        var dto = JsonSerializer.Deserialize<AppSettingsDto>(File.ReadAllText(path));
        if (dto is null)
            return new DesktopSettings();

        var settings = new DesktopSettings();
        if (!string.IsNullOrWhiteSpace(dto.ApiBaseUrl))
            settings.ApiBaseUrl = new Uri(dto.ApiBaseUrl, UriKind.Absolute);
        if (!string.IsNullOrWhiteSpace(dto.IdentityAuthorityUrl))
            settings.IdentityAuthorityUrl = new Uri(dto.IdentityAuthorityUrl, UriKind.Absolute);
        if (!string.IsNullOrWhiteSpace(dto.ClientId))
            settings.ClientId = dto.ClientId;
        if (!string.IsNullOrWhiteSpace(dto.RedirectUri))
            settings.RedirectUri = new Uri(dto.RedirectUri, UriKind.Absolute);
        if (dto.LoopbackPort is > 0)
            settings.LoopbackPort = dto.LoopbackPort.Value;
        if (dto.RefreshLoopIntervalSeconds is > 0)
            settings.RefreshLoopInterval = TimeSpan.FromSeconds(dto.RefreshLoopIntervalSeconds.Value);
        return settings;
    }

    private sealed class AppSettingsDto
    {
        public string? ApiBaseUrl { get; set; }
        public string? IdentityAuthorityUrl { get; set; }
        public string? ClientId { get; set; }
        public string? RedirectUri { get; set; }
        public int? LoopbackPort { get; set; }
        public int? RefreshLoopIntervalSeconds { get; set; }
    }
}
