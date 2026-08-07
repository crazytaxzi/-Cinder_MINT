using Cinder.MINT.Models;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Cinder.MINT.Controls;

public sealed class NodeSelectionChangedEventArgs(AudioNodeModel? node) : EventArgs
{
    public AudioNodeModel? Node { get; } = node;
}

public sealed class GraphMessageEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

public sealed class NodeGraphCanvas : FrameworkElement
{
    public const double NodeWidth = 204;
    private const double BaseNodeHeight = 100;
    private const double PortSpacing = 23;
    private const double PortStartY = 73;
    private const double SocketRadius = 6.5;

    private static readonly Color Mint = Color.FromRgb(125, 255, 214);
    private static readonly Color Aqua = Color.FromRgb(66, 232, 224);
    private static readonly Color Purple = Color.FromRgb(167, 107, 255);
    private static readonly Color Pink = Color.FromRgb(255, 95, 170);
    private static readonly Color Gold = Color.FromRgb(255, 211, 107);
    private static readonly Color Text = Color.FromRgb(242, 255, 249);
    private static readonly Color Muted = Color.FromRgb(143, 167, 170);

    private AudioNodeModel? _dragNode;
    private AudioNodeModel? _hoverNode;
    private AudioNodeModel? _selectedNode;
    private AudioPortModel? _cableStartPort;
    private Point _dragOffset;
    private Point _pressPoint;
    private Point _cablePoint;
    private bool _dragStarted;

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

    public event EventHandler<AudioNodeModel>? NodeToggled;
    public event EventHandler<AudioNodeModel>? NodeMoved;
    public event EventHandler<AudioNodeModel>? NodeDeleteRequested;
    public event EventHandler<NodeSelectionChangedEventArgs>? NodeSelected;
    public event EventHandler? ConnectionChanged;
    public event EventHandler<GraphMessageEventArgs>? ConnectionRejected;

    public NodeGraphCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public void ClearSelection()
    {
        _selectedNode = null;
        NodeSelected?.Invoke(this, new NodeSelectionChangedEventArgs(null));
        InvalidateVisual();
    }

    public void SelectNode(AudioNodeModel? node)
    {
        _selectedNode = node;
        NodeSelected?.Invoke(this, new NodeSelectionChangedEventArgs(node));
        InvalidateVisual();
    }

    public void RefreshVisual() => InvalidateVisual();

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
        if (Graph is null) return;
        AudioNodeModel? sourceNode = Graph.Nodes.FirstOrDefault(x => x.Id == connection.SourceNodeId);
        AudioNodeModel? targetNode = Graph.Nodes.FirstOrDefault(x => x.Id == connection.TargetNodeId);
        AudioPortModel? sourcePort = sourceNode?.Outputs.FirstOrDefault(x => x.Id == connection.SourcePortId);
        AudioPortModel? targetPort = targetNode?.Inputs.FirstOrDefault(x => x.Id == connection.TargetPortId);
        if (sourceNode is null || targetNode is null || sourcePort is null || targetPort is null) return;

        Point start = GetPortPoint(sourceNode, sourcePort);
        Point end = GetPortPoint(targetNode, targetPort);
        Color endColor = targetPort.Kind == AudioPortKind.Sidechain ? Pink : Purple;

