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
        Results.Clear();
        foreach (var h in Services.LinkResolver.Search(_main.Workspace, Query, _kinds, _exclude, _main.CurrentBoard))
            Results.Add(FromHit(h));
        Selected = Results.FirstOrDefault();
    }

    public static LinkOption FromHit(Services.LinkHit h) => new()
    {
        Kind = h.Kind, Id = h.Id, Title = h.Title, Subtitle = h.Subtitle, Score = h.Score,
        Icon = Enum.TryParse<SymbolRegular>(h.Icon, out var icon) ? icon : SymbolRegular.Link24,
    };
}
