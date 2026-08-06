using System.Windows;
using TokenBurn.Desktop.Core.Features.BurnTicker;

namespace TokenBurn.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(BurnTickerViewModel burnTicker)
    {
        InitializeComponent();

        // The shell owns the whole window DataContext (ShellViewModel, set in App.xaml.cs); the
        // footer ticker is a permanently-live singleton, so it gets its own DataContext here.
        BurnTickerFooter.DataContext = burnTicker;
    }
}
