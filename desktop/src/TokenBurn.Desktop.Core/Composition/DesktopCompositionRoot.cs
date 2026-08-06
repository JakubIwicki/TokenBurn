using Microsoft.Extensions.DependencyInjection;
using TokenBurn.Desktop.Core.Features.BurnTicker;
using TokenBurn.Desktop.Core.Features.Dashboard;
using TokenBurn.Desktop.Core.Features.Findings;
using TokenBurn.Desktop.Core.Features.RunDetail;
using TokenBurn.Desktop.Core.Features.Runs;
using TokenBurn.Desktop.Core.Features.Search;
using TokenBurn.Desktop.Core.Features.Shell;
using TokenBurn.Desktop.Core.Services;
using TokenBurn.Desktop.Core.Services.Generated;
using TokenBurn.Desktop.Core.Settings;

namespace TokenBurn.Desktop.Core.Composition;

/// <summary>
/// Registers every UI-free service and ViewModel. The WPF app (and tests) add <see cref="ITokenStore"/>
/// and <see cref="IDispatcher"/> before calling this; it never registers them itself.
/// </summary>
public static class DesktopCompositionRoot
{
    public static IServiceCollection ConfigureDesktopServices(this IServiceCollection services, DesktopSettings settings)
    {
        settings.Validate();

        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IAuthSession>(sp => new OidcService(
            sp.GetRequiredService<ITokenStore>(),
            sp.GetRequiredService<TimeProvider>(),
            settings));

        services.AddSingleton<IRefreshLoop>(sp => new PeriodicTimerRefreshLoop(
            settings.RefreshLoopInterval,
            sp.GetRequiredService<TimeProvider>()));

        // The generated InsightsApiClient builds ABSOLUTE request URLs from its own BaseUrl (default
        // "http://localhost/" from the OpenAPI servers block), not from HttpClient.BaseAddress. The
        // named client carries auth + resilience; the singleton overrides BaseUrl to target the API.
        // AddHttpMessageHandler<T> resolves the handler from the container, so it must be registered.
        services.AddTransient<AuthTokenHandler>();
        services.AddHttpClient("insights", client => client.BaseAddress = settings.ApiBaseUrl)
            .AddHttpMessageHandler<AuthTokenHandler>()
            .AddStandardResilienceHandler();

        services.AddSingleton<IInsightsApiClient>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("insights");
            return new InsightsApiClient(httpClient) { BaseUrl = settings.ApiBaseUrl.AbsoluteUri };
        });

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<RunsViewModel>();
        services.AddSingleton<RunDetailViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<FindingsViewModel>();
        services.AddSingleton<BurnTickerViewModel>();

        return services;
    }
}
