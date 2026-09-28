using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FlowBoard.Models;
using FlowBoard.Services;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

/// <summary>
/// Pan/zoom surface and all pointer interaction for the canvas: select, move, resize, connect, marquee, create.
/// The view model owns the data; this class only translates mouse and keyboard input.
/// </summary>
public partial class CanvasView : UserControl, Helpers.ICapturable
{
    private enum Drag { None, Pan, Move, Resize, Connect, Marquee, Pen, DrawLine, DrawFrame, MoveEdge, EdgeEnd, Bend }

    private CanvasViewModel? _vm;
    private readonly DrawingBrush _dots;
    private Drag _drag;
    private Point _start;            // screen point where the drag began
    private Point _last;             // last screen point
    private bool _moved;
    private CanvasNode? _target;     // node being moved / resized / connected from
    private CanvasNode? _clickedMember; // shape pressed inside a section (the section is what moves)
    private Dictionary<CanvasNode, Point> _startPositions = new();
    private Size _startSize;
    private bool _spaceDown;
    private readonly List<(double X, double Y)> _penPoints = [];
    private CanvasNode? _lineStartNode;
    private Point _lineStartWorld;
    private CanvasEdge? _edge;
    private bool _edgeFrom;
    private List<CanvasEdge> _movingEdges = [];
    private Dictionary<CanvasEdge, (double FromX, double FromY, double ToX, double ToY)> _edgeStarts = new();

    public CanvasView()
    {
        InitializeComponent();
        _dots = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        Dots.Fill = _dots;
        UpdateDotBrush();

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.Doc.PropertyChanged -= OnDocChanged;
                _vm.RenderRequested -= OnRenderRequested;
                _vm.ContentChanged -= OnContentChanged;
                StopMinimap();
            }

