using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace TokenBurn.Testing.Common.Integration;

public static class WebApiTestHost
{
    public static WebApplicationFactory<TEntryPoint> Create<TEntryPoint>(
        string connectionString,
        Action<IServiceCollection>? configureServices = null)
        where TEntryPoint : class
    {
        return new WebApplicationFactory<TEntryPoint>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Default", connectionString);
            b.ConfigureServices(services => configureServices?.Invoke(services));
        });
    }
}
