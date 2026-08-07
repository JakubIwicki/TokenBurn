using TokenBurn.Desktop.Core.Features.Ask;
using TokenBurn.Desktop.Core.Features.RunDetail;
using TokenBurn.Desktop.Core.Features.Shell;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Tests.Features;

public sealed class ShellViewModelTests
{
    private sealed class Fixture
    {
        public Mock<IAuthSession> Session { get; } = new();
        public FakeDispatcher Dispatcher { get; } = new();
        public FakeRefreshLoop Loop { get; } = new();
        public Mock<IInsightsApiClient> Api { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        public DashboardViewModel Dashboard { get; }
        public RunsViewModel Runs { get; }
        public RunDetailViewModel RunDetail { get; }
        public SearchViewModel Search { get; }
        public FindingsViewModel Findings { get; }
        public AskViewModel Ask { get; }
        public BurnTickerViewModel BurnTicker { get; }
        public ShellViewModel Sut { get; }

        public Fixture()
        {
            Session.SetupGet(s => s.GrantedScopes).Returns([]);
            Session.SetupGet(s => s.IsAuthenticated).Returns(false);

            // Valid empty responses so Activate's immediate refresh never NREs on a null DTO.
            Api.Setup(a => a.CostsSummaryAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CostSummaryResponse { Totals = new CostTotals(), Buckets = [], PricingCoverage = 0 });
            Api.Setup(a => a.RunsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RunsResponse { Runs = [], NextCursor = null });
            Api.Setup(a => a.SearchAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SearchResponse { Total = 0, Hits = [], Highlights = [], NextCursor = null });
            Api.Setup(a => a.FindingsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FindingsResponse { Findings = [], NextCursor = null });
            Api.Setup(a => a.RunsDetailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RunDetailResponse
                {
                    Run = new RunSummary { Id = Guid.NewGuid(), SessionId = "s", Source = "claude", Status = "completed", PricingStatus = "priced" },
                    Messages = [],
                    Findings = [],
                });
            Api.Setup(a => a.AskAsync(It.IsAny<AskRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AskResponse { Answer = "", Citations = [], Retrieval = [], PricingCoverage = 0 });

            Dashboard = new DashboardViewModel(Dispatcher, Api.Object, Loop);
            Runs = new RunsViewModel(Dispatcher, Api.Object, Loop);
            RunDetail = new RunDetailViewModel(Dispatcher, Api.Object);
            Search = new SearchViewModel(Dispatcher, Api.Object);
            Findings = new FindingsViewModel(Dispatcher, Api.Object, Loop);
            Ask = new AskViewModel(Dispatcher, Api.Object);
            BurnTicker = new BurnTickerViewModel(Dispatcher, Api.Object, Loop, Clock);

            Sut = new ShellViewModel(Session.Object, Dispatcher, Dashboard, Runs, RunDetail, Search, Findings, Ask, BurnTicker);
        }
    }

    [Fact]
    public void Ctor_ReflectsSessionState()
    {
        var fx = new Fixture();
        fx.Sut.IsAuthenticated.Should().BeFalse();
        fx.Sut.GrantedScopes.Should().BeEmpty();
    }

    [Fact]
    public async Task SignIn_Success_UpdatesAuthenticatedStateAndScopes()
    {
        var fx = new Fixture();
        fx.Session.Setup(s => s.SignInAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fx.Session.SetupGet(s => s.IsAuthenticated).Returns(true);
        fx.Session.SetupGet(s => s.GrantedScopes).Returns(["openid", "insights.read"]);

        await fx.Sut.SignInCommand.ExecuteAsync(null);

        fx.Sut.IsAuthenticated.Should().BeTrue();
        fx.Sut.GrantedScopes.Should().Contain("insights.read");
        fx.Sut.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task SignIn_Failure_SetsErrorMessage()
    {
        var fx = new Fixture();
        fx.Session.Setup(s => s.SignInAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await fx.Sut.SignInCommand.ExecuteAsync(null);

        fx.Sut.ErrorMessage.Should().Be("sign-in failed");
        fx.Sut.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task SignOut_UpdatesAuthenticatedState()
    {
        var fx = new Fixture();
        fx.Session.Setup(s => s.SignInAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fx.Session.SetupGet(s => s.IsAuthenticated).Returns(true);
        await fx.Sut.SignInCommand.ExecuteAsync(null);
        fx.Session.SetupGet(s => s.IsAuthenticated).Returns(false);
        fx.Session.Setup(s => s.SignOutAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await fx.Sut.SignOutCommand.ExecuteAsync(null);

        fx.Sut.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void UnauthenticatedEvent_SetsSignedOutState()
    {
        var fx = new Fixture();

        fx.Session.Raise(s => s.Unauthenticated += null, EventArgs.Empty);

        fx.Sut.IsAuthenticated.Should().BeFalse();
        fx.Sut.GrantedScopes.Should().BeEmpty();
    }

    [Fact]
    public async Task ShowAsk_SwitchesActiveFeatureAndView()
    {
        var fx = new Fixture();
        fx.Session.Setup(s => s.SignInAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fx.Session.SetupGet(s => s.IsAuthenticated).Returns(true);
        fx.Session.SetupGet(s => s.GrantedScopes).Returns(["insights.read", "ask.invoke"]);

        await fx.Sut.SignInCommand.ExecuteAsync(null);

        fx.Sut.ShowAskCommand.Execute(null);

        fx.Sut.ActiveFeature.Should().Be("Ask");
        fx.Sut.ActiveView.Should().BeSameAs(fx.Ask);
    }

    [Fact]
    public async Task Features_DeriveFromScopes()
    {
        var fx = new Fixture();
        fx.Session.Setup(s => s.SignInAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fx.Session.SetupGet(s => s.IsAuthenticated).Returns(true);
        fx.Session.SetupGet(s => s.GrantedScopes).Returns(["insights.read"]);

        await fx.Sut.SignInCommand.ExecuteAsync(null);

        fx.Sut.Features.Should().Equal(["Dashboard", "Runs", "Search", "Findings"]);
        fx.Sut.HasAskScope.Should().BeFalse();

        fx.Session.SetupGet(s => s.GrantedScopes).Returns(["insights.read", "ask.invoke"]);
        await fx.Sut.SignInCommand.ExecuteAsync(null);

        fx.Sut.Features.Should().EndWith("Ask");
        fx.Sut.HasAskScope.Should().BeTrue();
    }

    [Fact]
    public async Task SignOut_DropsAskScopeAndAskFeature()
    {
        var fx = new Fixture();
        fx.Session.Setup(s => s.SignInAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fx.Session.SetupGet(s => s.IsAuthenticated).Returns(true);
        fx.Session.SetupGet(s => s.GrantedScopes).Returns(["insights.read", "ask.invoke"]);

        await fx.Sut.SignInCommand.ExecuteAsync(null);

        fx.Session.SetupGet(s => s.IsAuthenticated).Returns(false);
        fx.Session.SetupGet(s => s.GrantedScopes).Returns([]);
        fx.Session.Setup(s => s.SignOutAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await fx.Sut.SignOutCommand.ExecuteAsync(null);

        fx.Sut.HasAskScope.Should().BeFalse();
        fx.Sut.Features.Should().NotContain("Ask");
    }

    [Fact]
    public async Task Unauthenticated_DropsAskScopeAndAskFeature()
    {
        var fx = new Fixture();
        fx.Session.Setup(s => s.SignInAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fx.Session.SetupGet(s => s.IsAuthenticated).Returns(true);
        fx.Session.SetupGet(s => s.GrantedScopes).Returns(["insights.read", "ask.invoke"]);

        await fx.Sut.SignInCommand.ExecuteAsync(null);

        fx.Session.Raise(s => s.Unauthenticated += null, EventArgs.Empty);

        fx.Sut.HasAskScope.Should().BeFalse();
        fx.Sut.Features.Should().NotContain("Ask");
    }

    [Fact]
    public void ShowRuns_SwitchesActiveFeatureAndDeactivatesDashboard()
    {
        var fx = new Fixture();

        fx.Sut.ShowRunsCommand.Execute(null);

        fx.Sut.ActiveFeature.Should().Be("Runs");
        fx.Sut.ActiveView.Should().BeSameAs(fx.Runs);
        fx.Loop.StopCount.Should().Be(1); // dashboard deactivated on the switch
    }

    [Fact]
    public void ShowSearch_SwitchesActiveFeature()
    {
        var fx = new Fixture();

        fx.Sut.ShowSearchCommand.Execute(null);

        fx.Sut.ActiveFeature.Should().Be("Search");
        fx.Sut.ActiveView.Should().BeSameAs(fx.Search);
    }

    [Fact]
    public void ShowDashboard_AfterRuns_DeactivatesRuns()
    {
        var fx = new Fixture();
        fx.Sut.ShowRunsCommand.Execute(null);

        fx.Sut.ShowDashboardCommand.Execute(null);

        fx.Sut.ActiveView.Should().BeSameAs(fx.Dashboard);
        fx.Sut.ActiveFeature.Should().Be("Dashboard");
        fx.Loop.StopCount.Should().Be(2);
    }

    [Fact]
    public async Task FeatureDeactivate_KeepsBurnTickerAlive()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.CostsSummaryAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CostSummaryResponse
            {
                Totals = new CostTotals { CostUsd = 4.62 },
                Buckets = [],
                PricingCoverage = 0.62,
            });

        // Deactivating the dashboard drops its loop reference; the ticker's own reference is held
        // (it activated at shell construction), so the shared loop must keep pumping it.
        fx.Sut.ShowRunsCommand.Execute(null);
        fx.Loop.StopCount.Should().Be(1);

        fx.Loop.Pump();
        await Task.Yield();

        fx.BurnTicker.Line.Should().Contain("$4.62");
    }

    [Fact]
    public void SelectingRun_OpensRunDetail()
    {
        var fx = new Fixture();
        var run = new RunSummary
        {
            Id = Guid.NewGuid(),
            SessionId = "s1",
            Source = "claude",
            Status = "completed",
            PricingStatus = "priced",
        };

        fx.Runs.SelectedRun = run;

        fx.Sut.ActiveFeature.Should().Be("RunDetail");
        fx.Sut.ActiveView.Should().BeSameAs(fx.RunDetail);
        fx.RunDetail.SelectedRunId.Should().Be(run.Id);
    }

    [Fact]
    public void Dispose_UnsubscribesFromSession()
    {
        var fx = new Fixture();
        fx.Sut.Dispose();

        fx.Session.Raise(s => s.Unauthenticated += null, EventArgs.Empty);

        // handler unsubscribed — no crash and state stays as-is
        fx.Sut.IsAuthenticated.Should().BeFalse();
        fx.Loop.StopCount.Should().Be(1); // ticker deactivated on dispose
    }
}
