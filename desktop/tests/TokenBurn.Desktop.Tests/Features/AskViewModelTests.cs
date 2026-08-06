using TokenBurn.Desktop.Core.Features.Ask;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Tests.Features;

public sealed class AskViewModelTests
{
    private sealed class Fixture
    {
        public FakeDispatcher Dispatcher { get; } = new();
        public Mock<IInsightsApiClient> Api { get; } = new();
        public AskViewModel Sut { get; }

        public Fixture()
        {
            Sut = new AskViewModel(Dispatcher, Api.Object);
        }
    }

    [Fact]
    public async Task Ask_Success_RendersAnswerCitationsRetrievalAndCoverage()
    {
        var fx = new Fixture();
        var runId = Guid.NewGuid();
        var startedAt = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        fx.Api.Setup(a => a.AskAsync(It.IsAny<AskRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AskResponse
            {
                Answer = "hello",
                Citations =
                [
                    new AskCitation { Kind = "trace", RunId = runId, SessionId = "s1", Excerpt = "first" },
                    new AskCitation { Kind = "document", Title = "doc.md", Uri = "file:///doc.md", Excerpt = "second" },
                ],
                Retrieval =
                [
                    new AskRetrievalHit { Kind = "trace", RunId = runId, Persona = "p", ModelSlug = "m", Status = "completed", StartedAt = startedAt, Tokens = 1234, Cost = 1.5 },
                    new AskRetrievalHit { Kind = "document", Title = "doc.md", Ordinal = 0 },
                ],
                PricingCoverage = 0.5,
            });
        fx.Sut.ApplyScopes(["ask.invoke"]);
        fx.Sut.Question = "what is a run?";

        await fx.Sut.AskCommand.ExecuteAsync(null);

        fx.Sut.Answer.Should().Be("hello");
        fx.Sut.PricingCoverageText.Should().Be("0.50");
        fx.Sut.Citations.Should().HaveCount(2);
        fx.Sut.Citations[0].Ref.Should().Be(runId.ToString("D"));
        fx.Sut.Citations[1].Ref.Should().Be("doc.md");
        fx.Sut.Retrieval.Should().HaveCount(2);
        fx.Sut.Retrieval[0].Ref.Should().Be(runId.ToString("D"));
        fx.Sut.Retrieval[0].StartedAt.Should().Be("2030-01-02 03:04");
        fx.Sut.Retrieval[0].Tokens.Should().Be("1.2k");
        fx.Sut.Retrieval[0].Cost.Should().Be("$1.50");
        fx.Sut.Retrieval[1].Ref.Should().Be("doc.md");
    }

    [Fact]
    public async Task Ask_RequestCarriesFiltersAndTrimmedQuestion()
    {
        var fx = new Fixture();
        AskRequest? captured = null;
        fx.Api.Setup(a => a.AskAsync(It.IsAny<AskRequest>(), It.IsAny<CancellationToken>()))
            .Returns((AskRequest body, CancellationToken ct) =>
            {
                captured = body;
                return Task.FromResult(new AskResponse { Answer = "", Citations = [], Retrieval = [], PricingCoverage = 0 });
            });
        var from = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2030, 1, 31, 0, 0, 0, TimeSpan.Zero);
        fx.Sut.ApplyScopes(["ask.invoke"]);
        fx.Sut.Question = "  hello  ";
        fx.Sut.Model = "m1";
        fx.Sut.Persona = "p1";
        fx.Sut.Source = "s1";
        fx.Sut.Status = "st1";
        fx.Sut.From = from;
        fx.Sut.To = to;

        await fx.Sut.AskCommand.ExecuteAsync(null);

        captured.Should().NotBeNull();
        captured!.Question.Should().Be("hello");
        captured.Model.Should().Be("m1");
        captured.Persona.Should().Be("p1");
        captured.Source.Should().Be("s1");
        captured.Status.Should().Be("st1");
        captured.From.Should().Be(from);
        captured.To.Should().Be(to);
    }

    [Fact]
    public async Task Ask_WhenApiThrows_SetsErrorMessageAndClearsLoading()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.AskAsync(It.IsAny<AskRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        fx.Sut.ApplyScopes(["ask.invoke"]);
        fx.Sut.Question = "hi";
        fx.Sut.Answer = "previous";

        await fx.Sut.AskCommand.ExecuteAsync(null);

        fx.Sut.ErrorMessage.Should().Contain("boom");
        fx.Sut.IsLoading.Should().BeFalse();
        fx.Sut.Answer.Should().Be("previous");
    }

    [Fact]
    public async Task Ask_WhenCancelled_LeavesStateUntouched()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.AskAsync(It.IsAny<AskRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        fx.Sut.ApplyScopes(["ask.invoke"]);
        fx.Sut.Question = "hi";
        fx.Sut.Answer = "previous";

        await fx.Sut.AskCommand.ExecuteAsync(null);

        fx.Sut.Answer.Should().Be("previous");
        fx.Sut.ErrorMessage.Should().BeEmpty();
        fx.Sut.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void AskCommand_CanExecute_GatesOnScopeQuestionAndLoading()
    {
        var fx = new Fixture();

        fx.Sut.AskCommand.CanExecute(null).Should().BeFalse();

        fx.Sut.ApplyScopes(["ask.invoke"]);
        fx.Sut.Question = "hi";
        fx.Sut.AskCommand.CanExecute(null).Should().BeTrue();

        fx.Sut.Question = "";
        fx.Sut.AskCommand.CanExecute(null).Should().BeFalse();

        fx.Sut.Question = "hi";
        fx.Sut.IsLoading = true;
        fx.Sut.AskCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ApplyScopes_Empty_LocksCommand()
    {
        var fx = new Fixture();
        fx.Sut.ApplyScopes(["ask.invoke"]);
        fx.Sut.Question = "hi";
        fx.Sut.AskCommand.CanExecute(null).Should().BeTrue();

        fx.Sut.ApplyScopes([]);

        fx.Sut.AskCommand.CanExecute(null).Should().BeFalse();
        fx.Sut.HasAskScope.Should().BeFalse();
    }
}
