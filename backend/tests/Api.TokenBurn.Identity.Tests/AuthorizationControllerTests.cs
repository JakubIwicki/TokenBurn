using Api.TokenBurn.Identity.Controllers;
using Microsoft.AspNetCore.Http;

namespace Api.TokenBurn.Identity.Tests;

public sealed class AuthorizationControllerTests
{
    private const string RequestScheme = "http";
    private static readonly HostString RequestHost = new("localhost");

    [Theory]
    [InlineData("https://localhost/")]
    [InlineData("https://localhost.evil.com/")]
    [InlineData("http://localhost.evil.com/")]
    [InlineData("http://localhost@evil.com/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("//localhost/")]
    public void Rejects_WhenAbsoluteReturnUrlDoesNotMatchRequestOrigin(string returnUrl)
        => Assert.False(AuthorizationController.IsSafeAbsoluteReturnUrl(returnUrl, RequestScheme, RequestHost));

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://localhost:80/")]
    [InlineData("http://localhost/connect/authorize?client_id=tokenburn-desktop")]
    public void Accepts_WhenAbsoluteReturnUrlMatchesRequestOrigin(string returnUrl)
        => Assert.True(AuthorizationController.IsSafeAbsoluteReturnUrl(returnUrl, RequestScheme, RequestHost));
}
