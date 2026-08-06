using System.Windows.Controls;
using TokenBurn.Desktop.Core.Features.Runs;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Features.Runs;

public partial class RunsView : UserControl
{
    public RunsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Selection wiring only: Core's <see cref="RunsViewModel.SelectedRun"/> is a <see cref="RunSummary"/>
    /// while the table rows are <see cref="RunRow"/>, so the shell's run-detail navigation is driven by
    /// setting a minimal RunSummary (RunDetail reloads the full run from the API by id).
    /// </summary>
    private void OnRunsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not RunsViewModel viewModel)
            return;
        if (sender is ListView { SelectedItem: RunRow row })
            viewModel.SelectedRun = new RunSummary { Id = row.Id };
    }
}
