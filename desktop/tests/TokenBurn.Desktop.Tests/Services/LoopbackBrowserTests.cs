using TokenBurn.Desktop.Core.Services;

namespace TokenBurn.Desktop.Tests.Services;

public sealed class LoopbackBrowserTests
{
    [Fact]
    public void BuildResponseUrl_OriginFormTarget_PrefixesLoopbackHostAndPort()
    {
        LoopbackBrowser.BuildResponseUrl(7891, "/callback?code=abc&state=xyz")
            .Should().Be("http://127.0.0.1:7891/callback?code=abc&state=xyz");
    }

    [Fact]
    public void BuildResponseUrl_AbsoluteTarget_IsReturnedAsIs()
    {
        LoopbackBrowser.BuildResponseUrl(7891, "http://127.0.0.1:9999/callback?code=abc")
            .Should().Be("http://127.0.0.1:9999/callback?code=abc");
    }
}
