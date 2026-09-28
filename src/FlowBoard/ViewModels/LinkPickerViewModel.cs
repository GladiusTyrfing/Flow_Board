using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using Wpf.Ui.Controls;

namespace FlowBoard.ViewModels;

public readonly record struct LinkPick(LinkTarget Kind, Guid Id, string Title);

public sealed class LinkOption
{
    public required LinkTarget Kind { get; init; }
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string Subtitle { get; init; } = string.Empty;
    public SymbolRegular Icon { get; init; }
    public string Group => Kind.ToString();
    public int Score { get; set; }
}

/// <summary>Search dialog for linking cards, boards, storyboards, canvases or notes.</summary>
public sealed partial class LinkPickerViewModel : DialogViewModel
{
    private readonly MainViewModel _main;
    private readonly HashSet<LinkTarget> _kinds;
    private readonly HashSet<Guid> _exclude;
    private readonly TaskCompletionSource<LinkPick?> _tcs = new();

    public LinkPickerViewModel(MainViewModel main, string title, IEnumerable<LinkTarget> kinds, IEnumerable<Guid>? exclude = null)
    {
        _main = main;
        Title = title;
        _kinds = kinds.ToHashSet();
        _exclude = exclude?.ToHashSet() ?? [];
        Refresh();
    }

    public string Title { get; }
    public Task<LinkPick?> Result => _tcs.Task;
    public ObservableCollection<LinkOption> Results { get; } = [];
    public string Placeholder => _kinds.Count == 1 && _kinds.Contains(LinkTarget.Card) ? "Search cards on every board…" : "Search cards, boards, storyboards, canvases, notes…";

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private LinkOption? _selected;

    partial void OnQueryChanged(string value) => Refresh();

    [RelayCommand]
    private void Choose(LinkOption? option)
    {
        option ??= Selected;
        if (option == null) return;
        _tcs.TrySetResult(new LinkPick(option.Kind, option.Id, option.Title));
        _main.CloseDialog(this);
    }

    [RelayCommand]
    private void MoveSelection(string direction)
    {
        if (Results.Count == 0) return;
        var i = Selected == null ? -1 : Results.IndexOf(Selected);
        i = direction == "up" ? Math.Max(0, i - 1) : Math.Min(Results.Count - 1, i + 1);
        Selected = Results[i];
    }

    public override void OnClosed() => _tcs.TrySetResult(null);

    private void Refresh()
    {
        var q = Query.Trim();
        var items = new List<LinkOption>();

        void Add(LinkTarget kind, Guid id, string title, string sub, SymbolRegular icon, int bonus)
        {
            if (!_kinds.Contains(kind) || _exclude.Contains(id)) return;
            var s = CommandPaletteViewModel.Score(title, q);
            if (q.Length > 0 && s <= 0) return;
            items.Add(new LinkOption { Kind = kind, Id = id, Title = title, Subtitle = sub, Icon = icon, Score = s + bonus });
        }

        foreach (var (board, list, card) in _main.Workspace.EnumerateActiveCards())
            Add(LinkTarget.Card, card.Id, card.Title, $"{board.Name} › {list.Name}", card.IsCompleted ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.TaskListLtr24,
                board == _main.CurrentBoard ? 6 : 0);
        foreach (var b in _main.Workspace.Boards) Add(LinkTarget.Board, b.Id, b.Name, $"Board · {b.ActiveCardCount} cards", SymbolRegular.Board24, 4);
        foreach (var s in _main.Workspace.Storyboards) Add(LinkTarget.Storyboard, s.Id, s.Name, $"Storyboard · {s.Shots.Count} shots", SymbolRegular.VideoClip24, 3);
        foreach (var c in _main.Workspace.Canvases) Add(LinkTarget.Canvas, c.Id, c.Name, $"Canvas · {c.Nodes.Count} shapes", SymbolRegular.Flowchart24, 3);
        foreach (var n in _main.Workspace.Notes) Add(LinkTarget.Note, n.Id, n.Title, "Note page", SymbolRegular.DocumentText24, 3);

        Results.Clear();
        foreach (var i in items.OrderByDescending(i => i.Score).Take(80)) Results.Add(i);
        Selected = Results.FirstOrDefault();
    }
}
