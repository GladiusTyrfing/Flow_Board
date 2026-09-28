using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;

namespace FlowBoard.ViewModels;

/// <summary>Base for everything shown in the overlay dialog stack.</summary>
public abstract partial class DialogViewModel : ObservableObject
{
    public virtual bool CloseOnBackdropClick => true;

    /// <summary>How strongly the app is dimmed behind the dialog (0 = not at all, e.g. live-preview drawers).</summary>
    public virtual double BackdropOpacity => 1;

    /// <summary>Entrance animation: "pop" (scale + fade) or "slide" (from the right).</summary>
    public virtual string EnterAnimation => "pop";

    /// <summary>Called after the dialog was removed from the stack.</summary>
    public virtual void OnClosed()
    {
    }

    [RelayCommand]
    protected virtual void Close() => MainViewModel.Instance.CloseDialog(this);
}

public sealed partial class ConfirmDialogViewModel : DialogViewModel
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public string Title { get; init; } = "Are you sure?";
    public string Message { get; init; } = string.Empty;
    public string ConfirmText { get; init; } = "OK";
    public string CancelText { get; init; } = "Cancel";
    public bool IsDanger { get; init; }
    public Task<bool> Result => _tcs.Task;

    [RelayCommand]
    private void Confirm()
    {
        _tcs.TrySetResult(true);
        MainViewModel.Instance.CloseDialog(this);
    }

    public override void OnClosed() => _tcs.TrySetResult(false);
}

public sealed partial class TextPromptViewModel : DialogViewModel
{
    private readonly TaskCompletionSource<string?> _tcs = new();

    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Placeholder { get; init; } = string.Empty;
    public string ConfirmText { get; init; } = "Save";
    public bool Multiline { get; init; }
    public bool AllowEmpty { get; init; }
    [ObservableProperty] private string _text = string.Empty;
    public Task<string?> Result => _tcs.Task;

    [RelayCommand]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(Text) && !AllowEmpty) return;
        _tcs.TrySetResult(Text.Trim());
        MainViewModel.Instance.CloseDialog(this);
    }

    public override void OnClosed() => _tcs.TrySetResult(null);
}