        DrawBezier(dc, start, end, Mint, endColor, 3.2);
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
        double bend = Math.Max(58, Math.Abs(end.X - start.X) * 0.42);
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = start };
        figure.Segments.Add(new BezierSegment(
            new Point(start.X + bend, start.Y),
            new Point(end.X - bend, end.Y),
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
        bool selected = ReferenceEquals(node, _selectedNode);
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

    private static string NodeBadge(AudioNodeModel node)
    {
        if (!node.Enabled) return "OFF";
        if (node.Type == AudioNodeType.Input) return "SOURCE";
        if (node.Type == AudioNodeType.Output) return "OUT";
        if (node.Type == AudioNodeType.Mixer) return "BUS";
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
        Point point = e.GetPosition(this);
        PortHit? portHit = HitTestPort(point);

        if (e.ChangedButton == MouseButton.Right)
        {
            if (portHit is not null && Graph is not null)
            {
                Graph.DisconnectPort(portHit.Port.Id);
                ConnectionChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            AudioNodeModel? rightNode = HitTestNode(point);
            if (rightNode is not null)
            {
                SelectNode(rightNode);
                e.Handled = true;
            }
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        if (portHit is not null)
        {
            SelectNode(portHit.Node);
            _cableStartPort = portHit.Port;
            _cablePoint = point;
            Mouse.Capture(this);
            e.Handled = true;
            return;
        }

        AudioNodeModel? hit = HitTestNode(point);
        if (hit is null)
        {
            ClearSelection();
            return;
        }

        if (e.ClickCount == 2 && hit.CanBypass)
        {
            hit.Enabled = !hit.Enabled;
            NodeToggled?.Invoke(this, hit);
            SelectNode(hit);
            e.Handled = true;
            return;
        }

        SelectNode(hit);
        _dragNode = hit;
        _pressPoint = point;
        _dragOffset = new Point(point.X - hit.X, point.Y - hit.Y);
        _dragStarted = false;
        Mouse.Capture(this);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point point = e.GetPosition(this);

        if (_cableStartPort is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            _cablePoint = point;
            InvalidateVisual();
            return;
        }

        if (_dragNode is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_dragStarted && (point - _pressPoint).Length > 3)
                _dragStarted = true;

            if (_dragStarted)
            {
                _dragNode.X = Math.Max(10, point.X - _dragOffset.X);
                _dragNode.Y = Math.Max(10, point.Y - _dragOffset.Y);
                NodeMoved?.Invoke(this, _dragNode);
                InvalidateVisual();
            }
            return;
        }

        AudioNodeModel? hover = HitTestNode(point);
        if (!ReferenceEquals(hover, _hoverNode))
        {
            _hoverNode = hover;
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (_cableStartPort is not null)
        {
            PortHit? target = HitTestPort(e.GetPosition(this));
            if (target is not null && target.Port.Id != _cableStartPort.Id && Graph is not null)
            {
                if (Graph.TryConnect(_cableStartPort, target.Port, out string error))
                    ConnectionChanged?.Invoke(this, EventArgs.Empty);
                else
                    ConnectionRejected?.Invoke(this, new GraphMessageEventArgs(error));
            }

            _cableStartPort = null;
            Mouse.Capture(null);
            InvalidateVisual();
            return;
        }

        _dragNode = null;
        _dragStarted = false;
        Mouse.Capture(null);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragNode is null && _cableStartPort is null)
        {
            _hoverNode = null;
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            _cableStartPort = null;
            Mouse.Capture(null);
            InvalidateVisual();
            return;
        }

        if (e.Key == Key.Delete && _selectedNode is not null)
        {
            NodeDeleteRequested?.Invoke(this, _selectedNode);
            e.Handled = true;
        }
    }

    private AudioNodeModel? HitTestNode(Point point) =>
        Graph?.Nodes.LastOrDefault(node =>
            new Rect(node.X, node.Y, NodeWidth, GetNodeHeight(node)).Contains(point));

    private PortHit? HitTestPort(Point point)
    {
        if (Graph is null) return null;

        foreach (AudioNodeModel node in Graph.Nodes.Reverse())
        {
            for (int i = 0; i < node.Inputs.Count; i++)
            {
                AudioPortModel port = node.Inputs[i];
                if ((GetPortPoint(node, port, i) - point).Length <= 12)
                    return new PortHit(node, port);
            }

            for (int i = 0; i < node.Outputs.Count; i++)
            {
                AudioPortModel port = node.Outputs[i];
                if ((GetPortPoint(node, port, i) - point).Length <= 12)
                    return new PortHit(node, port);
            }
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
