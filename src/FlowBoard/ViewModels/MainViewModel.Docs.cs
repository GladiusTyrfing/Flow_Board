using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;

namespace FlowBoard.ViewModels;

/// <summary>Storyboards, canvases, notes and the dashboard: opening, creating, deleting.</summary>
public sealed partial class MainViewModel
{
    [ObservableProperty] private ActiveView _activeView = ActiveView.Board;
    [ObservableProperty] private DocumentViewModel? _activeDocument;

    /// <summary>What the sidebar highlights: the open board or the open document's model.</summary>
    public object? SelectedItem => ActiveView == ActiveView.Board ? CurrentBoard : ActiveDocument?.Model ?? ActiveDocument;

    public IEnumerable<Storyboard> SidebarStoryboards => FilterDocs(Workspace.Storyboards, s => s.Name);
    public IEnumerable<CanvasDoc> SidebarCanvases => FilterDocs(Workspace.Canvases, c => c.Name);
    public IEnumerable<NotePage> SidebarNotes => FilterDocs(Workspace.Notes, n => n.Title);

    private IEnumerable<T> FilterDocs<T>(IEnumerable<T> items, Func<T, string> name) =>
        string.IsNullOrWhiteSpace(SidebarQuery)
            ? items
            : items.Where(i => name(i).Contains(SidebarQuery.Trim(), StringComparison.OrdinalIgnoreCase));

