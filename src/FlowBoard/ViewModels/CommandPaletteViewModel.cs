using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using Wpf.Ui.Controls;

namespace FlowBoard.ViewModels;

public sealed class PaletteItem
{
    public required string Title { get; init; }
    public string Subtitle { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public SymbolRegular Icon { get; init; } = SymbolRegular.ChevronRight24;
    public string? Shortcut { get; init; }
    public string? ColorHex { get; init; }
    public required Action Execute { get; init; }
    public int Score { get; set; }
}

/// <summary>Ctrl+K: fuzzy search over commands, boards and every card.</summary>
public sealed partial class CommandPaletteViewModel : DialogViewModel
{
    private readonly MainViewModel _main;
    private readonly List<PaletteItem> _commands;

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private PaletteItem? _selected;

    public CommandPaletteViewModel(MainViewModel main)
    {
        _main = main;
        _commands = BuildCommands();
        Refresh();
    }

    public ObservableCollection<PaletteItem> Results { get; } = [];

    partial void OnQueryChanged(string value) => Refresh();

    [RelayCommand]
    private void Run(PaletteItem? item)
    {
        item ??= Selected;
        if (item == null) return;
        _main.CloseDialog(this);
        item.Execute();
    }

    [RelayCommand]
    private void MoveSelection(string direction)
    {
        if (Results.Count == 0) return;
        var i = Selected == null ? -1 : Results.IndexOf(Selected);
        i = direction == "up" ? Math.Max(0, i - 1) : Math.Min(Results.Count - 1, i + 1);
        Selected = Results[i];
    }