public sealed class ShortcutsViewModel : DialogViewModel
{
    public ShortcutsViewModel()
    {
        var s = MainViewModel.Instance.Settings;
        Sections =
        [
            new("Anywhere in Windows (global)",
            [
                new(s.QuickAddHotkey, "Quick add a card from any app"),
                new(s.ShowHideHotkey, "Show / hide FlowBoard"),
            ]),
            new("General",
            [
                new("Ctrl + K  or  /", "Command palette — search cards, boards and commands"),
                new("Ctrl + Shift + Space  or  Q", "Quick add box"),
                new("Ctrl + F", "Filter the current board"),
                new("Ctrl + Z / Ctrl + Y", "Undo / Redo"),
                new("Ctrl + S", "Save now (FlowBoard also saves automatically)"),
                new("Ctrl + O", "Open a project"),
                new("Ctrl + H", "Home: projects, and stats for every section"),
                new("Ctrl + M", "Show or hide the minimap"),
                new("Ctrl + Shift + S", "High-res screenshot of what's open (the whole board, canvas, storyboard or page)"),
                new("Ctrl + T", "Toggle dark / light theme"),
                new("Ctrl + P", "Start or pause the focus timer"),
                new("Ctrl + ,", "Settings"),
                new("F1  or  ?", "This cheat sheet"),
                new("Esc", "Close the open dialog or cancel editing"),
            ]),
            new("Boards & views",
            [
                new("Ctrl + Tab / Ctrl + Shift + Tab", "Next / previous board"),
                new("Alt + 1 … 9", "Jump to board 1–9 (starred first)"),
                new("Ctrl + 1 / 2 / 3 / 4", "Board / Table / Calendar / Timeline view"),
                new("Ctrl + B  or  [", "Show or hide the sidebar"),
                new("Ctrl + Shift + N", "New board"),
                new("Ctrl + Shift + L", "New list"),
                new("Ctrl + N  or  N", "New card (in the hovered card's list)"),
                new("Ctrl + Shift + A", "Archived items"),
                new("F2", "Rename board"),
            ]),
            new("Hover a card and press",
            [
                new("Enter / Space / E", "Open the card"),
                new("X", "Mark complete / incomplete"),
                new("V", "Expand / collapse the card's details on the board"),
                new("C", "Archive"),
                new("Delete", "Delete"),
                new("Ctrl + D", "Duplicate"),
                new("1 … 9", "Toggle the board's 1st–9th label"),
                new("P", "Cycle priority (none → low → medium → high → urgent)"),
                new("D / L", "Open the card at dates / labels"),
                new("T", "Start / stop the timer"),
                new("F", "Start a focus session on this card"),
                new("Alt + ← / →", "Move to the previous / next list"),
                new("Alt + ↑ / ↓", "Move up / down in the list"),
                new("Alt + Home / End", "Move to the top / bottom of the list"),
            ]),
            new("Inside an open card (when not typing)",
            [
                new("L / D / P / B / M", "Labels / Dates / Priority / Cover / Move"),
                new("K", "Add a checklist"),
                new("A", "Attach files"),
                new("R", "Record a voice note (press again to save)"),
                new("T / F", "Timer / Focus session"),
                new("X", "Mark complete / incomplete"),
                new("Ctrl + V", "Paste an image or files as attachments"),
                new("Ctrl + Enter", "Finish editing a text box"),
                new("Ctrl + D", "Duplicate card"),
                new("Ctrl + Shift + C", "Archive card"),
                new("Ctrl + Delete", "Delete card"),
                new("Enter / Shift + Enter", "Save / new line in comments and checklists"),
            ]),
            new("Smart quick add (any new-card box)",
            [
                new("#label", "Adds (or creates) a label"),
                new("!high  !urgent  !1 … !4", "Sets the priority"),
                new("tomorrow 5pm, fri, next week, in 3 days, 12 May", "Sets the due date"),
            ]),
            new("Canvas & flowcharts",
            [
                new("Double-click", "Add a shape (or edit the one under the mouse)"),
                new("Drag a dot on a shape", "Connect it — drop on empty space to create a connected shape"),
                new("V / R / B / O / D / S / T / C / I", "Select / rounded / box / ellipse / diamond / sticky / text / connector / image"),
                new("L / A / P / F", "Line / arrow / pen (freehand) / section frame"),
                new("Shift + O / K", "Circle / link to a board, storyboard, shot, canvas or page"),
                new("Drag the ◆ on a selected line", "Curve it (double-click the ◆ to straighten)"),
                new("Alt + drag a shape", "Drag out a copy"),
                new("Drag a shape inside a section", "Moves the whole section with everything in it"),
                new("Ctrl + drag", "Move just one shape inside a section · on a section's empty area: box-select inside it"),
                new("Double-click inside a section", "Add a shape there (double-click the title to rename)"),
                new("Right-click empty space", "Add a shape, paste, tidy up, zoom to fit"),
                new("Tab / Enter", "Add a connected child / sibling (mind map)"),
                new("F2", "Edit the selected shape's text"),
                new("Space + drag, middle-drag", "Pan"),
                new("Ctrl + wheel / Ctrl + 0", "Zoom / zoom to fit"),
                new("Ctrl + C / V / D / A", "Copy / paste (images too) / duplicate / select all"),
                new("Arrow keys (+ Shift)", "Nudge by 1 px (grid step)"),
                new("Delete", "Delete the selection"),
            ]),
            new("Notes",
            [
                new("/", "Block menu: headings, to-dos, lists, quote, callout, code, image, link…"),
                new("# ,  ## ,  - ,  1. ,  [] ,  > ,  ``` ,  ---", "Markdown shortcuts at the start of a line"),
                new("Tab / Shift + Tab", "Indent / outdent"),
                new("Alt + ↑ / ↓", "Move the block"),
                new("Ctrl + Enter", "Tick a to-do (leave a code block)"),
                new("Ctrl + Alt + 1 / 2 / 3 / 0", "Heading 1 / 2 / 3 / text"),
                new("Ctrl + V", "Paste an image as a block"),
                new("@ (while typing)", "Link inline to a card, board, storyboard, shot, canvas or page — click the link to open it"),
                new("Select text", "Formatting bar: bold, italic, underline, strike, code, color, highlight"),
                new("Ctrl + B / I / U", "Bold / italic / underline"),
                new("Ctrl + Shift + X / Ctrl + E / Ctrl + Shift + H", "Strikethrough / inline code / highlight"),
            ]),
            new("Storyboards & timeline",
            [
                new("Drag a shot column", "Reorder shots"),
                new("Space / ← → / Home (animatic)", "Play-pause / step / restart"),
                new("Drag a timeline bar / its edges", "Move it / change start or due date"),
                new("Shift + wheel / Ctrl + wheel (timeline)", "Scroll through time / zoom"),
            ]),
        ];
    }

