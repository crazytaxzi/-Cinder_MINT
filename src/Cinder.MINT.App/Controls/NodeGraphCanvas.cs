using Cinder.MINT.Models;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Cinder.MINT.Controls;

public sealed class NodeGraphCanvas : FrameworkElement
{
    private const double NodeWidth = 156;
    private const double NodeHeight = 72;
    private AudioNodeModel? _dragNode;
    private Point _dragOffset;
    private AudioNodeModel? _hoverNode;

    public static readonly DependencyProperty GraphProperty =
        DependencyProperty.Register(
            nameof(Graph),
            typeof(AudioGraphModel),
            typeof(NodeGraphCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public AudioGraphModel? Graph
    {
        get => (AudioGraphModel?)GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public event EventHandler<AudioNodeModel>? NodeToggled;

    public NodeGraphCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        DrawGrid(dc);

        if (Graph is null) return;

        foreach (AudioConnectionModel connection in Graph.Connections)
        {
            AudioNodeModel? source = Graph.Nodes.FirstOrDefault(x => x.Id == connection.SourceId);
            AudioNodeModel? target = Graph.Nodes.FirstOrDefault(x => x.Id == connection.TargetId);
            if (source is null || target is null) continue;
            DrawCable(dc, source, target);
        }

        foreach (AudioNodeModel node in Graph.Nodes)
            DrawNode(dc, node);
    }

    private void DrawGrid(DrawingContext dc)
    {
        var minor = new Pen(new SolidColorBrush(Color.FromArgb(20, 135, 145, 175)), 1);
        var major = new Pen(new SolidColorBrush(Color.FromArgb(30, 183, 255, 42)), 1);

        for (double x = 0; x < ActualWidth; x += 24)
            dc.DrawLine(((int)x % 96 == 0) ? major : minor, new Point(x, 0), new Point(x, ActualHeight));

        for (double y = 0; y < ActualHeight; y += 24)
            dc.DrawLine(((int)y % 96 == 0) ? major : minor, new Point(0, y), new Point(ActualWidth, y));
    }

    private static void DrawCable(DrawingContext dc, AudioNodeModel source, AudioNodeModel target)
    {
        Point start = new(source.X + NodeWidth, source.Y + NodeHeight / 2);
        Point end = new(target.X, target.Y + NodeHeight / 2);
        double bend = Math.Max(50, Math.Abs(end.X - start.X) * 0.45);

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
            new Pen(new LinearGradientBrush(
                Color.FromRgb(183, 255, 42),
                Color.FromRgb(168, 85, 247),
                0), 3),
            geometry);

        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(183, 255, 42)), null, start, 4, 4);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(168, 85, 247)), null, end, 4, 4);
    }

    private void DrawNode(DrawingContext dc, AudioNodeModel node)
    {
        Rect rect = new(node.X, node.Y, NodeWidth, NodeHeight);
        bool hot = ReferenceEquals(node, _hoverNode);
        Color accent = node.Type switch
        {
            AudioNodeType.VoiceSource or AudioNodeType.ProgramSource => Color.FromRgb(183, 255, 42),
            AudioNodeType.Output or AudioNodeType.Limiter => Color.FromRgb(255, 79, 163),
            _ => Color.FromRgb(168, 85, 247)
        };

        var background = new SolidColorBrush(node.Enabled
            ? Color.FromRgb(20, 23, 35)
            : Color.FromRgb(39, 39, 45));
        var border = new Pen(new SolidColorBrush(node.Enabled ? accent : Color.FromRgb(94, 96, 108)), hot ? 2.5 : 1.5);

        dc.DrawRoundedRectangle(background, border, rect, 12, 12);
        dc.DrawRoundedRectangle(new SolidColorBrush(accent), null, new Rect(rect.X, rect.Y, 5, rect.Height), 3, 3);

        var title = new FormattedText(
            node.Title,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            13,
            node.Enabled ? Brushes.White : Brushes.Gray,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var subtitle = new FormattedText(
            node.Enabled ? node.Subtitle : "BYPASSED",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            10.5,
            new SolidColorBrush(node.Enabled ? Color.FromRgb(157, 163, 180) : Color.FromRgb(255, 191, 71)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(title, new Point(rect.X + 16, rect.Y + 16));
        dc.DrawText(subtitle, new Point(rect.X + 16, rect.Y + 40));
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        Point point = e.GetPosition(this);
        AudioNodeModel? hit = HitTestNode(point);
        if (hit is null) return;

        if (e.ClickCount == 2 && hit.Type is not AudioNodeType.VoiceSource
            and not AudioNodeType.ProgramSource
            and not AudioNodeType.Mixer
            and not AudioNodeType.Output)
        {
            hit.Enabled = !hit.Enabled;
            NodeToggled?.Invoke(this, hit);
            InvalidateVisual();
            return;
        }

        _dragNode = hit;
        _dragOffset = new Point(point.X - hit.X, point.Y - hit.Y);
        Mouse.Capture(this);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point point = e.GetPosition(this);

        if (_dragNode is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            _dragNode.X = Math.Max(8, point.X - _dragOffset.X);
            _dragNode.Y = Math.Max(8, point.Y - _dragOffset.Y);
            InvalidateVisual();
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
        _dragNode = null;
        Mouse.Capture(null);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverNode = null;
        InvalidateVisual();
    }

    private AudioNodeModel? HitTestNode(Point point) =>
        Graph?.Nodes.LastOrDefault(node =>
            new Rect(node.X, node.Y, NodeWidth, NodeHeight).Contains(point));
}
