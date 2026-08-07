using Cinder.MINT.Models;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Cinder.MINT.Controls;

public sealed class NodeSelectionChangedEventArgs(
    AudioNodeModel? node,
    IReadOnlyList<AudioNodeModel>? selectedNodes = null) : EventArgs
{
    public AudioNodeModel? Node { get; } = node;
    public IReadOnlyList<AudioNodeModel> SelectedNodes { get; } = selectedNodes ?? (node is null ? [] : [node]);
}

public sealed class GraphMessageEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

public sealed class GraphContextRequestEventArgs(
    Point canvasPoint,
    AudioNodeModel? node,
    AudioConnectionModel? connection,
    AudioPortModel? port) : EventArgs
{
    public Point CanvasPoint { get; } = canvasPoint;
    public AudioNodeModel? Node { get; } = node;
    public AudioConnectionModel? Connection { get; } = connection;
    public AudioPortModel? Port { get; } = port;
}

public sealed class GraphDeleteTargetEventArgs(
    AudioNodeModel? node,
    AudioConnectionModel? connection) : EventArgs
{
    public AudioNodeModel? Node { get; } = node;
    public AudioConnectionModel? Connection { get; } = connection;
}

public sealed class NodeGraphCanvas : FrameworkElement
{
    public const double NodeWidth = 204;
    private const double BaseNodeHeight = 100;
    private const double PortSpacing = 23;
    private const double PortStartY = 73;
    private const double SocketRadius = 6.5;
    private const double DragThreshold = 4.0;
    private const double MinZoom = 0.35;
    private const double MaxZoom = 2.60;
    private const double ZoomStep = 1.12;

    private static readonly Color Mint = Color.FromRgb(125, 255, 214);
    private static readonly Color Aqua = Color.FromRgb(66, 232, 224);
    private static readonly Color Purple = Color.FromRgb(167, 107, 255);
    private static readonly Color Pink = Color.FromRgb(255, 95, 170);
    private static readonly Color Gold = Color.FromRgb(255, 211, 107);
    private static readonly Color Text = Color.FromRgb(242, 255, 249);
    private static readonly Color Muted = Color.FromRgb(143, 167, 170);

    private readonly HashSet<Guid> _selectedNodeIds = [];
    private readonly Dictionary<Guid, Point> _dragOrigins = [];
    private readonly HashSet<Guid> _marqueeBaseSelection = [];
    private readonly ScaleTransform _zoomTransform = new(1, 1);

    private AudioNodeModel? _dragNode;
    private AudioNodeModel? _hoverNode;
    private AudioNodeModel? _selectedNode;
    private AudioConnectionModel? _hoverConnection;
    private AudioPortModel? _cableStartPort;
    private Point _pressPoint;
    private Point _cablePoint;
    private Point _lastPointerPoint = new(220, 180);
    private bool _dragStarted;

    private bool _marqueeActive;
    private bool _marqueeMoved;
    private Rect _marqueeRect;

    private bool _rightButtonActive;
    private bool _rightPanning;
    private Point _rightPressViewport;
    private double _rightStartHorizontalOffset;
    private double _rightStartVerticalOffset;

    private ScrollViewer? _scrollViewer;
    private double _zoom = 1.0;