    partial void OnActiveViewChanged(ActiveView value)
    {
        OnPropertyChanged(nameof(SelectedItem));
        RaiseUndoState();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnActiveDocumentChanged(DocumentViewModel? oldValue, DocumentViewModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnDocumentPropertyChanged;
            oldValue.OnDeactivated();
        }

        if (newValue != null) newValue.PropertyChanged += OnDocumentPropertyChanged;
        OnPropertyChanged(nameof(SelectedItem));
        RaiseUndoState();
    }

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentViewModel.CanUndo) or nameof(DocumentViewModel.CanRedo)) RaiseUndoState();
    }

    /// <summary>Title bar undo/redo: the board history on boards, the document's own history elsewhere.</summary>
    public bool CanUndoNow => ActiveView == ActiveView.Board ? Undo.CanUndo : ActiveDocument?.CanUndo == true;
    public bool CanRedoNow => ActiveView == ActiveView.Board ? Undo.CanRedo : ActiveDocument?.CanRedo == true;
    public string? UndoDescriptionNow => ActiveView == ActiveView.Board ? Undo.UndoDescription : null;
    public string? RedoDescriptionNow => ActiveView == ActiveView.Board ? Undo.RedoDescription : null;

    public void RaiseUndoState()
    {
        OnPropertyChanged(nameof(CanUndoNow));
        OnPropertyChanged(nameof(CanRedoNow));
        OnPropertyChanged(nameof(UndoDescriptionNow));
        OnPropertyChanged(nameof(RedoDescriptionNow));
    }

    public void RefreshDocSidebar()
    {
        OnPropertyChanged(nameof(SidebarStoryboards));
        OnPropertyChanged(nameof(SidebarCanvases));
        OnPropertyChanged(nameof(SidebarNotes));
        OnPropertyChanged(nameof(SelectedItem));
    }

    private void ShowDocument(DocumentViewModel doc)
    {
        CloseAllDialogs();
        IsFilterOpen = false;
        ActiveDocument = doc;
        ActiveView = doc.Kind;
        if (IsNarrow) NarrowSidebarOpen = false;
    }

    /// <summary>Back to the board area (used by SelectBoard).</summary>
    private void LeaveDocument()
    {
        if (ActiveView == ActiveView.Board) return;
        ActiveDocument = null;
        ActiveView = ActiveView.Board;
    }

    // ---------- open ----------

    [RelayCommand] public void OpenStoryboard(Storyboard s) => ShowDocument(new StoryboardViewModel(this, s));
    [RelayCommand] public void OpenCanvas(CanvasDoc c) => ShowDocument(new CanvasViewModel(this, c));
    [RelayCommand] public void OpenNote(NotePage n) => ShowDocument(new NoteViewModel(this, n));
    [RelayCommand] public void OpenDashboard() => ShowDocument(new DashboardViewModel(this));

    /// <summary>Opens anything that can be linked to (from notes, the palette...).</summary>
    public void OpenTarget(LinkTarget kind, Guid id)
    {
        switch (kind)
        {
            case LinkTarget.Board when Workspace.Boards.FirstOrDefault(b => b.Id == id) is { } b:
                SelectBoard(b);
                break;
            case LinkTarget.Card when Workspace.FindCard(id, out var board, out _) is { } card && board != null:
                // From a storyboard, canvas, note or the dashboard the card opens on top; on boards we switch to its board.
                if (ActiveView == ActiveView.Board && CurrentBoard != board) SelectBoard(board);
                card.Board ??= board;
                OpenCard(card);
                break;
            case LinkTarget.Storyboard when Workspace.Storyboards.FirstOrDefault(s => s.Id == id) is { } s:
                OpenStoryboard(s);
                break;
            case LinkTarget.Canvas when Workspace.Canvases.FirstOrDefault(c => c.Id == id) is { } c:
                OpenCanvas(c);
                break;
            case LinkTarget.Note when Workspace.Notes.FirstOrDefault(n => n.Id == id) is { } n:
                OpenNote(n);
                break;
            case LinkTarget.Shot when Services.LinkResolver.FindShot(Workspace, id, out var owner) is { } shot && owner != null:
                if (ActiveDocument is not StoryboardViewModel { } open || open.Board != owner) OpenStoryboard(owner);
                (ActiveDocument as StoryboardViewModel)?.FocusShot(shot);
                break;
            default:
                ShowToast("That item no longer exists.", isError: true);
                break;
        }
    }

    /// <summary>Raised to save ("save") or copy ("copy") a high-res screenshot of what's open.</summary>
    public event EventHandler<string>? ScreenshotRequested;

    [RelayCommand] private void Screenshot(string? mode) => ScreenshotRequested?.Invoke(this, mode ?? "save");

    /// <summary>Theme and wallpaper for the open storyboard, canvas or page.</summary>
    [RelayCommand]
    private void OpenDocStyle()
    {
        if (ActiveDocument?.Model is not StyledDocument doc) return;
        if (Dialogs.OfType<DocStyleViewModel>().FirstOrDefault() is { } open) CloseDialog(open);
        var name = doc switch { Storyboard s => s.Name, CanvasDoc c => c.Name, NotePage n => n.Title, _ => string.Empty };
        ShowDialog(new DocStyleViewModel(this, doc, name));
    }

    // ---------- create ----------

    [RelayCommand]
    private void NewStoryboard()
    {
        if (!EnsureProject()) return;
        var sb = new Storyboard { Name = $"Storyboard {Workspace.Storyboards.Count + 1}" };
        for (int i = 0; i < 3; i++) sb.Shots.Add(new Shot { Title = $"Shot {i + 1}" });
        Workspace.Storyboards.Add(sb);
        RefreshDocSidebar();
        OpenStoryboard(sb);
    }

    [RelayCommand]
    private void NewCanvas()
    {
        if (!EnsureProject()) return;
        var c = new CanvasDoc { Name = $"Canvas {Workspace.Canvases.Count + 1}" };
        var start = new CanvasNode { Shape = NodeShape.Ellipse, Text = "Start", X = 0, Y = 0, Width = 140, Height = 60, Fill = "#10B981" };
        var step = new CanvasNode { Shape = NodeShape.Rounded, Text = "Do something", X = 220, Y = 0, Width = 170, Height = 60, Fill = "#8B5CF6" };
        c.Nodes.Add(start);
        c.Nodes.Add(step);
        c.Edges.Add(new CanvasEdge { FromId = start.Id, ToId = step.Id });
        Workspace.Canvases.Add(c);
        RefreshDocSidebar();
        OpenCanvas(c);
    }

    [RelayCommand]
    private void NewNote()
    {
        if (!EnsureProject()) return;
        var n = new NotePage { Title = $"Untitled page {Workspace.Notes.Count + 1}" };
        n.Blocks.Add(new NoteBlock { Type = BlockType.Paragraph });
        Workspace.Notes.Add(n);
        RefreshDocSidebar();
        OpenNote(n);
    }

    // ---------- star / duplicate / delete ----------

    [RelayCommand]
    private void ToggleDocStar(object? doc)
    {
        switch (doc)
        {
            case Storyboard s: s.IsStarred = !s.IsStarred; break;
            case CanvasDoc c: c.IsStarred = !c.IsStarred; break;
            case NotePage n: n.IsStarred = !n.IsStarred; break;
        }

        RefreshDocSidebar();
    }

    [RelayCommand]
    private void DuplicateDoc(object? doc)
    {
        object? copy = doc switch
        {
            Storyboard s => Services.Json.CloneDocument(s),
            CanvasDoc c => Services.Json.CloneDocument(c),
            NotePage n => Services.Json.CloneDocument(n),
            _ => null,
        };
        switch (copy)
        {
            case Storyboard s:
                s.Name += " (copy)";
                Workspace.Storyboards.Add(s);
                break;
            case CanvasDoc c:
                c.Name += " (copy)";
                Workspace.Canvases.Add(c);
                break;
            case NotePage n:
                n.Title += " (copy)";
                Workspace.Notes.Add(n);
                break;
        }

        RefreshDocSidebar();
    }

    [RelayCommand]
    private async Task DeleteDoc(object? doc)
    {
        (IList? list, string name) = doc switch
        {
            Storyboard s => ((IList)Workspace.Storyboards, s.Name),
            CanvasDoc c => (Workspace.Canvases, c.Name),
            NotePage n => (Workspace.Notes, n.Title),
            _ => (null, string.Empty),
        };
        if (list == null || doc == null) return;
        var ok = await ConfirmAsync("Delete?", $"\"{name}\" will be deleted.", "Delete", danger: true);
        if (!ok) return;

        var index = list.IndexOf(doc);
        list.Remove(doc);
        if (ActiveDocument?.Model == doc) SelectBoard(CurrentBoard ?? Workspace.Boards.FirstOrDefault());
        RefreshDocSidebar();
        ShowToast($"Deleted \"{name}\"", "Undo", () =>
        {
            list.Insert(Math.Min(index, list.Count), doc);
            RefreshDocSidebar();
        });
    }
}

public sealed partial class MainViewModel
{
    /// <summary>Shows the link search dialog; null when cancelled.</summary>
    public Task<LinkPick?> PickLinkAsync(string title, params LinkTarget[] kinds) => PickLinkAsync(title, kinds, null);

    public async Task<LinkPick?> PickLinkAsync(string title, IEnumerable<LinkTarget> kinds, IEnumerable<Guid>? exclude)
    {
        var vm = new LinkPickerViewModel(this, title, kinds, exclude);
        ShowDialog(vm);
        return await vm.Result;
    }
}