            _vm = DataContext as CanvasViewModel;
            if (_vm == null) return;
            _vm.Doc.PropertyChanged += OnDocChanged;
            _vm.RenderRequested += OnRenderRequested;
            _vm.ContentChanged += OnContentChanged;
            ApplyTransform();
            if (IsLoaded) StartMinimap();
        };
        Loaded += (_, _) =>
        {
            Focus();
            ThemeService.ThemeApplied += OnThemeApplied;
            StartMinimap();
        };
        Unloaded += (_, _) =>
        {
            ThemeService.ThemeApplied -= OnThemeApplied;
            StopMinimap();
        };
        PreviewKeyDown += OnKey;
        KeyUp += (_, e) =>
        {
            if (e.Key == Key.Space) _spaceDown = false;
        };
    }

    private void OnThemeApplied(object? sender, EventArgs e) => UpdateDotBrush();

    // ================= minimap =================
    // A small vector drawing of the canvas (shapes as colored blocks, lines as strokes), redrawn only when
    // something changes and at most ~30 times a second. The visible area is the accent rectangle.

    private readonly System.Windows.Threading.DispatcherTimer _miniTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private Box _miniBounds;
    private double _miniScale = 1;
    private double _miniOx, _miniOy;
    private bool _miniDragging;
    private Vector _miniGrab;

    private void StartMinimap()
    {
        _miniTimer.Tick -= OnMiniTick;
        _miniTimer.Tick += OnMiniTick;
        if (_vm != null)
        {
            _vm.Main.Settings.PropertyChanged -= OnSettingsChanged;
            _vm.Main.Settings.PropertyChanged += OnSettingsChanged;
        }

        InvalidateMinimap();
    }

    private void StopMinimap()
    {
        _miniTimer.Stop();
        if (_vm != null) _vm.Main.Settings.PropertyChanged -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.ShowMinimap)) InvalidateMinimap();
    }

    private void OnContentChanged(object? sender, EventArgs e) => InvalidateMinimap();

    /// <summary>Schedules one redraw; many changes in a row (dragging) collapse into a single frame.</summary>
    private void InvalidateMinimap()
    {
        if (!_miniTimer.IsEnabled) _miniTimer.Start();
    }

    private void OnMiniTick(object? sender, EventArgs e)
    {
        _miniTimer.Stop();
        UpdateMinimap();
    }

    private Box ViewBox()
    {
        var d = _vm!.Doc;
        return new Box(-d.OffsetX / d.Zoom, -d.OffsetY / d.Zoom, Surface.ActualWidth / d.Zoom, Surface.ActualHeight / d.Zoom);
    }

    private void UpdateMinimap()
    {
        if (_vm == null) return;
        var show = _vm.Main.Settings.ShowMinimap && !_vm.IsEmpty;
        MiniPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MiniShow.Visibility = !_vm.Main.Settings.ShowMinimap && !_vm.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        var view = ViewBox();
        if (!_miniDragging)
        {
            // While the map is being dragged its scale stays put, so the map doesn't slide under the pointer.
            var content = _vm.ContentBounds(40);
            double l = Math.Min(content.X, view.X), t = Math.Min(content.Y, view.Y);
            double r = Math.Max(content.Right, view.Right), b = Math.Max(content.Bottom, view.Bottom);
            _miniBounds = new Box(l, t, Math.Max(1, r - l), Math.Max(1, b - t));
            _miniScale = Math.Min(MiniMap.Width / _miniBounds.W, MiniMap.Height / _miniBounds.H);
            _miniOx = (MiniMap.Width - _miniBounds.W * _miniScale) / 2;
            _miniOy = (MiniMap.Height - _miniBounds.H * _miniScale) / 2;
        }

        MiniImage.Source = DrawMinimap();
        var vx = _miniOx + (view.X - _miniBounds.X) * _miniScale;
        var vy = _miniOy + (view.Y - _miniBounds.Y) * _miniScale;
        Canvas.SetLeft(MiniViewport, vx);
        Canvas.SetTop(MiniViewport, vy);
        MiniViewport.Width = Math.Max(4, view.W * _miniScale);
        MiniViewport.Height = Math.Max(4, view.H * _miniScale);
    }

    private Point MiniPoint(double x, double y) => new(_miniOx + (x - _miniBounds.X) * _miniScale, _miniOy + (y - _miniBounds.Y) * _miniScale);

    private DrawingImage DrawMinimap()
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            // Fixed-size backdrop so the drawing maps 1:1 onto the map.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, MiniMap.Width, MiniMap.Height));
            var muted = TryFindResource("Fb.MutedTextBrush") as Brush ?? Brushes.Gray;
            var s = _miniScale;

            foreach (var n in _vm!.Doc.Nodes.OrderBy(n => n.Z))
            {
                var p = MiniPoint(n.X, n.Y);
                var rect = new Rect(p.X, p.Y, Math.Max(1.5, n.Width * s), Math.Max(1.5, n.Height * s));
                var color = ThemeService.TryParseColor(n.Fill, out var c) ? c : Colors.Gray;
                var fill = new SolidColorBrush(color);
                switch (n.Shape)
                {
                    case NodeShape.Frame:
                        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(28, color.R, color.G, color.B)), new Pen(fill, 1) { DashStyle = DashStyles.Dash }, rect, 2, 2);
                        break;
                    case NodeShape.Ellipse or NodeShape.Circle:
                        dc.DrawEllipse(fill, null, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), rect.Width / 2, rect.Height / 2);
                        break;
                    case NodeShape.Diamond:
                        var g = new StreamGeometry();
                        using (var ctx = g.Open())
                        {
                            ctx.BeginFigure(new Point(rect.X + rect.Width / 2, rect.Y), true, true);
                            ctx.LineTo(new Point(rect.Right, rect.Y + rect.Height / 2), false, false);
                            ctx.LineTo(new Point(rect.X + rect.Width / 2, rect.Bottom), false, false);
                            ctx.LineTo(new Point(rect.X, rect.Y + rect.Height / 2), false, false);
                        }

                        dc.DrawGeometry(fill, null, g);
                        break;
                    case NodeShape.Ink:
                        dc.DrawRectangle(null, new Pen(fill, 1), rect);
                        break;
                    case NodeShape.Text:
                        dc.DrawRectangle(muted, null, new Rect(rect.X, rect.Y + rect.Height / 2 - 1, rect.Width, 2));
                        break;
                    case NodeShape.Image or NodeShape.Card or NodeShape.Link:
                        dc.DrawRoundedRectangle(muted, null, rect, 1.5, 1.5);
                        break;
                    default:
                        dc.DrawRoundedRectangle(fill, null, rect, 1.5, 1.5);
                        break;
                }
            }

            foreach (var e in _vm.Doc.Edges)
            {
                if (string.IsNullOrEmpty(e.PathData)) continue;
                var color = ThemeService.TryParseColor(e.Color, out var c) ? c : Colors.Gray;
                dc.DrawLine(new Pen(new SolidColorBrush(color), 1), MiniPoint(e.StartX, e.StartY), MiniPoint(e.EndX, e.EndY));
            }
        }

        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private void MiniCenterOn(Point p)
    {
        if (_vm == null || _miniScale <= 0) return;
        var wx = _miniBounds.X + (p.X - _miniOx) / _miniScale;
        var wy = _miniBounds.Y + (p.Y - _miniOy) / _miniScale;
        _vm.Doc.OffsetX = Surface.ActualWidth / 2 - wx * _vm.Doc.Zoom;
        _vm.Doc.OffsetY = Surface.ActualHeight / 2 - wy * _vm.Doc.Zoom;
    }

    private void OnMiniDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(MiniMap);
        var vr = new Rect(Canvas.GetLeft(MiniViewport), Canvas.GetTop(MiniViewport), MiniViewport.Width, MiniViewport.Height);
        var center = new Point(vr.X + vr.Width / 2, vr.Y + vr.Height / 2);
        // Grabbing the visible-area box drags it from where it was grabbed; clicking elsewhere jumps there.
        _miniGrab = vr.Contains(p) ? p - center : new Vector();
        _miniDragging = true;
        MiniMap.CaptureMouse();
        if (!vr.Contains(p)) MiniCenterOn(p);
        e.Handled = true;
    }

    private void OnMiniMove(object sender, MouseEventArgs e)
    {
        if (!_miniDragging) return;
        MiniCenterOn(e.GetPosition(MiniMap) - _miniGrab);
    }

    private void OnMiniUp(object sender, MouseButtonEventArgs e)
    {
        _miniDragging = false;
        MiniMap.ReleaseMouseCapture();
        InvalidateMinimap();
    }

    private void OnMiniWheel(object sender, MouseWheelEventArgs e)
    {
        // Zoom around the point under the pointer on the map.
        if (_vm == null) return;
        MiniCenterOn(e.GetPosition(MiniMap));
        _vm.ZoomAt(e.Delta > 0 ? 1.15 : 1 / 1.15, Surface.ActualWidth / 2, Surface.ActualHeight / 2);
        e.Handled = true;
    }

    private void OnMiniHide(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.Main.Settings.ShowMinimap = false;
    }

    private void OnMiniShow(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.Main.Settings.ShowMinimap = true;
    }

    private void UpdateDotBrush()
    {
        var brush = TryFindResource("Fb.FaintTextBrush") as Brush ?? Brushes.Gray;
        var dot = new GeometryDrawing(brush, null, new EllipseGeometry(new Point(1, 1), 1, 1));
        var group = new DrawingGroup { Opacity = 0.55 };
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
        group.Children.Add(dot);
        _dots.Drawing = group;
        ApplyTransform();
    }

    // ================= transform =================

    private void OnDocChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CanvasDoc.Zoom) or nameof(CanvasDoc.OffsetX) or nameof(CanvasDoc.OffsetY))
        {
            ApplyTransform();
            InvalidateMinimap();
        }
    }

    private void ApplyTransform()
    {
        if (_vm == null) return;
        var d = _vm.Doc;
        WorldHost.RenderTransform = new MatrixTransform(d.Zoom, 0, 0, d.Zoom, d.OffsetX, d.OffsetY);
        var step = CanvasViewModel.Grid * d.Zoom;
        while (step < 10) step *= 5; // keep the dot grid readable when zoomed far out
        _dots.Viewport = new Rect(Mod(d.OffsetX, step), Mod(d.OffsetY, step), step, step);
        _dots.Viewbox = new Rect(0, 0, 20, 20);
        _dots.ViewboxUnits = BrushMappingMode.Absolute;
        _dots.Stretch = Stretch.Fill;
    }

    private static double Mod(double v, double m) => ((v % m) + m) % m;

    private static Brush BrushOf(string hex) =>
        ThemeService.TryParseColor(hex, out var c) ? new SolidColorBrush(c) : Brushes.Orange;

    private Point ToWorld(Point screen)
    {
        var d = _vm!.Doc;
        return new Point((screen.X - d.OffsetX) / d.Zoom, (screen.Y - d.OffsetY) / d.Zoom);
    }

    private void OnSurfaceSize(object sender, SizeChangedEventArgs e)
    {
        if (_vm == null) return;
        var first = _vm.ViewportWidth == 1000 && _vm.ViewportHeight == 700;
        _vm.ViewportWidth = Surface.ActualWidth;
        _vm.ViewportHeight = Surface.ActualHeight;
        InvalidateMinimap();
        // A brand-new canvas starts centered on its content.
        if (first && _vm.Doc.Nodes.Count > 0 && _vm.Doc.Zoom == 1 && _vm.Doc.OffsetX == 80 && _vm.Doc.OffsetY == 60) _vm.ZoomToFit();
    }

    // ================= hit testing =================

    /// <summary>Finds what's under the pointer: "node", "port", "resize" or "edge" with its data item.</summary>
    private (string? Kind, object? Item) HitAt(DependencyObject? source)
    {
        for (var d = source; d != null && d != Surface; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is TextBox) return ("text", (d as FrameworkElement)?.DataContext);
            if (d is FrameworkElement { Tag: string tag } fe && tag is "node" or "port" or "resize" or "edge" or "end-from" or "end-to" or "bend") return (tag, fe.DataContext);
        }

        return (null, null);
    }

    private CanvasNode? NodeAtScreen(Point p)
    {
        CanvasNode? found = null;
        VisualTreeHelper.HitTest(Surface, null, r =>
        {
            // A section's empty inside isn't a drop target for lines and connectors (only its title is).
            if (HitAt(r.VisualHit).Item is CanvasNode n && n != _target && !(n.Shape == NodeShape.Frame && ToWorld(p).Y >= n.Y))
            {
                found = n;
                return HitTestResultBehavior.Stop;
            }

            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(p));
        return found;
    }

    // ================= mouse =================

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null) return;
        var (kind, item) = HitAt(e.OriginalSource as DependencyObject);
        if (kind == "text") return; // let the text box handle it
        Focus();
        var p = e.GetPosition(Surface);
        _start = _last = p;
        _moved = false;

        if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && _spaceDown))
        {
            BeginDrag(Drag.Pan);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            // Right-click selects what's under the pointer so the context menu acts on it.
            if (item is CanvasNode rn && !rn.IsSelected) _vm.SelectOnly(rn);
            var cp = ToWorld(p);
            _vm.ContextPoint = (cp.X, cp.Y);
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;
        var world = ToWorld(p);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        // For drawing and shape tools, a section's empty inside counts as empty canvas (shapes land in the section).
        if (kind == "node" && item is CanvasNode { Shape: NodeShape.Frame } body && world.Y >= body.Y
            && _vm.Tool is not (CanvasTool.Select or CanvasTool.Connector))
        {
            kind = null;
            item = null;
        }

        // Drawing tools work anywhere (also on top of shapes).
        switch (_vm.Tool)
        {
            case CanvasTool.Pen:
                _penPoints.Clear();
                _penPoints.Add((world.X, world.Y));
                PenPreview.Points.Clear();
                PenPreview.Points.Add(world);
                PenPreview.Stroke = BrushOf(_vm.PenColor);
                PenPreview.StrokeThickness = _vm.PenSize;
                PenPreview.Visibility = Visibility.Visible;
                BeginDrag(Drag.Pen);
                e.Handled = true;
                return;
            case CanvasTool.Line or CanvasTool.Arrow when kind is null or "node":
                _lineStartNode = item as CanvasNode;
                _lineStartWorld = world;
                BeginDrag(Drag.DrawLine);
                e.Handled = true;
                return;
            case CanvasTool.Frame when kind == null:
                _lineStartWorld = world;
                BeginDrag(Drag.DrawFrame);
                e.Handled = true;
                return;
        }

        // Shape tools: click on empty space places a shape.
        if (kind == null && ToolShape(_vm.Tool) is { } shape)
        {
            _vm.AddNodeAt(shape, world.X, world.Y);
            _vm.Tool = CanvasTool.Select;
            e.Handled = true;
            return;
        }

        // Dragging from a port (or from a shape with the connector tool) draws a connector.
        if (item is CanvasNode source && (kind == "port" || (kind == "node" && _vm.Tool == CanvasTool.Connector)))
        {
            _target = source;
            BeginDrag(Drag.Connect);
            e.Handled = true;
            return;
        }

        switch (kind)
        {
            case "bend" when item is CanvasEdge bendEdge:
                if (e.ClickCount == 2)
                {
                    // Double-click the handle: straight again.
                    _vm.SelectEdge(bendEdge, false);
                    _vm.StraightenCommand.Execute(null);
                    break;
                }

                _edge = bendEdge;
                BeginDrag(Drag.Bend);
                break;

            case "end-from" or "end-to" when item is CanvasEdge endEdge:
                _edge = endEdge;
                _edgeFrom = kind == "end-from";
                BeginDrag(Drag.EdgeEnd);
                break;

            case "resize" when item is CanvasNode { Locked: false } rn2:
                _target = rn2;
                _startSize = new Size(rn2.Width, rn2.Height);
                BeginDrag(Drag.Resize);
                break;

            case "node" when item is CanvasNode node:
                var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                var inFrameBody = node.Shape == NodeShape.Frame && world.Y >= node.Y;
                if (e.ClickCount == 2)
                {
                    // Double-click inside a section adds a shape there; on its title (or any other shape) edits it.
                    if (inFrameBody) _vm.AddNodeAt(NodeShape.Rounded, world.X, world.Y);
                    else _vm.OpenNode(node);
                    break;
                }

                if (inFrameBody && ctrl)
                {
                    // Ctrl + drag inside a section: select shapes in it with a box.
                    if (!shift) _vm.ClearSelection();
                    BeginDrag(Drag.Marquee);
                    break;
                }

                foreach (var n in _vm.Doc.Nodes) if (n != node) n.IsEditing = false;

                // A shape inside a section is carried by the section: dragging it moves the whole section.
                // Ctrl + drag moves just that shape. A plain click (no drag) still selects the shape itself.
                var grab = ctrl ? node : _vm.OuterFrame(node);
                _clickedMember = grab != node ? node : null;
                if (_clickedMember == null)
                {
                    if (shift) node.IsSelected = !node.IsSelected;
                    else if (!node.IsSelected) _vm.SelectOnly(node);
                }

                if (grab.Locked)
                {
                    // Locked shapes (and shapes in a locked section) can be selected but not moved.
                    if (_clickedMember != null) _vm.SelectOnly(_clickedMember);
                    _clickedMember = null;
                    break;
                }
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && _clickedMember == null)
                {
                    // Alt + drag: drag out a copy (the originals stay put).
                    _vm.Copy();
                    _vm.Paste(offset: false);
                    grab = _vm.SelectedNodes.FirstOrDefault(n => n.X == grab.X && n.Y == grab.Y && n.Text == grab.Text) ?? grab;
                }

                _target = grab;
                var moving = grab.IsSelected ? _vm.SelectedNodes.Where(n => !n.Locked).ToList() : [grab];
                foreach (var f in moving.Where(n => n.Shape == NodeShape.Frame).ToList())
                    moving.AddRange(_vm.Members(f).Where(c => !moving.Contains(c)));
                _startPositions = moving.Distinct().ToDictionary(n => n, n => new Point(n.X, n.Y));
                _movingEdges = moving.Where(n => n.Shape == NodeShape.Frame).SelectMany(f => _vm.FreeEdgesWithin(f.Bounds)).Distinct().ToList();
                _edgeStarts = _movingEdges.ToDictionary(x => x, x => (x.FromX, x.FromY, x.ToX, x.ToY));
                BeginDrag(Drag.Move);
                break;

            case "edge" when item is CanvasEdge edge:
                if (e.ClickCount == 2)
                {
                    _ = _vm.EditEdgeLabel(edge);
                    break;
                }

                if (!edge.IsSelected || shift) _vm.SelectEdge(edge, shift);
                // Free lines can be dragged around.
                _movingEdges = _vm.SelectedEdges.Where(x => x.IsFreeFrom || x.IsFreeTo).ToList();
                if (_movingEdges.Count > 0) BeginDrag(Drag.MoveEdge);
                break;

            default:
                if (e.ClickCount == 2)
                {
                    _vm.AddNodeAt(NodeShape.Rounded, world.X, world.Y);
                    break;
                }

                if (!shift) _vm.ClearSelection();
                BeginDrag(Drag.Marquee);
                break;
        }

        e.Handled = true;
    }

    private static NodeShape? ToolShape(CanvasTool tool) => tool switch
    {
        CanvasTool.Rectangle => NodeShape.Rectangle,
        CanvasTool.Rounded => NodeShape.Rounded,
        CanvasTool.Ellipse => NodeShape.Ellipse,
        CanvasTool.Circle => NodeShape.Circle,
        CanvasTool.Diamond => NodeShape.Diamond,
        CanvasTool.Sticky => NodeShape.Sticky,
        CanvasTool.Text => NodeShape.Text,
        _ => null,
    };

    private void BeginDrag(Drag kind)
    {
        _drag = kind;
        Surface.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_vm == null || _drag == Drag.None) return;
        var p = e.GetPosition(Surface);
        var total = p - _start;
        if (!_moved && Math.Abs(total.X) + Math.Abs(total.Y) < 3) return;
        var firstMove = !_moved;
        _moved = true;
        var z = _vm.Doc.Zoom;

        switch (_drag)
        {
            case Drag.Pan:
                _vm.Doc.OffsetX += p.X - _last.X;
                _vm.Doc.OffsetY += p.Y - _last.Y;
                Cursor = Cursors.SizeAll;
                break;

            case Drag.Move when _target != null && _startPositions.TryGetValue(_target, out var anchor):
                if (firstMove)
                {
                    _vm.Checkpoint();
                    if (_clickedMember != null && !_target.IsSelected) _vm.SelectOnly(_target);
                }

                // Snap the grabbed node; everything else keeps its offset to it.
                var nx = _vm.SnapValue(anchor.X + total.X / z);
                var ny = _vm.SnapValue(anchor.Y + total.Y / z);
                var dx = nx - anchor.X;
                var dy = ny - anchor.Y;
                foreach (var (n, s) in _startPositions)
                {
                    n.X = s.X + dx;
                    n.Y = s.Y + dy;
                }

                foreach (var (ed, st) in _edgeStarts)
                {
                    if (ed.IsFreeFrom) { ed.FromX = st.FromX + dx; ed.FromY = st.FromY + dy; }
                    if (ed.IsFreeTo) { ed.ToX = st.ToX + dx; ed.ToY = st.ToY + dy; }
                    _vm.UpdateEdge(ed);
                }

                break;

            case Drag.Pen:
                var pw = ToWorld(p);
                var lastPt = _penPoints[^1];
                if (Math.Abs(pw.X - lastPt.X) + Math.Abs(pw.Y - lastPt.Y) >= 1.5 / z)
                {
                    _penPoints.Add((pw.X, pw.Y));
                    PenPreview.Points.Add(pw);
                }

                break;

            case Drag.DrawLine:
                var lw = ToWorld(p);
                var endNode = NodeAtScreen(p);
                Box fromBox = _lineStartNode?.Bounds ?? new Box(_lineStartWorld.X, _lineStartWorld.Y, 0, 0);
                Box toBox2 = endNode != null && endNode != _lineStartNode ? endNode.Bounds : new Box(lw.X, lw.Y, 0, 0);
                var ls = CanvasGeometry.Route(fromBox, toBox2, EdgeStyle.Straight, _vm.Tool == CanvasTool.Arrow);
                ConnectPreview.Data = Geometry.Parse((ls.Path + " " + ls.Arrow).Trim());
                ConnectPreview.Visibility = Visibility.Visible;
                break;

            case Drag.DrawFrame:
                var fr = new Rect(_start, p);
                Canvas.SetLeft(Marquee, fr.X);
                Canvas.SetTop(Marquee, fr.Y);
                Marquee.Width = fr.Width;
                Marquee.Height = fr.Height;
                Marquee.Visibility = Visibility.Visible;
                break;

            case Drag.MoveEdge:
                if (firstMove) _vm.Checkpoint();
                _vm.MoveFreeEdges(_movingEdges, (p.X - _last.X) / z, (p.Y - _last.Y) / z);
                break;

            case Drag.Bend when _edge != null:
                if (firstMove) _vm.Checkpoint();
                var bw = ToWorld(p);
                _vm.SetBend(_edge, bw.X, bw.Y);
                break;

            case Drag.EdgeEnd when _edge != null:
                if (firstMove) _vm.Checkpoint();
                var ew = ToWorld(p);
                _vm.SetEdgeEnd(_edge, _edgeFrom, ew.X, ew.Y, null);
                break;

            case Drag.Resize when _target != null:
                if (firstMove) _vm.Checkpoint();
                var w = Math.Max(40, _vm.SnapValue(_startSize.Width + total.X / z));
                var h = Math.Max(30, _vm.SnapValue(_startSize.Height + total.Y / z));
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _startSize.Width > 0)
                    h = Math.Max(30, w * _startSize.Height / _startSize.Width);
                if (_target.Shape == NodeShape.Circle) w = h = Math.Max(w, h);
                _target.Width = w;
                _target.Height = h;
                break;

            case Drag.Connect when _target != null:
                var world = ToWorld(p);
                var from = _target.Bounds;
                var over = NodeAtScreen(p);
                var toBox = over?.Bounds ?? new Box(world.X - 1, world.Y - 1, 2, 2);
                var shape = CanvasGeometry.Route(from, toBox, EdgeStyle.Curved, arrow: false);
                ConnectPreview.Data = Geometry.Parse(shape.Path);
                ConnectPreview.Visibility = Visibility.Visible;
                break;

            case Drag.Marquee:
                var r = new Rect(_start, p);
                Canvas.SetLeft(Marquee, r.X);
                Canvas.SetTop(Marquee, r.Y);
                Marquee.Width = r.Width;
                Marquee.Height = r.Height;
                Marquee.Visibility = Visibility.Visible;
                var a = ToWorld(r.TopLeft);
                var b = ToWorld(r.BottomRight);
                _vm.SelectInRect(new Box(a.X, a.Y, b.X - a.X, b.Y - a.Y), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                break;
        }

        _last = p;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null || _drag == Drag.None) return;
        var p = e.GetPosition(Surface);
        if (_drag == Drag.Connect && _target != null)
        {
            ConnectPreview.Visibility = Visibility.Collapsed;
            if (_moved)
            {
                var over = NodeAtScreen(p);
                if (over != null)
                {
                    _vm.Connect(_target, over);
                }
                else
                {
                    var w = ToWorld(p);
                    var created = _vm.ConnectToNew(_target, w.X, w.Y);
                    created.IsEditing = true;
                }
            }
        }

        var upWorld = ToWorld(p);
        switch (_drag)
        {
            case Drag.Pen:
                PenPreview.Visibility = Visibility.Collapsed;
                PenPreview.Points.Clear();
                if (_penPoints.Count > 1) _vm.AddInk(_penPoints);
                break;
            case Drag.DrawLine:
                ConnectPreview.Visibility = Visibility.Collapsed;
                var endNode = NodeAtScreen(p);
                if (endNode == _lineStartNode) endNode = null;
                var len = Math.Abs(upWorld.X - _lineStartWorld.X) + Math.Abs(upWorld.Y - _lineStartWorld.Y);
                if (len * _vm.Doc.Zoom > 6 || endNode != null)
                    _vm.AddFreeLine(_lineStartWorld.X, _lineStartWorld.Y, _lineStartNode, upWorld.X, upWorld.Y, endNode, _vm.Tool == CanvasTool.Arrow);
                break;
            case Drag.DrawFrame:
                if (_moved)
                {
                    var x = Math.Min(_lineStartWorld.X, upWorld.X);
                    var y = Math.Min(_lineStartWorld.Y, upWorld.Y);
                    var w = Math.Abs(upWorld.X - _lineStartWorld.X);
                    var h = Math.Abs(upWorld.Y - _lineStartWorld.Y);
                    var frame = _vm.AddNodeAt(NodeShape.Frame, x + w / 2, y + h / 2);
                    frame.X = _vm.SnapValue(x);
                    frame.Y = _vm.SnapValue(y);
                    frame.Width = Math.Max(120, _vm.SnapValue(w));
                    frame.Height = Math.Max(80, _vm.SnapValue(h));
                    _vm.AdoptInto(frame);
                }
                else
                {
                    _vm.AddNodeAt(NodeShape.Frame, upWorld.X, upWorld.Y);
                }

                _vm.Tool = CanvasTool.Select;
                break;
            case Drag.Move when _moved:
                // Shapes dropped inside a section join it (and stay in it until removed from the menu).
                _vm.JoinFrames(_startPositions.Keys.Where(n => n.Shape != NodeShape.Frame || n.FrameId == null));
                break;
            case Drag.Move when _clickedMember != null:
                // A click without dragging selects the shape inside the section.
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _clickedMember.IsSelected = !_clickedMember.IsSelected;
                else _vm.SelectOnly(_clickedMember);
                break;
            case Drag.EdgeEnd when _edge != null && _moved:
                _vm.SetEdgeEnd(_edge, _edgeFrom, upWorld.X, upWorld.Y, NodeAtScreen(p));
                break;
        }

        _edge = null;
        _movingEdges = [];
        _edgeStarts = new();
        Marquee.Visibility = Visibility.Collapsed;
        Cursor = null;
        _drag = Drag.None;
        _target = null;
        _clickedMember = null;
        _startPositions.Clear();
        Surface.ReleaseMouseCapture();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm == null) return;
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control))
        {
            var p = e.GetPosition(Surface);
            _vm.ZoomAt(e.Delta > 0 ? 1.12 : 1 / 1.12, p.X, p.Y);
        }
        else if (mods.HasFlag(ModifierKeys.Shift))
        {
            _vm.Doc.OffsetX += e.Delta * 0.8;
        }
        else
        {
            _vm.Doc.OffsetY += e.Delta * 0.8;
        }

        e.Handled = true;
    }

    // ================= keyboard =================

    private void OnEditorKey(object sender, KeyEventArgs e)
    {
        if (_vm == null || sender is not TextBox { DataContext: CanvasNode node } tb) return;
        switch (e.Key)
        {
            case Key.Enter when !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
            case Key.Escape:
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                node.IsEditing = false;
                Focus();
                e.Handled = true;
                break;
            case Key.Tab:
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                node.IsEditing = false;
                _vm.SelectOnly(node);
                _vm.AddChildCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_vm == null) return;
        if (Keyboard.FocusedElement is TextBox) return;
        var mods = Keyboard.Modifiers;
        var ctrl = mods.HasFlag(ModifierKeys.Control);
        var shift = mods.HasFlag(ModifierKeys.Shift);
        var single = _vm.SelectedNodes.Take(2).ToList() is [var one] ? one : null;
        var handled = true;

        switch (e.Key)
        {
            case Key.Space when !ctrl:
                _spaceDown = true;
                Cursor = Cursors.Hand;
                break;
            case Key.Delete:
            case Key.Back:
                _vm.DeleteSelectionCommand.Execute(null);
                break;
            case Key.A when ctrl:
                _vm.SelectAllCommand.Execute(null);
                break;
            case Key.C when ctrl:
                _vm.Copy();
                break;
            case Key.X when ctrl:
                _vm.Copy();
                _vm.DeleteSelectionCommand.Execute(null);
                break;
            case Key.V when ctrl:
                var at = Mouse.GetPosition(Surface);
                var inside = at.X >= 0 && at.Y >= 0 && at.X <= Surface.ActualWidth && at.Y <= Surface.ActualHeight;
                var w = ToWorld(at);
                if (!_vm.PasteImageFromClipboard(inside ? (w.X, w.Y) : null)) _vm.Paste();
                break;
            case Key.D when ctrl:
                _vm.DuplicateSelectionCommand.Execute(null);
                break;
            case Key.D0 when ctrl:
            case Key.NumPad0 when ctrl:
                _vm.ZoomToFit();
                break;
            case Key.OemPlus when ctrl:
            case Key.Add when ctrl:
                _vm.ZoomInCommand.Execute(null);
                break;
            case Key.OemMinus when ctrl:
            case Key.Subtract when ctrl:
                _vm.ZoomOutCommand.Execute(null);
                break;
            case Key.Tab when !ctrl:
                _vm.AddChildCommand.Execute(null);
                break;
            case Key.Enter when single != null:
                _vm.AddSiblingCommand.Execute(null);
                break;
            case Key.F2 when single != null:
                _vm.OpenNode(single);
                break;
            case Key.Escape when _vm.Tool != CanvasTool.Select || _vm.HasSelection:
                _vm.Tool = CanvasTool.Select;
                _vm.ClearSelection();
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down when _vm.HasNodeSelection:
                var step = shift ? CanvasViewModel.Grid : 1;
                if (!e.IsRepeat) _vm.Checkpoint();
                _vm.MoveSelection(e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0);
                break;
            case Key.V when mods == ModifierKeys.None:
                _vm.Tool = CanvasTool.Select;
                break;
            case Key.R when mods == ModifierKeys.None:
                _vm.Tool = CanvasTool.Rounded;
                break;
            case Key.B when mods == ModifierKeys.None:
                _vm.Tool = CanvasTool.Rectangle;
                break;
            case Key.O when mods == ModifierKeys.None:
                _vm.Tool = CanvasTool.Ellipse;
                break;
            case Key.O when mods == ModifierKeys.Shift:
                _vm.Tool = CanvasTool.Circle;
                break;
            case Key.K when mods == ModifierKeys.None:
                _vm.AddLinkCommand.Execute(null);
                break;
            case Key.D when mods == ModifierKeys.None:
                _vm.Tool = CanvasTool.Diamond;
                break;
            case Key.S when mods == ModifierKeys.None:
                _vm.Tool = CanvasTool.Sticky;
                break;
            case Key.T when mods == ModifierKeys.None:
                _vm.Tool = CanvasTool.Text;
                break;
            case Key.C when mods == ModifierKeys.None:
                _vm.Tool = CanvasTool.Connector;
                break;
            case Key.L when mods == ModifierKeys.None:
                _vm.Tool = CanvasTool.Line;
                break;
            case Key.A when mods == ModifierKeys.None:
                _vm.Tool = CanvasTool.Arrow;
                break;
            case Key.P when mods == ModifierKeys.None:
                _vm.Tool = CanvasTool.Pen;
                break;
            case Key.F when mods == ModifierKeys.None:
                _vm.Tool = CanvasTool.Frame;
                break;
            case Key.I when mods == ModifierKeys.None:
                _vm.AddImageCommand.Execute(null);
                break;
            default:
                handled = false;
                break;
        }

        if (handled) e.Handled = true;
    }

    // ================= drop / export =================

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_vm == null || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var w = ToWorld(e.GetPosition(Surface));
        var offset = 0.0;
        foreach (var f in files.Where(MediaStore.IsImageFile))
        {
            try
            {
                _vm.AddImageNode(MediaStore.ImportFile(f, _vm.Doc.Id), (w.X + offset, w.Y + offset));
                offset += 30;
            }
            catch (Exception ex)
            {
                _vm.Main.ShowToast($"Couldn't add {System.IO.Path.GetFileName(f)}: {ex.Message}", isError: true);
            }
        }

        e.Handled = true;
    }

    private void OnRenderRequested(object? sender, string what)
    {
        if (_vm == null) return;
        if (_vm.IsEmpty)
        {
            _vm.Main.ShowToast("The canvas is empty.", isError: true);
            return;
        }

        var selectedNodes = _vm.SelectedNodes.ToList();
        var selectedEdges = _vm.SelectedEdges.ToList();
        _vm.ClearSelection();
        World.UpdateLayout();
        try
        {
            var b = _vm.ContentBounds(40);
            var bg = (Brush)FindResource("Fb.WindowBrush");
            var image = MediaStore.Render(World, 2, bg, new Rect(b.X, b.Y, b.W, b.H));
            if (what == "print") MediaStore.Print(image, _vm.Doc.Name);
            else if (MediaStore.ExportPng(image, _vm.Doc.Name)) _vm.Main.ShowToast("Canvas exported");
        }
        catch (Exception ex)
        {
            _vm.Main.ShowToast($"Export failed: {ex.Message}", isError: true);
        }
        finally
        {
            foreach (var n in selectedNodes) n.IsSelected = true;
            foreach (var ed in selectedEdges) ed.IsSelected = true;
            _vm.RaiseSelection();
        }
    }

    // ----- high-res screenshot -----
    FrameworkElement Helpers.ICapturable.CaptureElement => World;
    Rect? Helpers.ICapturable.CaptureArea => _vm is { IsEmpty: false } v ? v.ContentBounds(40) is var b ? new Rect(b.X, b.Y, b.W, b.H) : null : null;
    System.Windows.Media.Brush? Helpers.ICapturable.CaptureBackground => Helpers.CaptureHelpers.DocWallpaper();
    string Helpers.ICapturable.CaptureName => _vm?.Doc.Name ?? "Canvas";
}
