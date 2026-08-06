using Cinder.MINT.Controls;
using Cinder.MINT.Models;
using Cinder.MINT.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
        InspectorCard.DataContext = _viewModel;
        SizeChanged += (_, _) => PositionInspector();
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

    private void AddNode_Click(object sender, RoutedEventArgs e)
    {
        AudioNodeModel node = _viewModel.AddSelectedNode();
        GraphCanvas.SelectNode(node);
        GraphScroll.ScrollToHorizontalOffset(Math.Max(0, node.X - 180));
        GraphScroll.ScrollToVerticalOffset(Math.Max(0, node.Y - 140));
        PositionInspector();
    }

    private void ResetGraph_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult result = MessageBox.Show(
            "Replace the current patch with the starter graph?",
            "Reset Cinder MINT patch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        InspectorPopup.IsOpen = false;
        _viewModel.ResetGraph();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.RefreshDevices();
            GraphCanvas.RefreshVisual();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Device refresh failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void GraphCanvas_NodeToggled(object? sender, AudioNodeModel node) =>
        _viewModel.ToggleNode(node);

    private void GraphCanvas_NodeMoved(object? sender, AudioNodeModel node)
    {
        if (ReferenceEquals(node, _viewModel.SelectedNode))
            PositionInspector();
    }

    private void GraphCanvas_NodeDeleteRequested(object? sender, AudioNodeModel node) => DeleteNode(node);

    private void GraphCanvas_NodeSelected(object? sender, NodeSelectionChangedEventArgs e)
    {
        _viewModel.SelectNode(e.Node);
        InspectorPopup.IsOpen = e.Node is not null;
        PositionInspector();
    }

    private void GraphCanvas_ConnectionChanged(object? sender, EventArgs e) =>
        _viewModel.NotifyGraphChanged("Patch cable updated", true);

    private void GraphCanvas_ConnectionRejected(object? sender, GraphMessageEventArgs e) =>
        _viewModel.NotifyGraphChanged(e.Message, false);

    private void EndpointSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        GraphCanvas.RefreshVisual();
        PositionInspector();
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNode is not null)
            DeleteNode(_viewModel.SelectedNode);
    }

    private void DeleteNode(AudioNodeModel node)
    {
        _viewModel.DeleteNode(node);
        InspectorPopup.IsOpen = false;
        GraphCanvas.ClearSelection();
    }

    private void CloseInspector_Click(object sender, RoutedEventArgs e)
    {
        InspectorPopup.IsOpen = false;
        GraphCanvas.ClearSelection();
    }

    private void GraphScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) => PositionInspector();

    private void PositionInspector()
    {
        AudioNodeModel? node = _viewModel.SelectedNode;
        if (node is null || !InspectorPopup.IsOpen) return;

        Point right = GraphCanvas.TranslatePoint(
            new Point(node.X + NodeGraphCanvas.NodeWidth + 14, node.Y),
            RootGrid);

        double x = right.X;
        double inspectorWidth = 350;
        if (x + inspectorWidth > ActualWidth - 18)
        {
            Point left = GraphCanvas.TranslatePoint(
                new Point(node.X - inspectorWidth - 14, node.Y),
                RootGrid);
            x = left.X;
        }

        double maxY = Math.Max(12, ActualHeight - 680);
        InspectorPopup.HorizontalOffset = Math.Max(12, x);
        InspectorPopup.VerticalOffset = Math.Clamp(right.Y, 12, maxY);
    }

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
        InspectorPopup.IsOpen = false;
        _viewModel.Dispose();
        base.OnClosing(e);
    }
}