    public static readonly DependencyProperty GraphProperty =
        DependencyProperty.Register(
            nameof(Graph),
            typeof(AudioGraphModel),
            typeof(NodeGraphCanvas),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnGraphChanged));

    public AudioGraphModel? Graph
    {
        get => (AudioGraphModel?)GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public AudioNodeModel? SelectedNode => _selectedNode;
    public IReadOnlyList<AudioNodeModel> SelectedNodes =>
        Graph?.Nodes.Where(node => _selectedNodeIds.Contains(node.Id)).ToList() ?? [];

    public Point LastPointerPoint => _lastPointerPoint;
    public double Zoom => _zoom;

    public event EventHandler<AudioNodeModel>? NodeToggled;
    public event EventHandler<AudioNodeModel>? NodeMoved;
    public event EventHandler<AudioNodeModel>? NodeDeleteRequested;
    public event EventHandler<NodeSelectionChangedEventArgs>? NodeSelected;
    public event EventHandler? ConnectionChanged;
    public event EventHandler<GraphMessageEventArgs>? ConnectionRejected;
    public event EventHandler<GraphContextRequestEventArgs>? ContextMenuRequested;
    public event EventHandler<GraphDeleteTargetEventArgs>? DeleteTargetRequested;
    public event EventHandler<AudioNodeModel>? InspectorRequested;

    public NodeGraphCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        LayoutTransform = _zoomTransform;
        Loaded += (_, _) => _scrollViewer = FindAncestor<ScrollViewer>(this);
    }

    public void ClearSelection()
    {
        _selectedNodeIds.Clear();
        _selectedNode = null;
        PublishSelection();
        InvalidateVisual();
    }

    public void SelectNode(AudioNodeModel? node)
    {
        _selectedNodeIds.Clear();
        if (node is not null)
            _selectedNodeIds.Add(node.Id);
        _selectedNode = node;
        PublishSelection();
        EnsureWorkspaceForSelection();
        InvalidateVisual();
    }

    public void RefreshVisual() => InvalidateVisual();

    public void RequestContextMenuAtCursor() => RaiseContextMenu(_lastPointerPoint);

    public void RequestDeleteAtCursor()
    {
        AudioNodeModel? node = HitTestNode(_lastPointerPoint);
        if (node is not null)
        {
            DeleteTargetRequested?.Invoke(this, new GraphDeleteTargetEventArgs(node, null));
            return;
        }

        AudioConnectionModel? connection = HitTestConnection(_lastPointerPoint);
        if (connection is not null)
            DeleteTargetRequested?.Invoke(this, new GraphDeleteTargetEventArgs(null, connection));
    }

    public void RequestInspectorForCurrentTarget()
    {
        AudioNodeModel? node = HitTestNode(_lastPointerPoint) ?? _selectedNode;
        if (node is not null)
            InspectorRequested?.Invoke(this, node);
    }

    public void ToggleBypassAtCursor()
    {
        AudioNodeModel? node = HitTestNode(_lastPointerPoint) ?? _selectedNode;
        if (node is null || !node.CanBypass) return;
        node.Enabled = !node.Enabled;
        NodeToggled?.Invoke(this, node);
        InvalidateVisual();
    }

    public static double GetNodeHeight(AudioNodeModel node) =>
        Math.Max(BaseNodeHeight, PortStartY + Math.Max(node.Inputs.Count, node.Outputs.Count) * PortSpacing + 12);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        DrawBackdrop(dc);
        DrawGrid(dc);

        if (Graph is null) return;

        foreach (AudioConnectionModel connection in Graph.Connections)
            DrawConnection(dc, connection);

        if (_cableStartPort is not null)
            DrawPendingCable(dc, _cableStartPort, _cablePoint);

        foreach (AudioNodeModel node in Graph.Nodes)
            DrawNode(dc, node);

        if (_marqueeActive && _marqueeMoved)
            DrawMarquee(dc);
    }

    private void DrawBackdrop(DrawingContext dc)
    {
        var background = new LinearGradientBrush(
            Color.FromRgb(7, 16, 20),
            Color.FromRgb(14, 9, 20),
            new Point(0, 0),
            new Point(1, 1));
        dc.DrawRectangle(background, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var mintBloom = new RadialGradientBrush
        {
            Center = new Point(0.18, 0.18),
            GradientOrigin = new Point(0.18, 0.18),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        mintBloom.GradientStops.Add(new GradientStop(Color.FromArgb(34, Mint.R, Mint.G, Mint.B), 0));
        mintBloom.GradientStops.Add(new GradientStop(Color.FromArgb(0, Mint.R, Mint.G, Mint.B), 1));
        dc.DrawRectangle(mintBloom, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var purpleBloom = new RadialGradientBrush
        {
            Center = new Point(0.87, 0.68),
            GradientOrigin = new Point(0.87, 0.68),
            RadiusX = 0.46,
            RadiusY = 0.46
        };
        purpleBloom.GradientStops.Add(new GradientStop(Color.FromArgb(26, Purple.R, Purple.G, Purple.B), 0));
        purpleBloom.GradientStops.Add(new GradientStop(Color.FromArgb(0, Purple.R, Purple.G, Purple.B), 1));
        dc.DrawRectangle(purpleBloom, null, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    private void DrawGrid(DrawingContext dc)
    {
        var minor = new Pen(new SolidColorBrush(Color.FromArgb(19, 104, 151, 153)), 1);
        var major = new Pen(new SolidColorBrush(Color.FromArgb(33, Mint.R, Mint.G, Mint.B)), 1);

        for (double x = 0; x < ActualWidth; x += 24)
            dc.DrawLine(((int)x % 96 == 0) ? major : minor, new Point(x, 0), new Point(x, ActualHeight));

        for (double y = 0; y < ActualHeight; y += 24)
            dc.DrawLine(((int)y % 96 == 0) ? major : minor, new Point(0, y), new Point(ActualWidth, y));
    }

    private void DrawConnection(DrawingContext dc, AudioConnectionModel connection)
    {
        if (!TryGetConnectionPoints(connection, out Point start, out Point end, out AudioPortModel? targetPort))
            return;

        Color endColor = targetPort!.Kind == AudioPortKind.Sidechain ? Pink : Purple;
        bool hovered = ReferenceEquals(connection, _hoverConnection);
        DrawBezier(dc, start, end, Mint, endColor, hovered ? 5.0 : 3.2);

        if (hovered)
        {
            Point middle = CubicPoint(start, Control1(start, end), Control2(start, end), end, 0.5);
            dc.DrawEllipse(
                new SolidColorBrush(Color.FromArgb(95, Mint.R, Mint.G, Mint.B)),
                new Pen(new SolidColorBrush(Mint), 1.2),
                middle,
                5.5,
                5.5);
        }
    }

    private void DrawPendingCable(DrawingContext dc, AudioPortModel port, Point cursor)
    {
        if (Graph is null) return;
        AudioNodeModel? node = Graph.GetNodeForPort(port.Id);
        if (node is null) return;

        Point socket = GetPortPoint(node, port);
        Point start = port.Direction == AudioPortDirection.Output ? socket : cursor;
        Point end = port.Direction == AudioPortDirection.Output ? cursor : socket;

        DrawBezier(
            dc,
            start,
            end,
            Mint,
            port.Kind == AudioPortKind.Sidechain ? Pink : Purple,
            2.5);
    }

    private static void DrawBezier(
        DrawingContext dc,
        Point start,
        Point end,
        Color startColor,
        Color endColor,
        double thickness)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = start };
        figure.Segments.Add(new BezierSegment(
            Control1(start, end),
            Control2(start, end),
            end,
            true));
        geometry.Figures.Add(figure);

        var glowColor = Color.FromArgb(55, startColor.R, startColor.G, startColor.B);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(glowColor), thickness + 5), geometry);
        dc.DrawGeometry(
            null,
            new Pen(new LinearGradientBrush(startColor, endColor, 0), thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            },
            geometry);
    }

    private void DrawNode(DrawingContext dc, AudioNodeModel node)
    {
        double nodeHeight = GetNodeHeight(node);
        Rect rect = new(node.X, node.Y, NodeWidth, nodeHeight);
        bool hovered = ReferenceEquals(node, _hoverNode);
        bool selected = _selectedNodeIds.Contains(node.Id);
        Color accent = Accent(node.Type);

        if (selected)
        {
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromArgb(34, accent.R, accent.G, accent.B)),
                new Pen(new SolidColorBrush(Color.FromArgb(72, accent.R, accent.G, accent.B)), 1),
                new Rect(rect.X - 8, rect.Y - 8, rect.Width + 16, rect.Height + 16),
                22,
                22);
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromArgb(18, accent.R, accent.G, accent.B)),
                null,
                new Rect(rect.X - 14, rect.Y - 14, rect.Width + 28, rect.Height + 28),
                28,
                28);
        }

        var background = node.Enabled
            ? new LinearGradientBrush(
                Color.FromRgb(20, 35, 41),
                Color.FromRgb(16, 16, 28),
                new Point(0, 0),
                new Point(1, 1))
            : new LinearGradientBrush(
                Color.FromRgb(35, 37, 41),
                Color.FromRgb(25, 26, 31),
                new Point(0, 0),
                new Point(1, 1));

        var borderColor = node.Enabled ? accent : Color.FromRgb(91, 100, 105);
        var border = new Pen(new SolidColorBrush(borderColor), selected ? 2.5 : hovered ? 1.9 : 1.15);

        dc.DrawRoundedRectangle(background, border, rect, 16, 16);

        var headerBrush = new LinearGradientBrush(
            Color.FromArgb(48, accent.R, accent.G, accent.B),
            Color.FromArgb(5, accent.R, accent.G, accent.B),
            new Point(0, 0),
            new Point(1, 0));
        dc.DrawRoundedRectangle(
            headerBrush,
            null,
            new Rect(rect.X + 1, rect.Y + 1, rect.Width - 2, 58),
            15,
            15);

        dc.DrawRoundedRectangle(
            new SolidColorBrush(accent),
            null,
            new Rect(rect.X + 12, rect.Y + 10, 4, 36),
            2,
            2);

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var title = new FormattedText(
            node.Title,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text, Segoe UI Semibold"),
            13.2,
            node.Enabled ? new SolidColorBrush(Text) : Brushes.Gray,
            dpi)
        {
            MaxTextWidth = NodeWidth - 72,
            Trimming = TextTrimming.CharacterEllipsis
        };

        var subtitle = new FormattedText(
            node.Enabled ? node.DisplaySubtitle : "BYPASSED",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text, Segoe UI"),
            10.2,
            new SolidColorBrush(node.Enabled ? Muted : Gold),
            dpi)
        {
            MaxTextWidth = NodeWidth - 35,
            Trimming = TextTrimming.CharacterEllipsis
        };

        dc.DrawText(title, new Point(rect.X + 23, rect.Y + 11));
        dc.DrawText(subtitle, new Point(rect.X + 23, rect.Y + 35));

        string badgeText = NodeBadge(node);
        var badge = new FormattedText(
            badgeText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text, Segoe UI Semibold"),
            8.2,
            new SolidColorBrush(accent),
            dpi);

        double badgeWidth = badge.Width + 14;
        Rect badgeRect = new(rect.Right - badgeWidth - 10, rect.Y + 11, badgeWidth, 18);
        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(38, accent.R, accent.G, accent.B)),
            new Pen(new SolidColorBrush(Color.FromArgb(110, accent.R, accent.G, accent.B)), 0.8),
            badgeRect,
            9,
            9);
        dc.DrawText(badge, new Point(badgeRect.X + 7, badgeRect.Y + 3));

        for (int i = 0; i < node.Inputs.Count; i++)
            DrawPort(dc, node, node.Inputs[i], i, dpi);

        for (int i = 0; i < node.Outputs.Count; i++)
            DrawPort(dc, node, node.Outputs[i], i, dpi);
    }

    private void DrawMarquee(DrawingContext dc)
    {
        var fill = new SolidColorBrush(Color.FromArgb(28, Mint.R, Mint.G, Mint.B));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(210, Mint.R, Mint.G, Mint.B)), 1.2)
        {
            DashStyle = DashStyles.Dash
        };
        dc.DrawRectangle(fill, pen, _marqueeRect);
    }

    private static string NodeBadge(AudioNodeModel node)
    {
        if (!node.Enabled) return "OFF";
        if (node.Type == AudioNodeType.Input) return "SOURCE";
        if (node.Type == AudioNodeType.Output) return "OUT";
        if (node.Type == AudioNodeType.Mixer) return "BUS";
        if (node.Type == AudioNodeType.AiProcessor) return "AI";
        if (node.Profile.AutoMode &&
            node.Type is AudioNodeType.NoiseGate or AudioNodeType.DeEsser or AudioNodeType.LevelRider)
            return "AUTO";
        return "DSP";
    }

    private void DrawPort(
        DrawingContext dc,
        AudioNodeModel node,
        AudioPortModel port,
        int index,
        double dpi)
    {
        Point point = GetPortPoint(node, port, index);
        Color color = port.Kind == AudioPortKind.Sidechain
            ? Pink
            : port.Direction == AudioPortDirection.Output
                ? Mint
                : Purple;

        dc.DrawEllipse(
            new SolidColorBrush(Color.FromArgb(45, color.R, color.G, color.B)),
            null,
            point,
            SocketRadius + 4,
            SocketRadius + 4);
        dc.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(7, 13, 17)),
            new Pen(new SolidColorBrush(color), 2.1),
            point,
            SocketRadius,
            SocketRadius);
        dc.DrawEllipse(new SolidColorBrush(color), null, point, 2.5, 2.5);

        var label = new FormattedText(
            port.Name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text, Segoe UI Semibold"),
            9.1,
            new SolidColorBrush(color),
            dpi);

        double y = point.Y - label.Height / 2;
        if (port.Direction == AudioPortDirection.Input)
            dc.DrawText(label, new Point(node.X + 14, y));
        else
            dc.DrawText(label, new Point(node.X + NodeWidth - 14 - label.Width, y));
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _lastPointerPoint = e.GetPosition(this);
        PortHit? portHit = HitTestPort(_lastPointerPoint);

        if (e.ChangedButton == MouseButton.Right)
        {
            BeginRightPanCandidate(e);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        if (portHit is not null)
        {
            _cableStartPort = portHit.Port;
            _cablePoint = _lastPointerPoint;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        AudioNodeModel? hit = HitTestNode(_lastPointerPoint);
        if (hit is not null)
        {
            if (e.ClickCount == 2 && hit.CanBypass)
            {
                EnsureNodeSelected(hit, Keyboard.Modifiers);
                hit.Enabled = !hit.Enabled;
                NodeToggled?.Invoke(this, hit);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            bool canDrag = EnsureNodeSelected(hit, Keyboard.Modifiers);
            if (canDrag)
                BeginNodeDrag(hit, _lastPointerPoint);

            e.Handled = true;
            return;
        }

        BeginMarquee(_lastPointerPoint, Keyboard.Modifiers);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _lastPointerPoint = e.GetPosition(this);

        if (_rightButtonActive && e.RightButton == MouseButtonState.Pressed)
        {
            ContinueRightPan(e);
            return;
        }

        if (_cableStartPort is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            _cablePoint = _lastPointerPoint;
            InvalidateVisual();
            return;
        }

        if (_dragNode is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            ContinueNodeDrag(_lastPointerPoint);
            return;
        }

        if (_marqueeActive && e.LeftButton == MouseButtonState.Pressed)
        {
            ContinueMarquee(_lastPointerPoint);
            return;
        }

        UpdateHover(_lastPointerPoint);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        _lastPointerPoint = e.GetPosition(this);

        if (e.ChangedButton == MouseButton.Right && _rightButtonActive)
        {
            bool showMenu = !_rightPanning;
            EndRightPan();
            if (showMenu)
                RaiseContextMenu(_lastPointerPoint);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        if (_cableStartPort is not null)
        {
            CompleteCable(_lastPointerPoint);
            e.Handled = true;
            return;
        }

        if (_dragNode is not null)
        {
            _dragNode = null;
            _dragOrigins.Clear();
            _dragStarted = false;
            ReleaseMouseCaptureIfOwned();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_marqueeActive)
        {
            EndMarquee();
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        _lastPointerPoint = e.GetPosition(this);
        ScrollViewer? scroll = GetScrollViewer();
        if (scroll is null) return;

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            ZoomAroundCursor(e, scroll);
            e.Handled = true;
            return;
        }

        double amount = -e.Delta * 0.72;
        if (modifiers.HasFlag(ModifierKeys.Alt))
            scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + amount);
        else
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + amount);

        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragNode is null &&
            _cableStartPort is null &&
            !_marqueeActive &&
            !_rightButtonActive)
        {
            _hoverNode = null;
            _hoverConnection = null;
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            CancelPointerOperation();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && _selectedNode is not null)
        {
            NodeDeleteRequested?.Invoke(this, _selectedNode);
            e.Handled = true;
        }
    }

    private void BeginNodeDrag(AudioNodeModel hit, Point point)
    {
        _dragNode = hit;
        _pressPoint = point;
        _dragStarted = false;
        _dragOrigins.Clear();

        foreach (AudioNodeModel selected in SelectedNodes)
            _dragOrigins[selected.Id] = new Point(selected.X, selected.Y);

        CaptureMouse();
    }

    private void ContinueNodeDrag(Point point)
    {
        if (_dragNode is null || _dragOrigins.Count == 0) return;

        Vector rawDelta = point - _pressPoint;
        if (!_dragStarted && rawDelta.Length > DragThreshold)
            _dragStarted = true;
        if (!_dragStarted) return;

        double minX = _dragOrigins.Values.Min(p => p.X);
        double minY = _dragOrigins.Values.Min(p => p.Y);
        double dx = Math.Max(rawDelta.X, 10 - minX);
        double dy = Math.Max(rawDelta.Y, 10 - minY);

        foreach (AudioNodeModel node in SelectedNodes)
        {
            if (!_dragOrigins.TryGetValue(node.Id, out Point origin)) continue;
            node.X = origin.X + dx;
            node.Y = origin.Y + dy;
            NodeMoved?.Invoke(this, node);
        }

        EnsureWorkspaceForSelection();
        InvalidateVisual();
    }

    private bool EnsureNodeSelected(AudioNodeModel node, ModifierKeys modifiers)
    {
        bool additive = modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Shift);
        bool alreadySelected = _selectedNodeIds.Contains(node.Id);

        if (additive && alreadySelected)
        {
            _selectedNodeIds.Remove(node.Id);
            _selectedNode = _selectedNodeIds.Count == 0
                ? null
                : Graph?.Nodes.LastOrDefault(x => _selectedNodeIds.Contains(x.Id));
            PublishSelection();
            InvalidateVisual();
            return false;
        }

        if (!additive && !alreadySelected)
            _selectedNodeIds.Clear();

        _selectedNodeIds.Add(node.Id);
        _selectedNode = node;
        PublishSelection();
        InvalidateVisual();
        return true;
    }

    private void BeginMarquee(Point point, ModifierKeys modifiers)
    {
        _marqueeActive = true;
        _marqueeMoved = false;
        _pressPoint = point;
        _marqueeRect = new Rect(point, point);
        _marqueeBaseSelection.Clear();

        bool additive = modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Shift);
        if (additive)
        {
            foreach (Guid id in _selectedNodeIds)
                _marqueeBaseSelection.Add(id);
        }
        else
        {
            _selectedNodeIds.Clear();
            _selectedNode = null;
            PublishSelection();
        }

        CaptureMouse();
        InvalidateVisual();
    }

    private void ContinueMarquee(Point point)
    {
        Vector delta = point - _pressPoint;
        if (!_marqueeMoved && delta.Length > DragThreshold)
            _marqueeMoved = true;

        _marqueeRect = NormalizeRect(_pressPoint, point);
        if (!_marqueeMoved)
        {
            InvalidateVisual();
            return;
        }

        var next = new HashSet<Guid>(_marqueeBaseSelection);
        if (Graph is not null)
        {
            foreach (AudioNodeModel node in Graph.Nodes)
            {
                Rect nodeRect = new(node.X, node.Y, NodeWidth, GetNodeHeight(node));
                if (_marqueeRect.IntersectsWith(nodeRect))
                    next.Add(node.Id);
            }
        }

        if (!_selectedNodeIds.SetEquals(next))
        {
            _selectedNodeIds.Clear();
            _selectedNodeIds.UnionWith(next);
            _selectedNode = Graph?.Nodes.LastOrDefault(x => _selectedNodeIds.Contains(x.Id));
            PublishSelection();
        }

        InvalidateVisual();
    }

    private void EndMarquee()
    {
        if (!_marqueeMoved && _marqueeBaseSelection.Count == 0)
        {
            _selectedNodeIds.Clear();
            _selectedNode = null;
            PublishSelection();
        }

        _marqueeActive = false;
        _marqueeMoved = false;
        _marqueeBaseSelection.Clear();
        ReleaseMouseCaptureIfOwned();
        InvalidateVisual();
    }

    private void BeginRightPanCandidate(MouseButtonEventArgs e)
    {
        ScrollViewer? scroll = GetScrollViewer();
        _rightButtonActive = true;
        _rightPanning = false;

        if (scroll is not null)
        {
            _rightPressViewport = e.GetPosition(scroll);
            _rightStartHorizontalOffset = scroll.HorizontalOffset;
            _rightStartVerticalOffset = scroll.VerticalOffset;
        }
        else
        {
            _rightPressViewport = _lastPointerPoint;
            _rightStartHorizontalOffset = 0;
            _rightStartVerticalOffset = 0;
        }

        CaptureMouse();
    }

    private void ContinueRightPan(MouseEventArgs e)
    {
        ScrollViewer? scroll = GetScrollViewer();
        if (scroll is null) return;

        Point current = e.GetPosition(scroll);
        Vector delta = current - _rightPressViewport;
        if (!_rightPanning && delta.Length > DragThreshold)
        {
            _rightPanning = true;
            Cursor = Cursors.ScrollAll;
        }

        if (!_rightPanning) return;

        scroll.ScrollToHorizontalOffset(_rightStartHorizontalOffset - delta.X);
        scroll.ScrollToVerticalOffset(_rightStartVerticalOffset - delta.Y);
    }

    private void EndRightPan()
    {
        _rightButtonActive = false;
        _rightPanning = false;
        Cursor = Cursors.Arrow;
        ReleaseMouseCaptureIfOwned();
    }

    private void CompleteCable(Point point)
    {
        if (_cableStartPort is null) return;

        PortHit? target = HitTestPort(point);
        if (target is not null &&
            target.Port.Id != _cableStartPort.Id &&
            Graph is not null)
        {
            if (Graph.TryConnect(_cableStartPort, target.Port, out string error))
                ConnectionChanged?.Invoke(this, EventArgs.Empty);
            else
                ConnectionRejected?.Invoke(this, new GraphMessageEventArgs(error));
        }

        _cableStartPort = null;
        ReleaseMouseCaptureIfOwned();
        InvalidateVisual();
    }

    private void ZoomAroundCursor(MouseWheelEventArgs e, ScrollViewer scroll)
    {
        Point logical = e.GetPosition(this);
        Point viewport = e.GetPosition(scroll);
        double factor = e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep;
        double next = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(next - _zoom) < 0.0001) return;

        _zoom = next;
        _zoomTransform.ScaleX = next;
        _zoomTransform.ScaleY = next;
        UpdateLayout();

        scroll.ScrollToHorizontalOffset(Math.Max(0, logical.X * next - viewport.X));
        scroll.ScrollToVerticalOffset(Math.Max(0, logical.Y * next - viewport.Y));
        InvalidateVisual();
    }

    private void RaiseContextMenu(Point point)
    {
        PortHit? port = HitTestPort(point);
        AudioNodeModel? node = port?.Node ?? HitTestNode(point);
        AudioConnectionModel? connection = node is null && port is null ? HitTestConnection(point) : null;

        ContextMenuRequested?.Invoke(
            this,
            new GraphContextRequestEventArgs(point, node, connection, port?.Port));
    }

    private void UpdateHover(Point point)
    {
        AudioNodeModel? hoverNode = HitTestNode(point);
        AudioConnectionModel? hoverConnection = hoverNode is null ? HitTestConnection(point) : null;

        if (!ReferenceEquals(hoverNode, _hoverNode) ||
            !ReferenceEquals(hoverConnection, _hoverConnection))
        {
            _hoverNode = hoverNode;
            _hoverConnection = hoverConnection;
            InvalidateVisual();
        }
    }

    private AudioNodeModel? HitTestNode(Point point) =>
        Graph?.Nodes.LastOrDefault(node =>
            new Rect(node.X, node.Y, NodeWidth, GetNodeHeight(node)).Contains(point));

    private PortHit? HitTestPort(Point point)
    {
        if (Graph is null) return null;

        double tolerance = 12 / Math.Max(_zoom, 0.35);
        foreach (AudioNodeModel node in Graph.Nodes.Reverse())
        {
            for (int i = 0; i < node.Inputs.Count; i++)
            {
                AudioPortModel port = node.Inputs[i];
                if ((GetPortPoint(node, port, i) - point).Length <= tolerance)
                    return new PortHit(node, port);
            }

            for (int i = 0; i < node.Outputs.Count; i++)
            {
                AudioPortModel port = node.Outputs[i];
                if ((GetPortPoint(node, port, i) - point).Length <= tolerance)
                    return new PortHit(node, port);
            }
        }

        return null;
    }

    private AudioConnectionModel? HitTestConnection(Point point)
    {
        if (Graph is null) return null;

        double tolerance = 10 / Math.Max(_zoom, 0.35);
        foreach (AudioConnectionModel connection in Graph.Connections.Reverse())
        {
            if (!TryGetConnectionPoints(connection, out Point start, out Point end, out _))
                continue;

            Point c1 = Control1(start, end);
            Point c2 = Control2(start, end);
            Point previous = start;

            for (int i = 1; i <= 32; i++)
            {
                double t = i / 32.0;
                Point current = CubicPoint(start, c1, c2, end, t);
                if (DistanceToSegment(point, previous, current) <= tolerance)
                    return connection;
                previous = current;
            }
        }

        return null;
    }

    private bool TryGetConnectionPoints(
        AudioConnectionModel connection,
        out Point start,
        out Point end,
        out AudioPortModel? targetPort)
    {
        start = default;
        end = default;
        targetPort = null;
        if (Graph is null) return false;

        AudioNodeModel? sourceNode = Graph.Nodes.FirstOrDefault(x => x.Id == connection.SourceNodeId);
        AudioNodeModel? targetNode = Graph.Nodes.FirstOrDefault(x => x.Id == connection.TargetNodeId);
        AudioPortModel? sourcePort = sourceNode?.Outputs.FirstOrDefault(x => x.Id == connection.SourcePortId);
        targetPort = targetNode?.Inputs.FirstOrDefault(x => x.Id == connection.TargetPortId);
        if (sourceNode is null || targetNode is null || sourcePort is null || targetPort is null)
            return false;

        start = GetPortPoint(sourceNode, sourcePort);
        end = GetPortPoint(targetNode, targetPort);
        return true;
    }

    private static Point Control1(Point start, Point end)
    {
        double bend = Math.Max(58, Math.Abs(end.X - start.X) * 0.42);
        return new Point(start.X + bend, start.Y);
    }

    private static Point Control2(Point start, Point end)
    {
        double bend = Math.Max(58, Math.Abs(end.X - start.X) * 0.42);
        return new Point(end.X - bend, end.Y);
    }

    private static Point CubicPoint(Point p0, Point p1, Point p2, Point p3, double t)
    {
        double u = 1 - t;
        double tt = t * t;
        double uu = u * u;
        double uuu = uu * u;
        double ttt = tt * t;

        return new Point(
            uuu * p0.X + 3 * uu * t * p1.X + 3 * u * tt * p2.X + ttt * p3.X,
            uuu * p0.Y + 3 * uu * t * p1.Y + 3 * u * tt * p2.Y + ttt * p3.Y);
    }

    private static double DistanceToSegment(Point point, Point a, Point b)
    {
        Vector ab = b - a;
        double lengthSquared = ab.X * ab.X + ab.Y * ab.Y;
        if (lengthSquared <= 0.000001)
            return (point - a).Length;

        Vector ap = point - a;
        double t = Math.Clamp((ap.X * ab.X + ap.Y * ab.Y) / lengthSquared, 0, 1);
        Point projection = a + ab * t;
        return (point - projection).Length;
    }

    private static Rect NormalizeRect(Point a, Point b) =>
        new(
            new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
            new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));

    private void EnsureWorkspaceForSelection()
    {
        if (SelectedNodes.Count == 0) return;

        double neededWidth = SelectedNodes.Max(node => node.X + NodeWidth + 350);
        double neededHeight = SelectedNodes.Max(node => node.Y + GetNodeHeight(node) + 300);

        if (double.IsNaN(Width) || Width < neededWidth)
            Width = Math.Max(2300, neededWidth);
        if (double.IsNaN(Height) || Height < neededHeight)
            Height = Math.Max(1200, neededHeight);
    }

    private void CancelPointerOperation()
    {
        _cableStartPort = null;
        _dragNode = null;
        _dragOrigins.Clear();
        _dragStarted = false;
        _marqueeActive = false;
        _marqueeMoved = false;
        _marqueeBaseSelection.Clear();

        if (_rightButtonActive)
            EndRightPan();
        else
            ReleaseMouseCaptureIfOwned();

        InvalidateVisual();
    }

    private void PublishSelection()
    {
        IReadOnlyList<AudioNodeModel> selected = SelectedNodes;
        NodeSelected?.Invoke(this, new NodeSelectionChangedEventArgs(_selectedNode, selected));
    }

    private void ReleaseMouseCaptureIfOwned()
    {
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private ScrollViewer? GetScrollViewer()
    {
        _scrollViewer ??= FindAncestor<ScrollViewer>(this);
        return _scrollViewer;
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static Point GetPortPoint(AudioNodeModel node, AudioPortModel port)
    {
        int index = port.Direction == AudioPortDirection.Input
            ? node.Inputs.IndexOf(port)
            : node.Outputs.IndexOf(port);
        return GetPortPoint(node, port, Math.Max(index, 0));
    }

    private static Point GetPortPoint(AudioNodeModel node, AudioPortModel port, int index) =>
        new(
            port.Direction == AudioPortDirection.Input ? node.X : node.X + NodeWidth,
            node.Y + PortStartY + index * PortSpacing);

    private static Color Accent(AudioNodeType type) => type switch
    {
        AudioNodeType.Input => Mint,
        AudioNodeType.Output => Aqua,
        AudioNodeType.Limiter => Pink,
        AudioNodeType.Mixer => Gold,
        AudioNodeType.AiProcessor => Aqua,
        AudioNodeType.Ducker => Pink,
        _ => Purple
    };

    private static void OnGraphChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var canvas = (NodeGraphCanvas)dependencyObject;
        canvas.DetachGraph(args.OldValue as AudioGraphModel);
        canvas.AttachGraph(args.NewValue as AudioGraphModel);
        canvas.ClearSelection();
        canvas.InvalidateVisual();
    }

    private void AttachGraph(AudioGraphModel? graph)
    {
        if (graph is null) return;
        graph.Nodes.CollectionChanged += GraphCollectionChanged;
        graph.Connections.CollectionChanged += GraphCollectionChanged;
        foreach (AudioNodeModel node in graph.Nodes)
            AttachNode(node);
    }

    private void DetachGraph(AudioGraphModel? graph)
    {
        if (graph is null) return;
        graph.Nodes.CollectionChanged -= GraphCollectionChanged;
        graph.Connections.CollectionChanged -= GraphCollectionChanged;
        foreach (AudioNodeModel node in graph.Nodes)
            DetachNode(node);
    }

    private void GraphCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender == Graph?.Nodes)
        {
            if (e.OldItems is not null)
                foreach (AudioNodeModel node in e.OldItems) DetachNode(node);
            if (e.NewItems is not null)
                foreach (AudioNodeModel node in e.NewItems) AttachNode(node);

            _selectedNodeIds.RemoveWhere(id => Graph?.Nodes.All(node => node.Id != id) ?? true);
            if (_selectedNode is not null && !_selectedNodeIds.Contains(_selectedNode.Id))
                _selectedNode = Graph?.Nodes.LastOrDefault(node => _selectedNodeIds.Contains(node.Id));
        }

        InvalidateVisual();
    }

    private void AttachNode(AudioNodeModel node)
    {
        node.PropertyChanged += NodePropertyChanged;
        node.Profile.PropertyChanged += NodePropertyChanged;
    }

    private void DetachNode(AudioNodeModel node)
    {
        node.PropertyChanged -= NodePropertyChanged;
        node.Profile.PropertyChanged -= NodePropertyChanged;
    }

    private void NodePropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private sealed record PortHit(AudioNodeModel Node, AudioPortModel Port);
}
