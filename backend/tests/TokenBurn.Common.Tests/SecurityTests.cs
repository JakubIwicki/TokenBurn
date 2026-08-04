using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TokenBurn.Common.Security;

namespace TokenBurn.Common.Tests;

public sealed class SecurityTests
{
    [Fact]
    public void Throws_WhenJwtAuthorityIsEmpty()
    {
        var builder = WebApplication.CreateBuilder([]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Authority"] = ""
        });

        Assert.Throws<InvalidOperationException>(() => builder.AddTokenBurnJwtAuth());
    }

    [Fact]
    public async Task RegistersScopePolicies_WhenJwtAuthorityIsConfigured()
    {
        var builder = WebApplication.CreateBuilder([]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Authority"] = "https://localhost/connect"
        });

        builder.AddTokenBurnJwtAuth();
        using var provider = builder.Services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>();

        Assert.NotNull(await authorization.GetPolicyAsync(AuthorizationPolicies.TelemetryWrite));
        Assert.NotNull(await authorization.GetPolicyAsync(AuthorizationPolicies.InsightsRead));
        Assert.NotNull(await authorization.GetPolicyAsync(AuthorizationPolicies.AskInvoke));
        Assert.NotNull(await authorization.GetPolicyAsync(AuthorizationPolicies.Admin));
    }
}
