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
    Diamond,
    Sticky,
    Text,
    Connector,
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
    public bool IsEmpty => Doc.Nodes.Count == 0;

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
    }

    private void OnEdgesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (CanvasEdge edge in e.NewItems) UpdateEdge(edge);
        RaiseSelection();
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
    }

    public CanvasNode? FindNode(Guid id) => Doc.Nodes.FirstOrDefault(n => n.Id == id);

    public void UpdateEdge(CanvasEdge edge)
    {
        if (FindNode(edge.FromId) is not { } a || FindNode(edge.ToId) is not { } b)
        {
            edge.PathData = string.Empty;
            edge.ArrowData = string.Empty;
            return;
        }

        var shape = CanvasGeometry.Route(a.Bounds, b.Bounds, edge.Style, edge.Arrow);
        edge.PathData = shape.Path;
        edge.ArrowData = shape.Arrow;
        edge.LabelX = shape.LabelX;
        edge.LabelY = shape.LabelY;
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
        _ => (170, 70),
    };

    public static string DefaultFill(NodeShape shape) => shape switch
    {
        NodeShape.Sticky => "#FACC15",
        NodeShape.Diamond => "#F97316",
        NodeShape.Ellipse => "#10B981",
        NodeShape.Text => "#00000000",
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
        if (edit && shape is not (NodeShape.Image or NodeShape.Card)) node.IsEditing = true;
        return node;
    }

    private string LastFill(NodeShape shape) =>
        shape is NodeShape.Sticky or NodeShape.Text ? DefaultFill(shape) : (_lastFill ?? DefaultFill(shape));

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
            var hit = r.Intersects(n.Bounds);
            if (add) n.IsSelected |= hit;
            else n.IsSelected = hit;
        }

        RaiseSelection();
    }

    // ================= editing =================

    public void BeginEdit(CanvasNode node)
    {
        if (node.Shape is NodeShape.Image or NodeShape.Card) return;
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
        foreach (var n in SelectedNodes.ToList())
        {
            n.X += dx;
            n.Y += dy;
        }
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
        _lastFill = hex;
        if (nodes.Count == 0) return;
        Checkpoint();
        foreach (var n in nodes) n.Fill = hex;
    }

    [RelayCommand]
    private void SetShape(NodeShape shape)
    {
        var nodes = SelectedNodes.Where(n => n.Shape is not (NodeShape.Image or NodeShape.Card)).ToList();
        if (nodes.Count == 0) return;
        Checkpoint();
        foreach (var n in nodes)
        {
            n.Shape = shape;
            if (shape == NodeShape.Sticky) n.Fill = DefaultFill(shape);
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

    [RelayCommand]
    private void AutoLayout()
    {
        if (Doc.Nodes.Count == 0) return;
        Checkpoint();
        var targets = HasNodeSelection && SelectedNodes.Count() > 1 ? SelectedNodes.ToList() : Doc.Nodes.ToList();
        var ids = targets.Select(n => n.Id).ToHashSet();
        var left = targets.Min(n => n.X);
        var top = targets.Min(n => n.Y);
        var pos = CanvasLayout.Layered(
            targets.Select(n => (n.Id, n.Width, n.Height)).ToList(),
            Doc.Edges.Where(e => ids.Contains(e.FromId) && ids.Contains(e.ToId)).Select(e => (e.FromId, e.ToId)).ToList());
        var minY = pos.Values.Min(p => p.Y);
        foreach (var n in targets)
        {
            var p = pos[n.Id];
            n.X = SnapValue(left + p.X);
            n.Y = SnapValue(top + p.Y - minY);
        }
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
        if (Doc.Nodes.Count == 0)
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
        if (Doc.Nodes.Count == 0) return new Box(0, 0, 1, 1);
        var l = Doc.Nodes.Min(n => n.X) - pad;
        var t = Doc.Nodes.Min(n => n.Y) - pad;
        var r = Doc.Nodes.Max(n => n.X + n.Width) + pad;
        var btm = Doc.Nodes.Max(n => n.Y + n.Height) + pad;
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
