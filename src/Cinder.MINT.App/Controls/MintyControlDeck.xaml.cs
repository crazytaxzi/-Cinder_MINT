using Cinder.MINT.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Cinder.MINT.Controls;

public partial class MintyControlDeck : UserControl
{
    private MintyControlDeckViewModel? _viewModel;

    public MintyControlDeck()
    {
        InitializeComponent();
        ShowPage(OverviewPage);
    }

    public event EventHandler? OpenMintyBayRequested;

    public void Attach(MainViewModel main)
    {
        _viewModel?.Dispose();
        _viewModel = new MintyControlDeckViewModel(main);
        DataContext = _viewModel;
    }

    private void StartStop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel?.StartOrStop();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "MintyFilter could not change state", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel?.RefreshDevices();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "MintyFilter device refresh failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OverviewNav_Click(object sender, RoutedEventArgs e) => ShowPage(OverviewPage);
    private void DevicesNav_Click(object sender, RoutedEventArgs e) => ShowPage(DevicesPage);
    private void ProcessingNav_Click(object sender, RoutedEventArgs e) => ShowPage(ProcessingPage);
    private void DiagnosticsNav_Click(object sender, RoutedEventArgs e) => ShowPage(DiagnosticsPage);

    private void MintyBay_Click(object sender, RoutedEventArgs e) =>
        OpenMintyBayRequested?.Invoke(this, EventArgs.Empty);

    private void ShowPage(FrameworkElement page)
    {
        foreach (FrameworkElement candidate in new[] { OverviewPage, DevicesPage, ProcessingPage, DiagnosticsPage })
            candidate.Visibility = ReferenceEquals(candidate, page) ? Visibility.Visible : Visibility.Collapsed;
    }
}
