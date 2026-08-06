using System.Collections.ObjectModel;
using TokenBurn.Desktop.Core.Services;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Core.Features.RunDetail;

public sealed partial class RunDetailViewModel : ObservableObject
{
    private readonly IDispatcher _dispatcher;
    private readonly IInsightsApiClient _api;

    public RunDetailViewModel(IDispatcher dispatcher, IInsightsApiClient api)
    {
        _dispatcher = dispatcher;
        _api = api;
    }

    /// <summary>The backend message table does not exist yet — the feed is empty by design.</summary>
    public string MessagesEmptyText => "no messages — the transcript feed arrives in a later phase";

    public bool HasMessages => false;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private Guid? _selectedRunId;

    [ObservableProperty]
    private RunSummary? _run;

    public ObservableCollection<FindingSummary> Findings { get; } = [];

    [RelayCommand]
    private async Task OpenAsync(Guid id, CancellationToken ct)
    {
        SelectedRunId = id;
        await LoadAsync(id, ct);
    }

    private async Task LoadAsync(Guid id, CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var response = await _api.RunsDetailAsync(id, ct);
            _dispatcher.Invoke(() =>
            {
                Run = response.Run;
                Findings.Clear();
                foreach (var finding in response.Findings)
                    Findings.Add(finding);
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
