using System.Collections.ObjectModel;
using System.Globalization;
using TokenBurn.Desktop.Core.Features.Common;
using TokenBurn.Desktop.Core.Services;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Core.Features.Ask;

public sealed partial class AskViewModel : ObservableObject
{
    private readonly IDispatcher _dispatcher;
    private readonly IInsightsApiClient _api;

    public AskViewModel(IDispatcher dispatcher, IInsightsApiClient api)
    {
        _dispatcher = dispatcher;
        _api = api;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    private bool _hasAskScope;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    private string _question = "";

    [ObservableProperty]
    private string? _model;

    [ObservableProperty]
    private string? _persona;

    [ObservableProperty]
    private string? _source;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private DateTimeOffset? _from;

    [ObservableProperty]
    private DateTimeOffset? _to;

    [ObservableProperty]
    private string _answer = "";

    [ObservableProperty]
    private string _pricingCoverageText = "";

    public ObservableCollection<AskCitationRow> Citations { get; } = [];

    public ObservableCollection<AskRetrievalRow> Retrieval { get; } = [];

    public void ApplyScopes(IReadOnlyList<string> scopes) =>
        _dispatcher.Invoke(() => HasAskScope = scopes.Contains("ask.invoke"));

    private bool CanAsk() => HasAskScope && !IsLoading && !string.IsNullOrWhiteSpace(Question);

    [RelayCommand(CanExecute = nameof(CanAsk))]
    private async Task AskAsync(CancellationToken ct)
    {
        _dispatcher.Invoke(() =>
        {
            IsLoading = true;
            ErrorMessage = "";
        });
        try
        {
            var body = new AskRequest
            {
                Question = Question.Trim(),
                Model = Model,
                Persona = Persona,
                Source = Source,
                Status = Status,
                From = From,
                To = To,
            };
            var response = await _api.AskAsync(body, ct);
            _dispatcher.Invoke(() =>
            {
                Answer = response.Answer;
                PricingCoverageText = ChartSeriesFactory.FormatCoverage(response.PricingCoverage);
                Citations.Clear();
                foreach (var citation in response.Citations)
                    Citations.Add(AskCitationRow.From(citation));
                Retrieval.Clear();
                foreach (var hit in response.Retrieval)
                    Retrieval.Add(AskRetrievalRow.From(hit));
            });
        }
        catch (OperationCanceledException)
        {
            // cancelled — leave the previous state untouched
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() => ErrorMessage = ex.Message);
        }
        finally
        {
            _dispatcher.Invoke(() => IsLoading = false);
        }
    }
}

public sealed record AskCitationRow(string Kind, string Ref, string Excerpt)
{
    public static AskCitationRow From(AskCitation citation) => new(
        citation.Kind,
        citation.Kind == "trace" ? (citation.RunId?.ToString("D") ?? "—") : (citation.Title ?? citation.Uri ?? "—"),
        citation.Excerpt);
}

public sealed record AskRetrievalRow(
    string Kind,
    string Ref,
    string Persona,
    string Model,
    string Status,
    string StartedAt,
    string Tokens,
    string Cost)
{
    public static AskRetrievalRow From(AskRetrievalHit hit) => new(
        hit.Kind,
        hit.Kind == "trace" ? (hit.RunId?.ToString("D") ?? "—") : (hit.Title ?? hit.Uri ?? "—"),
        hit.Persona ?? "—",
        hit.ModelSlug ?? "—",
        hit.Status ?? "—",
        hit.StartedAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "—",
        ChartSeriesFactory.FormatTokens(hit.Tokens ?? 0),
        ChartSeriesFactory.FormatCost(hit.Cost));
}
