using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using GongSolutions.Wpf.DragDrop;

namespace FlowBoard.ViewModels;

public sealed class CalendarDay
{
    public DateTime Date { get; init; }
    public bool IsCurrentMonth { get; init; }
    public bool IsToday => Date == DateTime.Today;
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public bool IsPast => Date < DateTime.Today;
    public string DayText => Date.Day == 1 ? Date.ToString("MMM d") : Date.Day.ToString();
    public ObservableCollection<Card> Cards { get; } = [];
}

/// <summary>Month grid of cards by due date. Cards can be dragged between days to reschedule.</summary>
public sealed partial class CalendarViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private bool _allBoards;

    public CalendarViewModel(MainViewModel main)
    {
        _main = main;
        DropHandler = new CalendarDropHandler(this);
    }

    public ObservableCollection<CalendarDay> Days { get; } = [];
    public ObservableCollection<Card> Unscheduled { get; } = [];
    public IDropTarget DropHandler { get; }
    public string MonthTitle => Month.ToString("MMMM yyyy");

    public IReadOnlyList<string> WeekdayNames { get; } =
        Enumerable.Range(0, 7).Select(i => System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat
            .AbbreviatedDayNames[((int)FirstDayOfWeek + i) % 7]).ToList();

    private static DayOfWeek FirstDayOfWeek => System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

    partial void OnMonthChanged(DateTime value)
    {
        OnPropertyChanged(nameof(MonthTitle));
        Rebuild();
    }

    partial void OnAllBoardsChanged(bool value) => Rebuild();

    [RelayCommand] private void PreviousMonth() => Month = Month.AddMonths(-1);
    [RelayCommand] private void NextMonth() => Month = Month.AddMonths(1);
    [RelayCommand] private void GoToToday() => Month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    [RelayCommand]
    private async Task AddCardOnDay(CalendarDay day)
    {
        var board = _main.CurrentBoard;
        if (board == null) return;
        if (board.Lists.Count == 0)
        {
            _main.ShowToast("Add a list to this board first.");
            return;
        }

        var title = await _main.PromptAsync("New card", $"Due {day.Date:dddd, MMMM d}", "Card title", "Add card");
        if (title == null) return;
        _main.Undo.Checkpoint(_main.Workspace, board, "Add card");
        var card = new Card { Title = title, DueDate = day.Date, Board = board, ReminderMinutes = _main.Settings.DefaultReminderMinutes };
        card.AddActivity($"added this card to {board.Lists[0].Name}", _main.Settings.DisplayName);
        board.Lists[0].Cards.Add(card);
        Rebuild();
    }

    public void Reschedule(Card card, DateTime? date)
    {
        if (card.Board is { } board) _main.Undo.Checkpoint(_main.Workspace, board, "Reschedule card");
        if (date == null)
        {
            card.DueDate = null;
            card.StartDate = null;
        }
        else
        {
            var time = card.DueDate?.TimeOfDay ?? TimeSpan.Zero;
            if (card.StartDate is { } start && card.DueDate is { } oldDue)
                card.StartDate = start + (date.Value.Date - oldDue.Date);
            card.DueDate = date.Value.Date + time;
        }

        card.AddActivity(date == null ? "removed the due date" : $"rescheduled this card to {date:MMM d}", _main.Settings.DisplayName);
        Rebuild();
    }

    public void Rebuild()
    {
        Days.Clear();
        Unscheduled.Clear();

        var first = Month;
        int offset = ((int)first.DayOfWeek - (int)FirstDayOfWeek + 7) % 7;
        var start = first.AddDays(-offset);
        var byDate = new Dictionary<DateTime, CalendarDay>();
        for (int i = 0; i < 42; i++)
        {
            var d = new CalendarDay { Date = start.AddDays(i), IsCurrentMonth = start.AddDays(i).Month == first.Month };
            Days.Add(d);
            byDate[d.Date] = d;
        }

        IEnumerable<Board> boards = AllBoards ? _main.Workspace.Boards : _main.CurrentBoard is { } b ? [b] : [];
        foreach (var board in boards)
        {
            foreach (var card in board.Lists.SelectMany(l => l.Cards))
            {
                if (!_main.Filter.Matches(card)) continue;
                if (card.IsCompleted && !_main.Settings.ShowCompletedInCalendar) continue;
                if (card.DueDate is not { } due)
                {
                    if (!card.IsCompleted) Unscheduled.Add(card);
                    continue;
                }

                // Multi-day cards (start date → due date) appear on every day in range.
                var from = card.StartDate?.Date ?? due.Date;
                if (from > due.Date) from = due.Date;
                for (var day = from; day <= due.Date; day = day.AddDays(1))
                    if (byDate.TryGetValue(day, out var cell)) cell.Cards.Add(card);
            }
        }

        foreach (var d in Days)
        {
            var sorted = d.Cards.OrderBy(c => c.IsCompleted).ThenBy(c => c.DueDate).ToList();
            d.Cards.Clear();
            foreach (var c in sorted) d.Cards.Add(c);
        }
    }
}
