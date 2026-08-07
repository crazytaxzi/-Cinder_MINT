using Cinder.MINT.Controls;
using Cinder.MINT.Models;
using Cinder.MINT.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Cinder.MINT;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly PatchbayHotkeyStore _hotkeyStore = new();
    private PatchbayHotkeySettings _hotkeys;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        _hotkeys = _hotkeyStore.Load();

        DataContext = _viewModel;
        InspectorCard.DataContext = _viewModel;

        GraphCanvas.ContextMenuRequested += GraphCanvas_ContextMenuRequested;
        GraphCanvas.DeleteTargetRequested += GraphCanvas_DeleteTargetRequested;
        GraphCanvas.InspectorRequested += GraphCanvas_InspectorRequested;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
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
        GraphScroll.ScrollToHorizontalOffset(Math.Max(0, node.X * GraphCanvas.Zoom - 180));
        GraphScroll.ScrollToVerticalOffset(Math.Max(0, node.Y * GraphCanvas.Zoom - 140));
    }

    private void ResetGraph_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult result = MessageBox.Show(
            "Replace the current patch with the starter graph?",
            "Reset Cinder MINT patch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
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
        if (InspectorPopup.IsOpen && ReferenceEquals(node, _viewModel.SelectedNode))
            PositionInspector();
    }

    private void GraphCanvas_NodeDeleteRequested(object? sender, AudioNodeModel node) =>
        DeleteNodeWithConfirmation(node);

    private void GraphCanvas_NodeSelected(object? sender, NodeSelectionChangedEventArgs e)
    {
        _viewModel.SelectNode(e.Node);

        // Selection and inspection are intentionally separate. A click may mean
        // move, patch, or group-select; it must never surprise-open controls.
        InspectorPopup.IsOpen = false;
    }

    private void GraphCanvas_ConnectionChanged(object? sender, EventArgs e) =>
        _viewModel.NotifyGraphChanged("Patch cable updated", true);

    private void GraphCanvas_ConnectionRejected(object? sender, GraphMessageEventArgs e) =>
        _viewModel.NotifyGraphChanged(e.Message, false);

    private void GraphCanvas_ContextMenuRequested(object? sender, GraphContextRequestEventArgs e)
    {
        if (e.Node is not null &&
            !GraphCanvas.SelectedNodes.Any(node => node.Id == e.Node.Id))
        {
            GraphCanvas.SelectNode(e.Node);
        }

        ContextMenu menu = BuildPatchContextMenu(e);
        menu.PlacementTarget = GraphCanvas;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void GraphCanvas_DeleteTargetRequested(object? sender, GraphDeleteTargetEventArgs e)
    {
        if (e.Node is not null)
        {
            DeleteNodeWithConfirmation(e.Node);
            return;
        }

        if (e.Connection is not null)
            DeleteConnectionWithConfirmation(e.Connection);
    }

    private void GraphCanvas_InspectorRequested(object? sender, AudioNodeModel node) =>
        OpenInspector(node);

    private ContextMenu BuildPatchContextMenu(GraphContextRequestEventArgs context)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(8, 18, 22)),
            Foreground = new SolidColorBrush(Color.FromRgb(242, 255, 249)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(54, 91, 96)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4)
        };

        if (context.Node is not null)
        {
            MenuItem open = CreateMenuItem("OPEN NODE CONTROLS");
            open.Click += (_, _) => OpenInspector(context.Node);
            menu.Items.Add(open);

            if (context.Node.CanBypass)
            {
                MenuItem toggle = CreateMenuItem(context.Node.Enabled ? "BYPASS NODE" : "ENABLE NODE");
                toggle.Click += (_, _) =>
                {
                    context.Node.Enabled = !context.Node.Enabled;
                    _viewModel.ToggleNode(context.Node);
                    GraphCanvas.RefreshVisual();
                };
                menu.Items.Add(toggle);
            }

            MenuItem remove = CreateMenuItem("REMOVE NODE…", warning: true);
            remove.Click += (_, _) => DeleteNodeWithConfirmation(context.Node);
            menu.Items.Add(remove);
            menu.Items.Add(new Separator());
        }
        else if (context.Connection is not null)
        {
            MenuItem removeCable = CreateMenuItem("REMOVE CABLE…", warning: true);
            removeCable.Click += (_, _) => DeleteConnectionWithConfirmation(context.Connection);
            menu.Items.Add(removeCable);
            menu.Items.Add(new Separator());
        }

        if (context.Port is not null)
        {
            MenuItem disconnect = CreateMenuItem($"DISCONNECT {context.Port.Name}");
            disconnect.Click += (_, _) => DisconnectPortWithConfirmation(context.Port);
            menu.Items.Add(disconnect);
            menu.Items.Add(new Separator());
        }

        MenuItem add = CreateMenuItem(
            context.Node is null && context.Connection is null && context.Port is null
                ? "ADD NODE AT CURSOR"
                : "ADD NODE HERE");
        PopulateAddNodeMenu(add, context.CanvasPoint);
        menu.Items.Add(add);

        menu.Items.Add(new Separator());

        MenuItem shortcuts = CreateMenuItem("EDIT MINTYBAY SHORTCUTS…");
        shortcuts.Click += (_, _) => EditPatchbayShortcuts();
        menu.Items.Add(shortcuts);

        return menu;
    }

    private void PopulateAddNodeMenu(MenuItem root, Point point)
    {
        var ai = CreateMenuItem("AI SPECIALISTS");
        var routing = CreateMenuItem("ROUTING / I-O");
        var manual = CreateMenuItem("MANUAL DSP");

        foreach (NodePaletteItem palette in _viewModel.NodePalette)
        {
            MenuItem item = CreateMenuItem(palette.Label);
            item.Click += (_, _) => AddNodeAt(palette, point);

            if (palette.Type == AudioNodeType.AiProcessor)
                ai.Items.Add(item);
            else if (palette.Type is AudioNodeType.Input
                     or AudioNodeType.Output
                     or AudioNodeType.Mixer
                     or AudioNodeType.Ducker)
                routing.Items.Add(item);
            else
                manual.Items.Add(item);
        }

        root.Items.Add(ai);
        root.Items.Add(routing);
        root.Items.Add(manual);
    }

    private static MenuItem CreateMenuItem(string header, bool warning = false) =>
        new()
        {
            Header = header,
            Foreground = new SolidColorBrush(
                warning
                    ? Color.FromRgb(255, 95, 170)
                    : Color.FromRgb(242, 255, 249)),
            Padding = new Thickness(8, 5, 10, 5)
        };

    private void AddNodeAt(NodePaletteItem palette, Point point)
    {
        _viewModel.SelectedPaletteItem = palette;
        AudioNodeModel node = _viewModel.AddSelectedNode();

        node.X = Math.Max(10, point.X - NodeGraphCanvas.NodeWidth / 2);
        node.Y = Math.Max(10, point.Y - 35);
        GraphCanvas.SelectNode(node);
        GraphCanvas.RefreshVisual();
        _viewModel.NotifyGraphChanged($"Placed {node.Title} at cursor", true);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!GraphCanvas.IsKeyboardFocusWithin)
            return;

        if (PatchbayGesture.Matches(e, _hotkeys.AddNode))
        {
            GraphCanvas.RequestContextMenuAtCursor();
            e.Handled = true;
            return;
        }

        if (PatchbayGesture.Matches(e, _hotkeys.RemoveHovered))
        {
            GraphCanvas.RequestDeleteAtCursor();
            e.Handled = true;
            return;
        }

        if (PatchbayGesture.Matches(e, _hotkeys.OpenControls))
        {
            GraphCanvas.RequestInspectorForCurrentTarget();
            e.Handled = true;
            return;
        }

        if (PatchbayGesture.Matches(e, _hotkeys.ToggleBypass))
        {
            GraphCanvas.ToggleBypassAtCursor();
            e.Handled = true;
        }
    }

    private void EditPatchbayShortcuts()
    {
        var dialog = new PatchbayHotkeyDialog(this, _hotkeys);
        if (dialog.ShowDialog() != true) return;

        _hotkeys = dialog.Settings;
        try
        {
            _hotkeyStore.Save(_hotkeys);
            _viewModel.NotifyGraphChanged(
                $"MintyBay shortcuts saved — Add {_hotkeys.AddNode}, Remove {_hotkeys.RemoveHovered}",
                false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Could not save shortcuts",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void EndpointSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        GraphCanvas.RefreshVisual();
        PositionInspector();
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNode is not null)
            DeleteNodeWithConfirmation(_viewModel.SelectedNode);
    }

    private void DeleteNodeWithConfirmation(AudioNodeModel node)
    {
        var dialog = new ConfirmPatchRemovalDialog(
            this,
            $"Remove node \"{node.Title}\" and every cable connected to it?");
        if (dialog.ShowDialog() != true) return;

        _viewModel.DeleteNode(node);
        InspectorPopup.IsOpen = false;
        GraphCanvas.ClearSelection();
    }

    private void DeleteConnectionWithConfirmation(AudioConnectionModel connection)
    {
        AudioNodeModel? source = _viewModel.Graph.SourceNode(connection);
        AudioNodeModel? target = _viewModel.Graph.TargetNode(connection);
        string description = source is not null && target is not null
            ? $"Remove the cable from \"{source.Title}\" to \"{target.Title}\"?"
            : "Remove this patch cable?";

        var dialog = new ConfirmPatchRemovalDialog(this, description);
        if (dialog.ShowDialog() != true) return;

        _viewModel.Graph.Disconnect(connection);
        _viewModel.NotifyGraphChanged("Patch cable removed", true);
        GraphCanvas.RefreshVisual();
    }

    private void DisconnectPortWithConfirmation(AudioPortModel port)
    {
        int count = _viewModel.Graph.Connections.Count(
            connection => connection.SourcePortId == port.Id || connection.TargetPortId == port.Id);
        if (count == 0) return;

        var dialog = new ConfirmPatchRemovalDialog(
            this,
            $"Disconnect {count} cable{(count == 1 ? string.Empty : "s")} from port \"{port.Name}\"?");
        if (dialog.ShowDialog() != true) return;

        _viewModel.Graph.DisconnectPort(port.Id);
        _viewModel.NotifyGraphChanged("Port disconnected", true);
        GraphCanvas.RefreshVisual();
    }

    private void OpenInspector(AudioNodeModel node)
    {
        if (!GraphCanvas.SelectedNodes.Any(selected => selected.Id == node.Id))
            GraphCanvas.SelectNode(node);

        _viewModel.SelectNode(node);
        InspectorPopup.IsOpen = true;
        PositionInspector();
    }

    private void CloseInspector_Click(object sender, RoutedEventArgs e)
    {
        // Closing controls does not change patch selection.
        InspectorPopup.IsOpen = false;
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
        const double inspectorWidth = 390;
        if (x + inspectorWidth > ActualWidth - 18)
        {
            Point left = GraphCanvas.TranslatePoint(
                new Point(node.X - inspectorWidth - 14, node.Y),
                RootGrid);
            x = left.X;
        }

        double maxY = Math.Max(12, ActualHeight - 740);
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

