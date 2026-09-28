using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowBoard.Models;

namespace FlowBoard.ViewModels;

public sealed class TableRow
{
    public required Card Card { get; init; }
    public required Board Board { get; init; }
    public required BoardList List { get; init; }
    public string ListName => List.Name;
    public string BoardName => Board.Name;
    public int ListIndex => Board.Lists.IndexOf(List);
    public DateTime DueSort => Card.DueDate ?? DateTime.MaxValue;
    public double ChecklistSort => Card.ChecklistTotal == 0 ? -1 : (double)Card.ChecklistDone / Card.ChecklistTotal;
    public long TrackedSort => Card.TrackedTime.Ticks;
    public string LabelsText => string.Join(", ", Card.ResolvedLabels.Select(l => l.Name).Where(n => n.Length > 0));
}

/// <summary>Spreadsheet-like view of all cards with sortable columns.</summary>
public sealed partial class TableViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private bool _allBoards;
    [ObservableProperty] private TableRow? _selectedRow;

    public TableViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<TableRow> Rows { get; } = [];
    public IReadOnlyList<Priority> Priorities { get; } = Enum.GetValues<Priority>();

    partial void OnAllBoardsChanged(bool value) => Rebuild();

    public void Rebuild()
    {
        Rows.Clear();
        IEnumerable<Board> boards = AllBoards ? _main.Workspace.Boards : _main.CurrentBoard is { } b ? [b] : [];
        foreach (var board in boards)
            foreach (var list in board.Lists)
                foreach (var card in list.Cards)
                    if (_main.Filter.Matches(card))
                        Rows.Add(new TableRow { Card = card, Board = board, List = list });
    }
}
