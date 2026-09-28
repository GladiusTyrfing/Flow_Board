using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;

namespace FlowBoard.ViewModels;

public enum CanvasTool
{
    Select,
    Rectangle,
    Rounded,
    Ellipse,
    Circle,
    Diamond,
    Sticky,
    Text,
    Connector,
    Line,
    Arrow,
    Pen,
    Frame,
}

/// <summary>Infinite canvas for flowcharts, mind maps and mood boards.</summary>
public sealed partial class CanvasViewModel : DocumentViewModel
{
    public const double Grid = 20;
    private static List<CanvasNode>? _clipboard;
    private static List<CanvasEdge>? _clipboardEdges;

    public CanvasViewModel(MainViewModel main, CanvasDoc doc) : base(main)
    {
        Doc = doc;
        Hook();
    }

    public CanvasDoc Doc { get; }
    public override object Model => Doc;
    public override ActiveView Kind => ActiveView.Canvas;

    public static readonly string[] FillPalette =
        ["#8B5CF6", "#6366F1", "#3B82F6", "#06B6D4", "#10B981", "#84CC16", "#FACC15", "#F97316", "#EF4444", "#EC4899", "#64748B", "#1F2937", "#FFFFFF"];

    public static readonly string[] EdgePalette = ["#98A2B3", "#8B5CF6", "#3B82F6", "#10B981", "#F97316", "#EF4444", "#EC4899"];

    public string[] Fills => FillPalette;
    public string[] EdgeColors => EdgePalette;

    [ObservableProperty] private CanvasTool _tool = CanvasTool.Select;
    [ObservableProperty] private bool _isEditingName;

    /// <summary>Size of the visible area in screen pixels (set by the view, used for zoom-to-fit and centering).</summary>
    public double ViewportWidth { get; set; } = 1000;
    public double ViewportHeight { get; set; } = 700;

    public IEnumerable<CanvasNode> SelectedNodes => Doc.Nodes.Where(n => n.IsSelected);
    public IEnumerable<CanvasEdge> SelectedEdges => Doc.Edges.Where(e => e.IsSelected);
    public bool HasNodeSelection => Doc.Nodes.Any(n => n.IsSelected);
    public bool HasEdgeSelection => Doc.Edges.Any(e => e.IsSelected);
    public bool HasSelection => HasNodeSelection || HasEdgeSelection;
    public string ZoomText => $"{Math.Round(Doc.Zoom * 100)}%";
    public bool IsEmpty => Doc.Nodes.Count == 0 && Doc.Edges.Count == 0;

    /// <summary>Raised when anything the minimap draws changes (shapes, sizes, colors, lines).</summary>
    public event EventHandler? ContentChanged;

    private void RaiseContentChanged() => ContentChanged?.Invoke(this, EventArgs.Empty);

    // ================= wiring =================

    private void Hook()
    {
        foreach (var n in Doc.Nodes) HookNode(n);
        Doc.Nodes.CollectionChanged += OnNodesChanged;
        Doc.Edges.CollectionChanged += OnEdgesChanged;
        Doc.PropertyChanged += OnDocChanged;
        ResolveCards();
        UpdateAllEdges();
    }

    private void Unhook()
    {
        foreach (var n in Doc.Nodes) n.PropertyChanged -= OnNodeChanged;
        Doc.Nodes.CollectionChanged -= OnNodesChanged;
        Doc.Edges.CollectionChanged -= OnEdgesChanged;
        Doc.PropertyChanged -= OnDocChanged;
    }

    private void HookNode(CanvasNode n)
    {
        n.PropertyChanged -= OnNodeChanged;
        n.PropertyChanged += OnNodeChanged;
    }

