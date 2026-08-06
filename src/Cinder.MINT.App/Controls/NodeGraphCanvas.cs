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
    public const double NodeWidth = 190;
    private const double BaseNodeHeight = 92;
    private const double PortSpacing = 22;
    private const double PortStartY = 67;
    private const double SocketRadius = 6;

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
        Math.Max(BaseNodeHeight, PortStartY + Math.Max(node.Inputs.Count, node.Outputs.Count) * PortSpacing + 10);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        DrawGrid(dc);

        if (Graph is null) return;

        foreach (AudioConnectionModel connection in Graph.Connections)
            DrawConnection(dc, connection);

        if (_cableStartPort is not null)
            DrawPendingCable(dc, _cableStartPort, _cablePoint);

        foreach (AudioNodeModel node in Graph.Nodes)
            DrawNode(dc, node);
    }

    private void DrawGrid(DrawingContext dc)
    {
        var minor = new Pen(new SolidColorBrush(Color.FromArgb(20, 135, 145, 175)), 1);
        var major = new Pen(new SolidColorBrush(Color.FromArgb(32, 183, 255, 42)), 1);

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
        Color endColor = targetPort.Kind == AudioPortKind.Sidechain
            ? Color.FromRgb(255, 79, 163)
            : Color.FromRgb(168, 85, 247);

        DrawBezier(dc, start, end, Color.FromRgb(183, 255, 42), endColor, 3);
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
            Color.FromRgb(183, 255, 42),
            port.Kind == AudioPortKind.Sidechain ? Color.FromRgb(255, 79, 163) : Color.FromRgb(168, 85, 247),
            2.2);
    }

    private static void DrawBezier(
        DrawingContext dc,
        Point start,
        Point end,
        Color startColor,
        Color endColor,
        double thickness)
    {
        double bend = Math.Max(55, Math.Abs(end.X - start.X) * 0.42);
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = start };
        figure.Segments.Add(new BezierSegment(
            new Point(start.X + bend, start.Y),
            new Point(end.X - bend, end.Y),
            end,
            true));
        geometry.Figures.Add(figure);

        dc.DrawGeometry(
            null,
            new Pen(new LinearGradientBrush(startColor, endColor, 0), thickness),
            geometry);
    }

    private void DrawNode(DrawingContext dc, AudioNodeModel node)
    {
        double nodeHeight = GetNodeHeight(node);
        Rect rect = new(node.X, node.Y, NodeWidth, nodeHeight);
        bool hovered = ReferenceEquals(node, _hoverNode);
        bool selected = ReferenceEquals(node, _selectedNode);
        Color accent = Accent(node.Type);

        var background = new SolidColorBrush(node.Enabled
            ? Color.FromRgb(20, 23, 35)
            : Color.FromRgb(39, 39, 45));
        var borderColor = node.Enabled ? accent : Color.FromRgb(94, 96, 108);
        var border = new Pen(new SolidColorBrush(borderColor), selected ? 3 : hovered ? 2.2 : 1.4);

        if (selected)
        {
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromArgb(42, accent.R, accent.G, accent.B)),
                null,
                new Rect(rect.X - 6, rect.Y - 6, rect.Width + 12, rect.Height + 12),
                16,
                16);
        }

        dc.DrawRoundedRectangle(background, border, rect, 12, 12);
        dc.DrawRoundedRectangle(new SolidColorBrush(accent), null, new Rect(rect.X, rect.Y, 5, rect.Height), 3, 3);

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var title = new FormattedText(
            node.Title,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            13,
            node.Enabled ? Brushes.White : Brushes.Gray,
            dpi)
        {
            MaxTextWidth = NodeWidth - 28,
            Trimming = TextTrimming.CharacterEllipsis
        };

        var subtitle = new FormattedText(
            node.Enabled ? node.DisplaySubtitle : "BYPASSED",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            10.2,
            new SolidColorBrush(node.Enabled ? Color.FromRgb(157, 163, 180) : Color.FromRgb(255, 191, 71)),
            dpi)
        {
            MaxTextWidth = NodeWidth - 28,
            Trimming = TextTrimming.CharacterEllipsis
        };

        dc.DrawText(title, new Point(rect.X + 16, rect.Y + 14));
        dc.DrawText(subtitle, new Point(rect.X + 16, rect.Y + 38));

        for (int i = 0; i < node.Inputs.Count; i++)
            DrawPort(dc, node, node.Inputs[i], i, dpi);

        for (int i = 0; i < node.Outputs.Count; i++)
            DrawPort(dc, node, node.Outputs[i], i, dpi);
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
            ? Color.FromRgb(255, 79, 163)
            : port.Direction == AudioPortDirection.Output
                ? Color.FromRgb(183, 255, 42)
                : Color.FromRgb(168, 85, 247);

        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(8, 9, 16)), new Pen(new SolidColorBrush(color), 2), point, SocketRadius, SocketRadius);
        dc.DrawEllipse(new SolidColorBrush(color), null, point, 2.4, 2.4);

        var label = new FormattedText(
            port.Name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            9.2,
            new SolidColorBrush(color),
            dpi);

        double y = point.Y - label.Height / 2;
        if (port.Direction == AudioPortDirection.Input)
            dc.DrawText(label, new Point(node.X + 12, y));
        else
            dc.DrawText(label, new Point(node.X + NodeWidth - 12 - label.Width, y));
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
                if ((GetPortPoint(node, port, i) - point).Length <= 11)
                    return new PortHit(node, port);
            }

            for (int i = 0; i < node.Outputs.Count; i++)
            {
                AudioPortModel port = node.Outputs[i];
                if ((GetPortPoint(node, port, i) - point).Length <= 11)
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
        AudioNodeType.Input => Color.FromRgb(183, 255, 42),
        AudioNodeType.Output or AudioNodeType.Limiter => Color.FromRgb(255, 79, 163),
        AudioNodeType.Mixer => Color.FromRgb(255, 191, 71),
        _ => Color.FromRgb(168, 85, 247)
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
