using Cinder.MINT.Models;
using Cinder.MINT.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace Cinder.MINT;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_viewModel.AutoStart)
        {
            try
            {
                _viewModel.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "MINT auto-start", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cannot start MINT", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _viewModel.Stop();

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.RefreshDevices();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Device refresh failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void GraphCanvas_NodeToggled(object? sender, AudioNodeModel node) =>
        _viewModel.ToggleNode(node);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            ToggleMaximize();
        else
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosing(e);
    }
}