    private void Refresh()
    {
        var q = Query.Trim();
        var items = new List<PaletteItem>();

        foreach (var c in _commands)
        {
            c.Score = Score(c.Title, q);
            if (c.Score > 0 || q.Length == 0) items.Add(c);
        }

        foreach (var b in _main.Workspace.Boards)
        {
            var s = Score(b.Name, q);
            if (q.Length == 0 || s > 0)
            {
                var board = b;
                items.Add(new PaletteItem
                {
                    Title = b.Name, Subtitle = $"{b.Lists.Count} lists · {b.ActiveCardCount} cards", Group = "Boards",
                    Icon = b.IsStarred ? SymbolRegular.Star24 : SymbolRegular.Board24, Score = s + 5,
                    Execute = () => _main.SelectBoard(board),
                });
            }
        }

        void AddDoc(string title, string subtitle, string group, SymbolRegular icon, Action open)
        {
            var s = Score(title, q);
            if (q.Length > 0 && s <= 0) return;
            items.Add(new PaletteItem { Title = title, Subtitle = subtitle, Group = group, Icon = icon, Score = s + 4, Execute = open });
        }

        foreach (var sb in _main.Workspace.Storyboards)
        {
            var x = sb;
            AddDoc(sb.Name, $"{sb.Mode} storyboard · {sb.Shots.Count} {(sb.Mode == StoryboardMode.Animation ? "frames" : "shots")}", "Storyboards", SymbolRegular.VideoClip24, () => _main.OpenStoryboard(x));
        }

        if (q.Length > 0)
        {
            foreach (var sb in _main.Workspace.Storyboards)
            {
                foreach (var shot in sb.Shots)
                {
                    var title = Services.LinkResolver.ShotTitle(sb, shot);
                    var s = Score(title, q);
                    if (s <= 0) continue;
                    var id = shot.Id;
                    items.Add(new PaletteItem
                    {
                        Title = title, Subtitle = $"Shot {sb.Shots.IndexOf(shot) + 1} in {sb.Name}", Group = "Shots", Icon = SymbolRegular.SlideLayout24, Score = s + 3,
                        Execute = () => _main.OpenTarget(LinkTarget.Shot, id),
                    });
                }
            }
        }

        foreach (var cv in _main.Workspace.Canvases)
        {
            var x = cv;
            AddDoc(cv.Name, $"Canvas · {cv.Nodes.Count} shapes", "Canvases", SymbolRegular.Flowchart24, () => _main.OpenCanvas(x));
        }

        foreach (var n in _main.Workspace.Notes)
        {
            var x = n;
            AddDoc(n.Title, $"Page · edited {n.UpdatedAt:d MMM}", "Notes", SymbolRegular.DocumentText24, () => _main.OpenNote(x));
        }

        if (q.Length > 0)
        {
            // Text inside note pages.
            foreach (var n in _main.Workspace.Notes)
            {
                var hit = n.Blocks.FirstOrDefault(b => b.Text.Contains(q, StringComparison.OrdinalIgnoreCase));
                if (hit == null || Score(n.Title, q) > 0) continue;
                var x = n;
                var snippet = hit.Text.Length > 70 ? hit.Text[..70] + "…" : hit.Text;
                items.Add(new PaletteItem { Title = n.Title, Subtitle = snippet, Group = "Notes", Icon = SymbolRegular.DocumentText24, Score = 9, Execute = () => _main.OpenNote(x) });
            }

            foreach (var (board, list, card) in _main.Workspace.EnumerateActiveCards())
            {
                var s = Math.Max(Score(card.Title, q) + 10, card.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ? 8 : 0);
                if (s <= 10 && !card.Description.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
                var (b2, c2) = (board, card);
                items.Add(new PaletteItem
                {
                    Title = card.Title, Subtitle = $"{board.Name} › {list.Name}", Group = "Cards",
                    Icon = card.IsCompleted ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.TaskListLtr24, Score = s,
                    Execute = () =>
                    {
                        _main.SelectBoard(b2);
                        _main.OpenCard(c2);
                    },
                });
            }
        }

        Results.Clear();
        IEnumerable<PaletteItem> ordered = q.Length == 0 ? items : items.OrderByDescending(i => i.Score);
        foreach (var i in ordered.Take(60)) Results.Add(i);
        Selected = Results.FirstOrDefault();
    }

    /// <summary>Simple fuzzy score: substring beats in-order character match; 0 = no match.</summary>
    internal static int Score(string text, string q)
    {
        if (q.Length == 0) return 1;
        if (string.IsNullOrEmpty(text)) return 0;
        var idx = text.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (idx == 0) return 100;
        if (idx > 0) return 80 - Math.Min(idx, 40);
        int ti = 0, matched = 0;
        foreach (var ch in q)
        {
            if (char.IsWhiteSpace(ch)) continue;
            ti = text.IndexOf(char.ToString(ch), ti, StringComparison.OrdinalIgnoreCase);
            if (ti < 0) return 0;
            matched++;
            ti++;
        }

        return matched > 0 ? 20 : 0;
    }

    private List<PaletteItem> BuildCommands() =>
    [
        new() { Title = "New card", Group = "Commands", Icon = SymbolRegular.Add24, Shortcut = "Ctrl+N", Execute = () => _main.QuickAddCardCommand.Execute(null) },
        new() { Title = "Quick add card…", Group = "Commands", Icon = SymbolRegular.Flash24, Shortcut = "Ctrl+Shift+Space", Execute = () => _main.OpenQuickAddCommand.Execute(null) },
        new() { Title = "Save now", Group = "Commands", Icon = SymbolRegular.Save24, Shortcut = "Ctrl+S", Execute = () => _main.SaveNowCommand.Execute(null) },
        new() { Title = "New list", Group = "Commands", Icon = SymbolRegular.ColumnTriple24, Shortcut = "Ctrl+Shift+L", Execute = () => _main.StartAddListCommand.Execute(null) },
        new() { Title = "New board", Group = "Commands", Icon = SymbolRegular.Board24, Shortcut = "Ctrl+Shift+N", Execute = () => _main.NewBoardCommand.Execute(null) },
        new() { Title = "Board view", Group = "Commands", Icon = SymbolRegular.Board24, Shortcut = "Ctrl+1", Execute = () => _main.SetViewModeCommand.Execute(BoardViewMode.Board) },
        new() { Title = "Table view", Group = "Commands", Icon = SymbolRegular.Table24, Shortcut = "Ctrl+2", Execute = () => _main.SetViewModeCommand.Execute(BoardViewMode.Table) },
        new() { Title = "Calendar view", Group = "Commands", Icon = SymbolRegular.CalendarLtr24, Shortcut = "Ctrl+3", Execute = () => _main.SetViewModeCommand.Execute(BoardViewMode.Calendar) },
        new() { Title = "Timeline view", Group = "Commands", Icon = SymbolRegular.TextBulletListSquare24, Shortcut = "Ctrl+4", Execute = () => _main.SetViewModeCommand.Execute(BoardViewMode.Timeline) },
        new() { Title = "Dashboard", Group = "Commands", Icon = SymbolRegular.DataPie24, Execute = () => _main.OpenDashboardCommand.Execute(null) },
        new() { Title = "New storyboard", Group = "Commands", Icon = SymbolRegular.VideoClip24, Execute = () => _main.NewStoryboardCommand.Execute(null) },
        new() { Title = "New canvas / flowchart", Group = "Commands", Icon = SymbolRegular.Flowchart24, Execute = () => _main.NewCanvasCommand.Execute(null) },
        new() { Title = "New note page", Group = "Commands", Icon = SymbolRegular.DocumentText24, Execute = () => _main.NewNoteCommand.Execute(null) },
        new() { Title = "Filter cards", Group = "Commands", Icon = SymbolRegular.Filter24, Shortcut = "Ctrl+F", Execute = () => _main.OpenFilterCommand.Execute(null) },
        new() { Title = "Toggle dark / light theme", Group = "Commands", Icon = SymbolRegular.WeatherMoon24, Shortcut = "Ctrl+T", Execute = () => _main.ToggleThemeCommand.Execute(null) },
        new() { Title = "Toggle sidebar", Group = "Commands", Icon = SymbolRegular.PanelLeft24, Shortcut = "Ctrl+B", Execute = () => _main.ToggleSidebarCommand.Execute(null) },
        new() { Title = "Start / pause focus timer", Group = "Commands", Icon = SymbolRegular.Timer24, Shortcut = "Ctrl+P", Execute = () => _main.TogglePomodoroCommand.Execute(null) },
        new() { Title = "Change board background", Group = "Commands", Icon = SymbolRegular.PaintBrush24, Execute = () => _main.OpenBoardSettingsCommand.Execute("background") },
        new() { Title = "Edit labels", Group = "Commands", Icon = SymbolRegular.Tag24, Execute = () => _main.OpenBoardSettingsCommand.Execute("labels") },
        new() { Title = "Archived items", Group = "Commands", Icon = SymbolRegular.Archive24, Execute = () => _main.OpenArchiveCommand.Execute(null) },
        new() { Title = "Save board as template", Group = "Commands", Icon = SymbolRegular.Bookmark24, Execute = () => _main.SaveAsTemplateCommand.Execute(null) },
        new() { Title = "Undo", Group = "Commands", Icon = SymbolRegular.ArrowUndo24, Shortcut = "Ctrl+Z", Execute = () => _main.UndoCommand.Execute(null) },
        new() { Title = "Redo", Group = "Commands", Icon = SymbolRegular.ArrowRedo24, Shortcut = "Ctrl+Y", Execute = () => _main.RedoCommand.Execute(null) },
        new() { Title = "Settings", Group = "Commands", Icon = SymbolRegular.Settings24, Shortcut = "Ctrl+,", Execute = () => _main.OpenSettingsCommand.Execute(null) },
        new() { Title = "Keyboard shortcuts", Group = "Commands", Icon = SymbolRegular.Keyboard24, Shortcut = "F1", Execute = () => _main.OpenShortcutsCommand.Execute(null) },
        new() { Title = "Back up everything to a zip…", Group = "Commands", Icon = SymbolRegular.ArrowExport24, Execute = () => _main.BackupNowCommand.Execute(null) },
        new() { Title = "Open data folder", Group = "Commands", Icon = SymbolRegular.FolderOpen24, Execute = () => _main.OpenDataFolderCommand.Execute(null) },
    ];
}
