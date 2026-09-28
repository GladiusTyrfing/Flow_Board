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
    [ObservableProperty] private string _text = string.Empty;
    public Task<string?> Result => _tcs.Task;

    [RelayCommand]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(Text)) return;
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
                new("Ctrl + 1 / 2 / 3", "Board / Table / Calendar view"),
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