    public IReadOnlyList<ShortcutSection> Sections { get; }
}

public sealed record ShortcutSection(string Title, IReadOnlyList<ShortcutRow> Rows);

public sealed record ShortcutRow(string Keys, string Action);

public sealed class ImagePreviewViewModel : DialogViewModel
{
    public ImagePreviewViewModel(Attachment attachment) => Attachment = attachment;
    public Attachment Attachment { get; }
}

public sealed partial class ArchiveViewModel : DialogViewModel
{
    public ArchiveViewModel(Board board) => Board = board;
    public Board Board { get; }
    [ObservableProperty] private bool _showLists;
}

public sealed partial class ToastMessage : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Text { get; init; } = string.Empty;
    public string? ActionText { get; init; }
    public Action? Action { get; init; }
    public bool IsError { get; init; }

    [RelayCommand]
    private void Invoke()
    {
        Action?.Invoke();
        MainViewModel.Instance.Toasts.Remove(this);
    }

    [RelayCommand]
    private void Dismiss() => MainViewModel.Instance.Toasts.Remove(this);
}

public sealed partial class NewBoardViewModel : DialogViewModel
{
    public NewBoardViewModel(IEnumerable<BoardTemplate> templates)
    {
        Templates = new ObservableCollection<BoardTemplate>(templates);
        _selectedTemplate = Templates.FirstOrDefault();
        _background = _selectedTemplate?.Background ?? Board.GradientPresets[1];
    }

    public ObservableCollection<BoardTemplate> Templates { get; }
    public IReadOnlyList<string> Backgrounds { get; } = Board.GradientPresets.Concat(Board.SolidPresets).ToList();
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private BoardTemplate? _selectedTemplate;
    [ObservableProperty] private string _background;

    public string PreviewName => string.IsNullOrWhiteSpace(Name) ? SelectedTemplate?.Name ?? "New board" : Name;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(PreviewName));

    partial void OnSelectedTemplateChanged(BoardTemplate? value)
    {
        if (value != null) Background = value.Background;
        OnPropertyChanged(nameof(PreviewName));
    }

    [RelayCommand]
    private void PickBackground(string bg) => Background = bg;

    [RelayCommand]
    private void Create()
    {
        if (SelectedTemplate == null) return;
        MainViewModel.Instance.CreateBoardFromTemplate(SelectedTemplate, Name, Background);
        MainViewModel.Instance.CloseDialog(this);
    }

    [RelayCommand]
    private void DeleteTemplate(BoardTemplate t)
    {
        if (t.IsBuiltIn) return;
        MainViewModel.Instance.Workspace.UserTemplates.Remove(t);
        Templates.Remove(t);
        if (SelectedTemplate == t) SelectedTemplate = Templates.FirstOrDefault();
    }
}

/// <summary>Pick any color (used where a palette sits in a menu).</summary>
public sealed partial class ColorDialogViewModel : DialogViewModel
{
    private readonly TaskCompletionSource<string?> _tcs = new();

    public string Title { get; init; } = "Pick a color";
    [ObservableProperty] private string _color = "#8B5CF6";
    public Task<string?> Result => _tcs.Task;

    [RelayCommand]
    private void Apply()
    {
        Controls.ColorPicker.Remember(Color);
        _tcs.TrySetResult(Color);
        MainViewModel.Instance.CloseDialog(this);
    }

    public override void OnClosed() => _tcs.TrySetResult(null);
}