    private void OnDocChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CanvasDoc.Zoom)) OnPropertyChanged(nameof(ZoomText));
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (CanvasNode n in e.NewItems) HookNode(n);
        if (e.OldItems != null) foreach (CanvasNode n in e.OldItems) n.PropertyChanged -= OnNodeChanged;
        OnPropertyChanged(nameof(IsEmpty));
        RaiseSelection();
        RaiseContentChanged();
    }

    private void OnEdgesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (CanvasEdge edge in e.NewItems) UpdateEdge(edge);
        OnPropertyChanged(nameof(IsEmpty));
        RaiseSelection();
        RaiseContentChanged();
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CanvasNode n) return;
        switch (e.PropertyName)
        {
            case nameof(CanvasNode.X):
            case nameof(CanvasNode.Y):
            case nameof(CanvasNode.Width):
            case nameof(CanvasNode.Height):
                foreach (var edge in Doc.Edges)
                    if (edge.FromId == n.Id || edge.ToId == n.Id) UpdateEdge(edge);
                RaiseContentChanged();
                break;
            case nameof(CanvasNode.Fill):
            case nameof(CanvasNode.Shape):
                RaiseContentChanged();
                break;
            case nameof(CanvasNode.IsSelected):
                RaiseSelection();
                break;
        }
    }

    public void RaiseSelection()
    {
        OnPropertyChanged(nameof(HasNodeSelection));
        OnPropertyChanged(nameof(HasEdgeSelection));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void ResolveCards()
    {
        foreach (var n in Doc.Nodes.Where(n => n.CardId != null))
            n.Card = Main.Workspace.FindCard(n.CardId!.Value, out _, out _);
        foreach (var n in Doc.Nodes.Where(n => n.Shape == NodeShape.Link && n.LinkId != null))
            n.Link = LinkResolver.Describe(Main.Workspace, n.LinkKind, n.LinkId!.Value, n.Text);
    }

    public CanvasNode? FindNode(Guid id) => Doc.Nodes.FirstOrDefault(n => n.Id == id);

    public void UpdateEdge(CanvasEdge edge)
    {
        Box? a = edge.IsFreeFrom ? new Box(edge.FromX, edge.FromY, 0, 0) : FindNode(edge.FromId)?.Bounds;
        Box? b = edge.IsFreeTo ? new Box(edge.ToX, edge.ToY, 0, 0) : FindNode(edge.ToId)?.Bounds;
        if (a == null || b == null)
        {
            edge.PathData = string.Empty;
            edge.ArrowData = string.Empty;
            return;
        }

        var shape = CanvasGeometry.Route(a.Value, b.Value, edge.Style, edge.Arrow, edge.StartArrow, edge.Thickness, edge.BendX, edge.BendY);
        edge.PathData = shape.Path;
        edge.ArrowData = shape.Arrow;
        edge.LabelX = shape.LabelX;
        edge.LabelY = shape.LabelY;
        edge.StartX = shape.X1;
        edge.StartY = shape.Y1;
        edge.EndX = shape.X2;
        edge.EndY = shape.Y2;
        RaiseContentChanged();
    }

    public void UpdateAllEdges()
    {
        foreach (var e in Doc.Edges) UpdateEdge(e);
    }

    public override void OnDeactivated() => Unhook();

    protected override void Restore(string json)
    {
        var snap = JsonSerializer.Deserialize<CanvasDoc>(json, Json.Options);
        if (snap == null) return;
        Doc.Nodes.Clear();
        Doc.Edges.Clear();
        foreach (var n in snap.Nodes) Doc.Nodes.Add(n);
        foreach (var e in snap.Edges) Doc.Edges.Add(e);
        Doc.Name = snap.Name;
        ResolveCards();
        UpdateAllEdges();
    }

    // Selection/zoom are not part of undo snapshots: serialize without view state noise.
    protected override string Serialize() => JsonSerializer.Serialize(Doc, Json.Compact);

    // ================= name =================

    [RelayCommand] private void StartRename() => IsEditingName = true;

    public override void BeginRename() => IsEditingName = true;

    [RelayCommand]
    private void CommitName()
    {
        IsEditingName = false;
        if (string.IsNullOrWhiteSpace(Doc.Name)) Doc.Name = "Untitled canvas";
        Main.RefreshDocSidebar();
    }

    // ================= tools & creation =================

    [RelayCommand] private void SetTool(CanvasTool tool) => Tool = tool;

    public double SnapValue(double v) => Doc.SnapToGrid ? CanvasGeometry.Snap(v, Grid) : v;

    public static (double W, double H) DefaultSize(NodeShape shape) => shape switch
    {
        NodeShape.Ellipse => (150, 80),
        NodeShape.Diamond => (150, 100),
        NodeShape.Sticky => (180, 160),
        NodeShape.Text => (200, 40),
        NodeShape.Image => (260, 180),
        NodeShape.Card => (240, 96),
        NodeShape.Circle => (110, 110),
        NodeShape.Link => (280, 100),
        NodeShape.Frame => (480, 320),
        _ => (170, 70),
    };

    public static string DefaultFill(NodeShape shape) => shape switch
    {
        NodeShape.Sticky => "#FACC15",
        NodeShape.Diamond => "#F97316",
        NodeShape.Ellipse => "#10B981",
        NodeShape.Circle => "#06B6D4",
        NodeShape.Text => "#00000000",
        NodeShape.Frame => "#64748B",
        _ => "#8B5CF6",
    };

    /// <summary>Creates a node centered on a world point and selects it.</summary>
    public CanvasNode AddNodeAt(NodeShape shape, double cx, double cy, bool edit = true, string text = "")
    {
        Checkpoint();
        var (w, h) = DefaultSize(shape);
        var node = new CanvasNode
        {
            Shape = shape, Width = w, Height = h, Text = text,
            X = SnapValue(cx - w / 2), Y = SnapValue(cy - h / 2),
            Fill = LastFill(shape),
            Z = NextZ(),
        };
        Doc.Nodes.Add(node);
        SelectOnly(node);
        if (shape == NodeShape.Frame)
        {
            node.Text = string.IsNullOrEmpty(text) ? "Section" : text;
            node.Z = Doc.Nodes.Count == 1 ? 0 : Doc.Nodes.Min(n => n.Z) - 1; // frames sit behind everything
        }

        else if (FrameAt(node.CenterX, node.CenterY, node) is { } frame)
        {
            node.FrameId = frame.Id; // shapes added inside a section belong to it
        }

        if (edit && shape is not (NodeShape.Image or NodeShape.Card or NodeShape.Ink or NodeShape.Link)) node.IsEditing = true;
        return node;
    }

    // ================= sections (frames) =================

    /// <summary>Topmost section under a point (ignoring <paramref name="except"/>).</summary>
    public CanvasNode? FrameAt(double x, double y, CanvasNode? except = null) =>
        Doc.Nodes.Where(n => n.Shape == NodeShape.Frame && n != except && n.Bounds.Contains(x, y)).OrderByDescending(n => n.Z).FirstOrDefault();

    /// <summary>Shapes that belong to a section (including shapes in sections inside it).</summary>
    public IEnumerable<CanvasNode> Members(CanvasNode frame)
    {
        var result = new List<CanvasNode>();
        var queue = new Queue<Guid>([frame.Id]);
        var seen = new HashSet<Guid> { frame.Id };
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var n in Doc.Nodes.Where(n => n.FrameId == id && seen.Add(n.Id)))
            {
                result.Add(n);
                if (n.Shape == NodeShape.Frame) queue.Enqueue(n.Id);
            }
        }

        return result;
    }

    /// <summary>After a move: shapes dropped inside a section join it. Shapes already in a section stay there until removed.</summary>
    public void JoinFrames(IEnumerable<CanvasNode> moved)
    {
        foreach (var n in moved)
        {
            if (n.FrameId != null && FindNode(n.FrameId.Value) != null) continue;
            var frame = FrameAt(n.CenterX, n.CenterY, n);
            if (frame == null || frame.FrameId == n.Id) continue; // no cycles
            n.FrameId = frame.Id;
        }
    }

    /// <summary>A section drawn around shapes takes them in.</summary>
    public void AdoptInto(CanvasNode frame)
    {
        var b = frame.Bounds;
        foreach (var n in Doc.Nodes.Where(n => n != frame && n.FrameId == null && b.Contains(n.CenterX, n.CenterY) && n.Width <= b.W))
            n.FrameId = frame.Id;
    }

    [RelayCommand]
    private void RemoveFromFrame(CanvasNode? node)
    {
        var nodes = node != null && !node.IsSelected ? [node] : SelectedNodes.Where(n => n.FrameId != null).ToList();
        if (nodes.Count == 0) return;
        Checkpoint();
        foreach (var n in nodes) n.FrameId = null;
        Main.ShowToast(nodes.Count == 1 ? "Removed from the section" : $"Removed {nodes.Count} shapes from their section");
    }

    [RelayCommand]
    private void ToggleLock(CanvasNode? node)
    {
        var nodes = node != null && !node.IsSelected ? [node] : SelectedNodes.ToList();
        if (nodes.Count == 0) return;
        var locked = !nodes[0].Locked;
        foreach (var n in nodes) n.Locked = locked;
    }

    // ================= links to boards, storyboards, shots, pages… =================

    [RelayCommand]
    private async Task AddLink()
    {
        var pick = await Main.PickLinkAsync("Add a link to the canvas", LinkResolver.All);
        if (pick == null) return;
        var (x, y) = ViewCenter();
        var node = AddNodeAt(NodeShape.Link, x, y, edit: false);
        node.LinkKind = pick.Value.Kind;
        node.LinkId = pick.Value.Id;
        node.Text = pick.Value.Title;
        node.Link = LinkResolver.Describe(Main.Workspace, node.LinkKind, pick.Value.Id, node.Text);
    }

    // ================= bending lines =================

    /// <summary>Pulls the middle of a line to a point (the line curves through it).</summary>
    public void SetBend(CanvasEdge e, double x, double y)
    {
        e.BendX = Math.Abs(x - (e.StartX + e.EndX) / 2) < 0.5 ? 0.01 : x - (e.StartX + e.EndX) / 2;
        e.BendY = y - (e.StartY + e.EndY) / 2;
        UpdateEdge(e);
    }

    [RelayCommand]
    private void Straighten()
    {
        var edges = SelectedEdges.Where(e => e.HasBend).ToList();
        if (edges.Count == 0) return;
        Checkpoint();
        foreach (var e in edges)
        {
            e.BendX = 0;
            e.BendY = 0;
            UpdateEdge(e);
        }
    }

    // ================= pen, lines, frames =================

    [ObservableProperty] private string _penColor = "#F97316";
    [ObservableProperty] private double _penSize = 3;

    [RelayCommand] private void SetPenColor(string hex) => PenColor = hex;

    [RelayCommand]
    private void SetPenSize(string size)
    {
        if (double.TryParse(size, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v)) PenSize = v;
    }

    /// <summary>Turns a freehand stroke (world points) into an ink shape that can be moved and resized.</summary>
    public CanvasNode? AddInk(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count < 2) return null;
        var pad = PenSize / 2 + 1;
        double minX = points.Min(p => p.X) - pad, minY = points.Min(p => p.Y) - pad;
        double maxX = points.Max(p => p.X) + pad, maxY = points.Max(p => p.Y) + pad;
        var sb = new System.Text.StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        sb.Append(inv, $"M {points[0].X - minX:0.#},{points[0].Y - minY:0.#}");
        foreach (var p in points.Skip(1)) sb.Append(inv, $" L {p.X - minX:0.#},{p.Y - minY:0.#}");
        Checkpoint();
        var node = new CanvasNode
        {
            Shape = NodeShape.Ink, X = minX, Y = minY, Width = Math.Max(4, maxX - minX), Height = Math.Max(4, maxY - minY),
            PathData = sb.ToString(), Fill = PenColor, StrokeWidth = PenSize, Z = NextZ(),
            InkWidth = Math.Max(4, maxX - minX), InkHeight = Math.Max(4, maxY - minY),
        };
        Doc.Nodes.Add(node);
        return node;
    }

    /// <summary>A line or arrow drawn on its own. Ends dropped on a shape stick to it.</summary>
    public CanvasEdge AddFreeLine(double x1, double y1, CanvasNode? fromNode, double x2, double y2, CanvasNode? toNode, bool arrow)
    {
        Checkpoint();
        var edge = new CanvasEdge
        {
            FromId = fromNode?.Id ?? Guid.Empty, ToId = toNode?.Id ?? Guid.Empty,
            FromX = x1, FromY = y1, ToX = x2, ToY = y2,
            Arrow = arrow, Style = fromNode != null && toNode != null ? EdgeStyle.Curved : EdgeStyle.Straight,
            Color = _lastEdgeColor ?? "#98A2B3", Thickness = 2.5,
        };
        Doc.Edges.Add(edge);
        foreach (var n in Doc.Nodes) n.IsSelected = false;
        foreach (var e in Doc.Edges) e.IsSelected = e == edge;
        RaiseSelection();
        return edge;
    }

    private string? _lastEdgeColor;

    /// <summary>Moves the free ends of the selected lines (lines attached to shapes follow their shapes).</summary>
    public void MoveFreeEdges(IEnumerable<CanvasEdge> edges, double dx, double dy)
    {
        foreach (var e in edges)
        {
            if (e.IsFreeFrom) { e.FromX += dx; e.FromY += dy; }
            if (e.IsFreeTo) { e.ToX += dx; e.ToY += dy; }
            UpdateEdge(e);
        }
    }

    /// <summary>Drags one end of a line; dropping it on a shape attaches it, elsewhere it becomes free.</summary>
    public void SetEdgeEnd(CanvasEdge e, bool from, double x, double y, CanvasNode? attach)
    {
        if (from)
        {
            e.FromId = attach?.Id ?? Guid.Empty;
            e.FromX = x;
            e.FromY = y;
        }
        else
        {
            e.ToId = attach?.Id ?? Guid.Empty;
            e.ToX = x;
            e.ToY = y;
        }

        UpdateEdge(e);
    }


    /// <summary>Edges drawn freely inside a moving selection move along.</summary>
    public IEnumerable<CanvasEdge> FreeEdgesWithin(Box b) =>
        Doc.Edges.Where(e => (e.IsFreeFrom || e.IsFreeTo)
                             && (!e.IsFreeFrom || b.Contains(e.FromX, e.FromY))
                             && (!e.IsFreeTo || b.Contains(e.ToX, e.ToY)));

    // ================= align & distribute =================

    [RelayCommand]
    private void Align(string mode)
    {
        var nodes = SelectedNodes.ToList();
        if (nodes.Count < 2) return;
        Checkpoint();
        double left = nodes.Min(n => n.X), right = nodes.Max(n => n.X + n.Width);
        double top = nodes.Min(n => n.Y), bottom = nodes.Max(n => n.Y + n.Height);
        foreach (var n in nodes)
        {
            switch (mode)
            {
                case "left": n.X = left; break;
                case "center": n.X = (left + right) / 2 - n.Width / 2; break;
                case "right": n.X = right - n.Width; break;
                case "top": n.Y = top; break;
                case "middle": n.Y = (top + bottom) / 2 - n.Height / 2; break;
                case "bottom": n.Y = bottom - n.Height; break;
            }
        }
    }

    [RelayCommand]
    private void Distribute(string axis)
    {
        var nodes = SelectedNodes.ToList();
        if (nodes.Count < 3) return;
        Checkpoint();
        if (axis == "h")
        {
            nodes = nodes.OrderBy(n => n.X).ToList();
            var gap = (nodes[^1].X + nodes[^1].Width - nodes[0].X - nodes.Sum(n => n.Width)) / (nodes.Count - 1);
            var x = nodes[0].X;
            foreach (var n in nodes) { n.X = x; x += n.Width + gap; }
        }
        else
        {
            nodes = nodes.OrderBy(n => n.Y).ToList();
            var gap = (nodes[^1].Y + nodes[^1].Height - nodes[0].Y - nodes.Sum(n => n.Height)) / (nodes.Count - 1);
            var y = nodes[0].Y;
            foreach (var n in nodes) { n.Y = y; y += n.Height + gap; }
        }
    }

    [RelayCommand]
    private void MatchSize()
    {
        var nodes = SelectedNodes.ToList();
        if (nodes.Count < 2) return;
        Checkpoint();
        var first = nodes[0];
        foreach (var n in nodes.Skip(1)) { n.Width = first.Width; n.Height = first.Height; }
    }

    [RelayCommand]
    private void SetThickness(string value)
    {
        var edges = SelectedEdges.ToList();
        if (edges.Count == 0 || !double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var t)) return;
        Checkpoint();
        foreach (var e in edges)
        {
            e.Thickness = t;
            UpdateEdge(e);
        }
    }

    [RelayCommand]
    private void ToggleStartArrow()
    {
        var edges = SelectedEdges.ToList();
        if (edges.Count == 0) return;
        Checkpoint();
        var on = !edges[0].StartArrow;
        foreach (var e in edges)
        {
            e.StartArrow = on;
            UpdateEdge(e);
        }
    }

    // ================= right-click on empty space =================

    /// <summary>World point of the last right-click on empty canvas.</summary>
    public (double X, double Y) ContextPoint { get; set; }

    [RelayCommand]
    private void AddShapeAtContext(NodeShape shape) => AddNodeAt(shape, ContextPoint.X, ContextPoint.Y);

    [RelayCommand]
    private void PasteAtContext()
    {
        if (!PasteImageFromClipboard(ContextPoint)) Paste();
    }

    [RelayCommand]
    private void CropNode(CanvasNode? node)
    {
        node ??= SelectedNodes.FirstOrDefault(n => n.Shape == NodeShape.Image);
        if (node?.ImagePath is not { } path) return;
        Main.ShowDialog(new ImageCropViewModel(path, Doc.Id, 0, null, r =>
        {
            Checkpoint();
            node.ImagePath = r.ImagePath;
            if (Converters.ImageLoader.Load(node.ImageFullPath, 0) is { } bmp && bmp.PixelWidth > 0)
                node.Height = Math.Max(Grid, node.Width * bmp.PixelHeight / bmp.PixelWidth);
        }));
    }

    private string LastFill(NodeShape shape) =>
        shape is NodeShape.Sticky or NodeShape.Text or NodeShape.Frame ? DefaultFill(shape) : (_lastFill ?? DefaultFill(shape));

    private string? _lastFill;

    private int NextZ() => Doc.Nodes.Count == 0 ? 0 : Doc.Nodes.Max(n => n.Z) + 1;

    public (double X, double Y) ViewCenter() =>
        ((ViewportWidth / 2 - Doc.OffsetX) / Doc.Zoom, (ViewportHeight / 2 - Doc.OffsetY) / Doc.Zoom);

    [RelayCommand]
    private void AddShape(NodeShape shape)
    {
        var (x, y) = ViewCenter();
        AddNodeAt(shape, x + Random.Shared.Next(-30, 30), y + Random.Shared.Next(-20, 20));
        Tool = CanvasTool.Select;
    }

    [RelayCommand]
    private void AddImage()
    {
        try
        {
            var rel = MediaStore.PickImage(Doc.Id);
            if (rel != null) AddImageNode(rel, null);
        }
        catch (Exception ex)
        {
            Main.ShowToast($"Couldn't add the image: {ex.Message}", isError: true);
        }
    }

    public void AddImageNode(string relativePath, (double X, double Y)? at)
    {
        var (x, y) = at ?? ViewCenter();
        var node = AddNodeAt(NodeShape.Image, x, y, edit: false);
        node.ImagePath = relativePath;
        if (Converters.ImageLoader.Load(node.ImageFullPath, 0) is { } bmp && bmp.PixelWidth > 0)
        {
            var w = Math.Min(360, bmp.PixelWidth);
            node.Width = SnapValue(w);
            node.Height = Math.Max(Grid, SnapValue(w * bmp.PixelHeight / bmp.PixelWidth));
        }
    }

    [RelayCommand]
    private async Task AddCard()
    {
        var pick = await Main.PickLinkAsync("Add a card to the canvas", LinkTarget.Card);
        if (pick == null) return;
        var (x, y) = ViewCenter();
        var node = AddNodeAt(NodeShape.Card, x, y, edit: false);
        node.CardId = pick.Value.Id;
        node.Card = Main.Workspace.FindCard(pick.Value.Id, out _, out _);
        node.Text = node.Card?.Title ?? string.Empty;
    }

    public bool PasteImageFromClipboard((double X, double Y)? at)
    {
        var rel = MediaStore.FromClipboard(Doc.Id);
        if (rel == null) return false;
        AddImageNode(rel, at);
        return true;
    }

    // ================= selection =================

    public void SelectOnly(CanvasNode? node)
    {
        foreach (var n in Doc.Nodes) n.IsSelected = n == node;
        foreach (var e in Doc.Edges) e.IsSelected = false;
    }

    public void SelectEdge(CanvasEdge edge, bool add)
    {
        if (!add)
        {
            foreach (var n in Doc.Nodes) n.IsSelected = false;
            foreach (var e in Doc.Edges) e.IsSelected = false;
        }

        edge.IsSelected = !add || !edge.IsSelected;
        RaiseSelection();
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var n in Doc.Nodes)
        {
            n.IsSelected = false;
            n.IsEditing = false;
        }

        foreach (var e in Doc.Edges) e.IsSelected = false;
        RaiseSelection();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var n in Doc.Nodes) n.IsSelected = true;
        RaiseSelection();
    }

    public void SelectInRect(Box r, bool add)
    {
        foreach (var n in Doc.Nodes)
        {
            // Sections are only picked when the box covers them completely (so a box inside one selects its shapes).
            var hit = n.Shape == NodeShape.Frame ? r.Contains(n.Bounds) : r.Intersects(n.Bounds);
            if (add) n.IsSelected |= hit;
            else n.IsSelected = hit;
        }

        RaiseSelection();
    }

    // ================= editing =================

    public void BeginEdit(CanvasNode node)
    {
        if (node.Shape is NodeShape.Image or NodeShape.Card or NodeShape.Ink or NodeShape.Link) return;
        Checkpoint();
        SelectOnly(node);
        node.IsEditing = true;
    }

    [RelayCommand]
    private void EndEdit(CanvasNode? node)
    {
        if (node != null) node.IsEditing = false;
        else foreach (var n in Doc.Nodes) n.IsEditing = false;
    }

    public void MoveSelection(double dx, double dy)
    {
        // Sections carry their contents; a shape selected together with its section only moves once.
        var nodes = SelectedNodes.Where(n => !n.Locked).ToList();
        var carried = nodes.Where(n => n.Shape == NodeShape.Frame).SelectMany(Members).ToHashSet();
        foreach (var n in nodes.Where(n => !carried.Contains(n))) ShiftWithMembers(n, dx, dy);
        UpdateAllEdges();
    }

    [RelayCommand]
    private void DeleteSelection()
    {
        var nodes = SelectedNodes.ToList();
        var edges = SelectedEdges.ToList();
        if (nodes.Count == 0 && edges.Count == 0) return;
        Checkpoint();
        var ids = nodes.Select(n => n.Id).ToHashSet();
        foreach (var e in Doc.Edges.Where(e => e.IsSelected || ids.Contains(e.FromId) || ids.Contains(e.ToId)).ToList()) Doc.Edges.Remove(e);
        foreach (var n in nodes) Doc.Nodes.Remove(n);
        // Shapes of a deleted section stay on the canvas, just loose.
        foreach (var n in Doc.Nodes.Where(n => n.FrameId is { } f && ids.Contains(f))) n.FrameId = null;
    }

    [RelayCommand]
    private void DuplicateSelection()
    {
        if (!HasNodeSelection) return;
        Copy();
        Paste(offset: true);
    }

    [RelayCommand]
    public void Copy()
    {
        var nodes = SelectedNodes.ToList();
        if (nodes.Count == 0) return;
        var ids = nodes.Select(n => n.Id).ToHashSet();
        _clipboard = nodes.Select(Json.CloneDocument).ToList();
        _clipboardEdges = Doc.Edges.Where(e => ids.Contains(e.FromId) && ids.Contains(e.ToId)).Select(Json.CloneDocument).ToList();
    }

    /// <summary>Pastes copied nodes (with the connectors between them) slightly offset. Returns false when nothing was copied.</summary>
    public bool Paste(bool offset = true)
    {
        if (_clipboard is not { Count: > 0 }) return false;
        Checkpoint();
        var map = new Dictionary<Guid, Guid>();
        ClearSelection();
        var z = NextZ();
        foreach (var src in _clipboard)
        {
            var n = Json.CloneDocument(src);
            map[src.Id] = n.Id = Guid.NewGuid();
            if (offset)
            {
                n.X += Grid * 2;
                n.Y += Grid * 2;
            }

            n.Z = z++;
            n.IsSelected = true;
            if (n.CardId is { } cid) n.Card = Main.Workspace.FindCard(cid, out _, out _);
            Doc.Nodes.Add(n);
        }

        foreach (var src in _clipboardEdges ?? [])
        {
            var e = Json.CloneDocument(src);
            e.Id = Guid.NewGuid();
            e.FromId = map[src.FromId];
            e.ToId = map[src.ToId];
            Doc.Edges.Add(e);
        }

        // Next paste lands further down so repeated pastes don't stack exactly.
        _clipboard = Doc.Nodes.Where(n => n.IsSelected).Select(Json.CloneDocument).ToList();
        var ids = _clipboard.Select(n => n.Id).ToHashSet();
        _clipboardEdges = Doc.Edges.Where(e => ids.Contains(e.FromId) && ids.Contains(e.ToId)).Select(Json.CloneDocument).ToList();
        RaiseSelection();
        return true;
    }

    [RelayCommand] private void PasteNodes() => Paste();

    [RelayCommand]
    private void SetFill(string hex)
    {
        var nodes = SelectedNodes.ToList();
        if (Tool == CanvasTool.Pen) PenColor = hex;
        _lastFill = hex;
        if (nodes.Count == 0) return;
        Checkpoint();
        foreach (var n in nodes) n.Fill = hex;
    }

    [RelayCommand]
    private void SetShape(NodeShape shape)
    {
        var nodes = SelectedNodes.Where(n => n.Shape is not (NodeShape.Image or NodeShape.Card or NodeShape.Ink or NodeShape.Frame or NodeShape.Link)).ToList();
        if (nodes.Count == 0) return;
        Checkpoint();
        foreach (var n in nodes)
        {
            n.Shape = shape;
            if (shape == NodeShape.Sticky) n.Fill = DefaultFill(shape);
            if (shape == NodeShape.Circle) n.Width = n.Height = Math.Max(n.Width, n.Height);
        }
    }

    [RelayCommand]
    private void ChangeFontSize(string delta)
    {
        var nodes = SelectedNodes.ToList();
        if (nodes.Count == 0 || !double.TryParse(delta, out var d)) return;
        Checkpoint();
        foreach (var n in nodes) n.FontSize = Math.Clamp(n.FontSize + d, 9, 72);
    }

    [RelayCommand]
    private void BringToFront()
    {
        var z = NextZ();
        foreach (var n in SelectedNodes.ToList()) n.Z = z++;
    }

    [RelayCommand]
    private void SendToBack()
    {
        var min = Doc.Nodes.Count == 0 ? 0 : Doc.Nodes.Min(n => n.Z);
        foreach (var n in SelectedNodes.ToList()) n.Z = --min;
    }

    // ================= connectors =================

    public CanvasEdge? Connect(CanvasNode from, CanvasNode to, bool checkpoint = true)
    {
        if (from == to) return null;
        var existing = Doc.Edges.FirstOrDefault(e => e.FromId == from.Id && e.ToId == to.Id);
        if (existing != null) return existing;
        if (checkpoint) Checkpoint();
        var template = Doc.Edges.LastOrDefault();
        var edge = new CanvasEdge
        {
            FromId = from.Id, ToId = to.Id,
            Style = template?.Style ?? EdgeStyle.Curved,
            Color = template?.Color ?? "#98A2B3",
        };
        Doc.Edges.Add(edge);
        return edge;
    }

    /// <summary>Dragging a connector onto empty space creates a new node there, already connected.</summary>
    public CanvasNode ConnectToNew(CanvasNode from, double x, double y)
    {
        var shape = from.Shape is NodeShape.Image or NodeShape.Card or NodeShape.Text ? NodeShape.Rounded : from.Shape;
        var node = AddNodeAt(shape, x, y);
        node.Fill = from.Shape is NodeShape.Image or NodeShape.Card or NodeShape.Text ? node.Fill : from.Fill;
        Connect(from, node, checkpoint: false);
        return node;
    }

    [RelayCommand]
    private void SetEdgeStyle(EdgeStyle style)
    {
        var edges = SelectedEdges.ToList();
        if (edges.Count == 0) edges = Doc.Edges.ToList(); // no selection: apply to the whole diagram
        if (edges.Count == 0) return;
        Checkpoint();
        foreach (var e in edges)
        {
            e.Style = style;
            UpdateEdge(e);
        }
    }

    [RelayCommand]
    private void ToggleDashed()
    {
        var edges = SelectedEdges.ToList();
        if (edges.Count == 0) return;
        Checkpoint();
        var dashed = !edges[0].Dashed;
        foreach (var e in edges) e.Dashed = dashed;
    }

    [RelayCommand]
    private void ToggleArrow()
    {
        var edges = SelectedEdges.ToList();
        if (edges.Count == 0) return;
        Checkpoint();
        var arrow = !edges[0].Arrow;
        foreach (var e in edges)
        {
            e.Arrow = arrow;
            UpdateEdge(e);
        }
    }

    [RelayCommand]
    private void ReverseEdge()
    {
        var edges = SelectedEdges.ToList();
        if (edges.Count == 0) return;
        Checkpoint();
        foreach (var e in edges)
        {
            (e.FromId, e.ToId) = (e.ToId, e.FromId);
            UpdateEdge(e);
        }
    }

    [RelayCommand]
    private void SetEdgeColor(string hex)
    {
        _lastEdgeColor = hex;
        var edges = SelectedEdges.ToList();
        if (edges.Count == 0) return;
        Checkpoint();
        foreach (var e in edges) e.Color = hex;
    }

    [RelayCommand]
    public async Task EditEdgeLabel(CanvasEdge? edge)
    {
        edge ??= SelectedEdges.FirstOrDefault();
        if (edge == null) return;
        var text = await Main.PromptAsync("Connector label", "Shown in the middle of the line. Leave empty to remove.", "e.g. Yes / No", "Save", edge.Label, allowEmpty: true);
        if (text == null) return;
        Checkpoint();
        edge.Label = text;
    }

    // ================= mind map =================

    /// <summary>Tab: new child to the right, connected.</summary>
    [RelayCommand]
    private void AddChild()
    {
        if (SelectedNodes.ToList() is not [var parent]) return;
        var siblings = Doc.Edges.Where(e => e.FromId == parent.Id).Select(e => FindNode(e.ToId)).OfType<CanvasNode>().ToList();
        var x = parent.X + parent.Width + 90;
        var y = siblings.Count == 0 ? parent.Y : siblings.Max(s => s.Y + s.Height) + 30;
        var shape = parent.Shape is NodeShape.Image or NodeShape.Card or NodeShape.Text or NodeShape.Diamond ? NodeShape.Rounded : parent.Shape;
        var (w, h) = DefaultSize(shape);
        var node = AddNodeAt(shape, x + w / 2, y + h / 2);
        if (parent.Shape is not (NodeShape.Image or NodeShape.Card or NodeShape.Text)) node.Fill = parent.Fill;
        Connect(parent, node, checkpoint: false);
    }

    /// <summary>Enter: new sibling below, connected to the same parent.</summary>
    [RelayCommand]
    private void AddSibling()
    {
        if (SelectedNodes.ToList() is not [var node]) return;
        var parentEdge = Doc.Edges.FirstOrDefault(e => e.ToId == node.Id);
        var parent = parentEdge == null ? null : FindNode(parentEdge.FromId);
        var sib = AddNodeAt(node.Shape is NodeShape.Image or NodeShape.Card ? NodeShape.Rounded : node.Shape,
            node.CenterX, node.Y + node.Height + 30 + node.Height / 2);
        sib.Fill = node.Fill;
        sib.Width = node.Width;
        sib.Height = node.Height;
        if (parent != null) Connect(parent, sib, checkpoint: false);
    }

    /// <summary>
    /// Tidy up: lays out the whole canvas (or the selection, or the inside of one selected section).
    /// Sections are laid out inside first and shrink or grow to fit; then each section moves as one block
    /// with everything in it, and connectors between shapes in different sections count for the section.
    /// </summary>
    [RelayCommand]
    private void AutoLayout()
    {
        if (Doc.Nodes.Count == 0) return;
        Checkpoint();
        var selected = SelectedNodes.ToList();
        if (selected.Count == 1 && selected[0].Shape == NodeShape.Frame)
        {
            LayoutInside(selected[0]);
            UpdateAllEdges();
            return;
        }

        var scope = (selected.Count > 1 ? selected : Doc.Nodes.ToList()).ToHashSet();
        foreach (var f in scope.Where(n => n.Shape == NodeShape.Frame).ToList())
            foreach (var m in Members(f)) scope.Add(m);

        // Units: shapes and sections whose parent section isn't part of the tidy-up.
        var units = scope.Where(n => n.FrameId is not { } fid || FindNode(fid) is not { } parent || !scope.Contains(parent)).ToList();
        foreach (var f in units.Where(n => n.Shape == NodeShape.Frame)) LayoutInside(f);

        var unitIds = units.Select(n => n.Id).ToHashSet();
        Guid? UnitOf(Guid id)
        {
            var n = FindNode(id);
            while (n != null && !unitIds.Contains(n.Id))
                n = n.FrameId is { } fid && scope.Contains(n) ? FindNode(fid) : null;
            return n?.Id;
        }

        var left = units.Min(n => n.X);
        var top = units.Min(n => n.Y);
        var pos = CanvasLayout.Layered(
            units.Select(n => (n.Id, n.Width, n.Height)).ToList(),
            MapEdges(UnitOf),
            gapX: units.Any(n => n.Shape == NodeShape.Frame) ? 110 : 90,
            gapY: units.Any(n => n.Shape == NodeShape.Frame) ? 70 : 36);
        var minY = pos.Values.Min(p => p.Y);
        foreach (var n in units)
        {
            var p = pos[n.Id];
            ShiftWithMembers(n, SnapValue(left + p.X) - n.X, SnapValue(top + p.Y - minY) - n.Y);
        }

        UpdateAllEdges();
    }

    private const double FramePad = 30;

    /// <summary>Lays out a section's own shapes (sections inside it first) and fits the section around them.</summary>
    private void LayoutInside(CanvasNode frame, int depth = 0)
    {
        if (depth > 20) return; // guards against a broken (cyclic) section chain
        var children = Doc.Nodes.Where(n => n.FrameId == frame.Id && n != frame).ToList();
        if (children.Count == 0) return;
        foreach (var c in children.Where(c => c.Shape == NodeShape.Frame)) LayoutInside(c, depth + 1);

        var childIds = children.Select(c => c.Id).ToHashSet();
        Guid? ChildOf(Guid id)
        {
            var n = FindNode(id);
            for (int i = 0; n != null && i < 50; i++)
            {
                if (childIds.Contains(n.Id)) return n.Id;
                n = n.FrameId is { } fid ? FindNode(fid) : null;
            }

            return null;
        }

        var pos = CanvasLayout.Layered(children.Select(n => (n.Id, n.Width, n.Height)).ToList(), MapEdges(ChildOf));
        var minY = pos.Values.Min(p => p.Y);
        var x0 = frame.X + FramePad;
        var y0 = frame.Y + FramePad;
        foreach (var c in children)
        {
            var p = pos[c.Id];
            ShiftWithMembers(c, SnapValue(x0 + p.X) - c.X, SnapValue(y0 + p.Y - minY) - c.Y);
        }

        frame.Width = Math.Max(160, SnapValue(children.Max(c => c.X + c.Width) - frame.X + FramePad));
        frame.Height = Math.Max(100, SnapValue(children.Max(c => c.Y + c.Height) - frame.Y + FramePad));
    }

    /// <summary>Connectors between shapes, re-pointed at the layout unit each end belongs to.</summary>
    private List<(Guid From, Guid To)> MapEdges(Func<Guid, Guid?> unitOf) =>
        Doc.Edges.Where(e => !e.IsFreeFrom && !e.IsFreeTo)
            .Select(e => (From: unitOf(e.FromId), To: unitOf(e.ToId)))
            .Where(t => t.From is { } f && t.To is { } to && f != to)
            .Select(t => (t.From!.Value, t.To!.Value))
            .Distinct()
            .ToList();

    /// <summary>Moves a shape; a section takes everything inside it (and loose lines drawn inside it) along.</summary>
    public void ShiftWithMembers(CanvasNode node, double dx, double dy)
    {
        if (dx == 0 && dy == 0) return;
        if (node.Shape == NodeShape.Frame)
        {
            foreach (var e in FreeEdgesWithin(node.Bounds).ToList())
            {
                if (e.IsFreeFrom) { e.FromX += dx; e.FromY += dy; }
                if (e.IsFreeTo) { e.ToX += dx; e.ToY += dy; }
            }

            foreach (var m in Members(node))
            {
                m.X += dx;
                m.Y += dy;
            }
        }

        node.X += dx;
        node.Y += dy;
    }

    /// <summary>The outermost section a shape sits in (the shape itself when it is loose).</summary>
    public CanvasNode OuterFrame(CanvasNode node)
    {
        var current = node;
        var seen = new HashSet<Guid> { node.Id };
        while (current.FrameId is { } fid && FindNode(fid) is { } parent && seen.Add(parent.Id)) current = parent;
        return current;
    }

    // ================= view =================

    public void ZoomAt(double factor, double sx, double sy)
    {
        var old = Doc.Zoom;
        var z = Math.Clamp(old * factor, 0.1, 4);
        // Keep the world point under the cursor fixed.
        Doc.OffsetX = sx - (sx - Doc.OffsetX) * z / old;
        Doc.OffsetY = sy - (sy - Doc.OffsetY) * z / old;
        Doc.Zoom = z;
    }

    [RelayCommand] private void ZoomIn() => ZoomAt(1.2, ViewportWidth / 2, ViewportHeight / 2);
    [RelayCommand] private void ZoomOut() => ZoomAt(1 / 1.2, ViewportWidth / 2, ViewportHeight / 2);
    [RelayCommand] private void ResetZoom() => ZoomAt(1 / Doc.Zoom, ViewportWidth / 2, ViewportHeight / 2);

    [RelayCommand]
    public void ZoomToFit()
    {
        if (IsEmpty)
        {
            Doc.Zoom = 1;
            Doc.OffsetX = ViewportWidth / 2;
            Doc.OffsetY = ViewportHeight / 2;
            return;
        }

        var b = ContentBounds(60);
        var z = Math.Clamp(Math.Min(ViewportWidth / b.W, ViewportHeight / b.H), 0.1, 1.5);
        Doc.Zoom = z;
        Doc.OffsetX = (ViewportWidth - b.W * z) / 2 - b.X * z;
        Doc.OffsetY = (ViewportHeight - b.H * z) / 2 - b.Y * z;
    }

    public Box ContentBounds(double pad)
    {
        if (Doc.Nodes.Count == 0 && Doc.Edges.Count == 0) return new Box(0, 0, 1, 1);
        var l = Doc.Nodes.Count == 0 ? double.MaxValue : Doc.Nodes.Min(n => n.X) - pad;
        var t = Doc.Nodes.Count == 0 ? double.MaxValue : Doc.Nodes.Min(n => n.Y) - pad;
        var r = Doc.Nodes.Count == 0 ? double.MinValue : Doc.Nodes.Max(n => n.X + n.Width) + pad;
        var btm = Doc.Nodes.Count == 0 ? double.MinValue : Doc.Nodes.Max(n => n.Y + n.Height) + pad;
        foreach (var e in Doc.Edges.Where(e => e.IsFreeFrom || e.IsFreeTo))
        {
            l = Math.Min(l, Math.Min(e.StartX, e.EndX) - pad);
            t = Math.Min(t, Math.Min(e.StartY, e.EndY) - pad);
            r = Math.Max(r, Math.Max(e.StartX, e.EndX) + pad);
            btm = Math.Max(btm, Math.Max(e.StartY, e.EndY) + pad);
        }

        return new Box(l, t, r - l, btm - t);
    }

    [RelayCommand] private void ToggleSnap() => Doc.SnapToGrid = !Doc.SnapToGrid;

    // ================= open / export =================

    [RelayCommand]
    public void OpenNode(CanvasNode node)
    {
        switch (node.Shape)
        {
            case NodeShape.Card when node.CardId is { } id:
                Main.OpenTarget(LinkTarget.Card, id);
                break;
            case NodeShape.Link when node.LinkId is { } linkId:
                Main.OpenTarget(node.LinkKind, linkId);
                break;
            case NodeShape.Image when node.ImagePath != null:
                Main.ShowDialog(new ImagePreviewViewModel(new Attachment { Kind = AttachmentKind.Image, Name = node.Text, RelativePath = node.ImagePath }));
                break;
            default:
                BeginEdit(node);
                break;
        }
    }

    public event EventHandler<string>? RenderRequested;

    [RelayCommand] private void ExportImage() => RenderRequested?.Invoke(this, "export");
    [RelayCommand] private void Print() => RenderRequested?.Invoke(this, "print");
}
