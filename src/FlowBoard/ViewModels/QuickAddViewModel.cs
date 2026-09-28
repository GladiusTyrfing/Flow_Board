using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;

namespace FlowBoard.ViewModels;

/// <summary>
/// Quick capture box opened by the global hotkey (Ctrl+Alt+Space by default) from any app.
/// When FlowBoard was hidden before, it hides again after the card is added.
/// </summary>
public sealed partial class QuickAddViewModel : DialogViewModel
{
    private readonly MainViewModel _main;
    private readonly bool _hideAfter;

    [ObservableProperty] private Board? _board;
    [ObservableProperty] private BoardList? _list;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _due = "none";
    [ObservableProperty] private Priority _priority;

    public QuickAddViewModel(MainViewModel main, bool hideAfter)
    {
        _main = main;
        _hideAfter = hideAfter;
        _board = main.CurrentBoard ?? main.Workspace.Boards.FirstOrDefault();
        _list = _board?.Lists.FirstOrDefault();
    }

    public ObservableCollection<Board> Boards => _main.Workspace.Boards;
    public IEnumerable<BoardList> Lists => Board?.Lists ?? Enumerable.Empty<BoardList>();
    public IReadOnlyList<Priority> Priorities { get; } = Enum.GetValues<Priority>();

    partial void OnBoardChanged(Board? value)
    {
        OnPropertyChanged(nameof(Lists));
        List = value?.Lists.FirstOrDefault();
    }

    [RelayCommand]
    private void SetDue(string due) => Due = due;

    [RelayCommand]
    private void Add()
    {
        var title = Title.Trim();
        if (title.Length == 0 || Board == null) return;
        if (List == null)
        {
            _main.ShowToast($"\"{Board.Name}\" has no lists yet.", isError: true);
            return;
        }

        DateTime? due = Due switch
        {
            "today" => DateTime.Today,
            "tomorrow" => DateTime.Today.AddDays(1),
            "nextweek" => DateTime.Today.AddDays(7),
            _ => null,
        };

        _main.Undo.Checkpoint(_main.Workspace, Board, "Quick add card");
        foreach (var line in title.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            var card = new Card
            {
                Title = line, Board = Board, DueDate = due, Priority = Priority, IsCompleted = List.IsDoneList,
                ReminderMinutes = due != null ? _main.Settings.DefaultReminderMinutes : -1,
            };
            card.AddActivity($"added this card to {List.Name} (quick add)", _main.Settings.DisplayName);
            List.Cards.Add(card);
        }

        _main.CloseDialog(this);
        _main.ApplyFilter();
        _main.ShowToast($"Added to {Board.Name} › {List.Name}");
        if (_hideAfter) _main.RequestHideWindow();
    }
}
