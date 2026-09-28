using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;

namespace FlowBoard.ViewModels;

public sealed class TimelineDay
{
    public required DateTime Date { get; init; }
    public required double X { get; init; }
    public required double Width { get; init; }
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public bool IsToday => Date == DateTime.Today;
    public string DayText { get; init; } = string.Empty;
    public string MonthText { get; init; } = string.Empty;
}

/// <summary>One line of the timeline: a list header or a card bar.</summary>
public sealed partial class TimelineRow : ObservableObject
{
    public required BoardList List { get; init; }
    public Card? Card { get; init; }
    public bool IsHeader => Card == null;
    public double Top { get; set; }
    public double Height { get; set; }
    public string Title => Card?.Title ?? List.Name;
    public int Count { get; init; }
    public string Color { get; init; } = "#8B5CF6";

    [ObservableProperty] private double _left;
    [ObservableProperty] private double _width;
    [ObservableProperty] private bool _hasBar;
    [ObservableProperty] private string _datesText = string.Empty;
    [ObservableProperty] private bool _isBlocked;
}

public sealed record TimelineLink(string Path, string Arrow, bool IsBlocking);

/// <summary>Gantt-style view: cards as bars from start to due date, grouped by list, with dependency arrows.</summary>
public sealed partial class TimelineViewModel : ObservableObject
{
    public const double HeaderRowHeight = 34;
    public const double RowHeight = 40;
    private readonly MainViewModel _main;

    public TimelineViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<TimelineRow> Rows { get; } = [];
    public ObservableCollection<TimelineDay> Days { get; } = [];
    public ObservableCollection<TimelineLink> Links { get; } = [];
    public ObservableCollection<TimelineRow> Unscheduled { get; } = [];

    [ObservableProperty] private DateTime _rangeStart = DateTime.Today.AddDays(-14);
    [ObservableProperty] private double _totalWidth;
    [ObservableProperty] private double _totalHeight;
    [ObservableProperty] private double _todayX;
    [ObservableProperty] private bool _isEmpty;

    public double DayWidth => _main.CurrentBoard?.TimelineDayWidth ?? 36;
    public bool ShowDayNumbers => DayWidth >= 22;
    public int UnscheduledCount => Unscheduled.Count;

    /// <summary>Asks the view to scroll so a date is visible (argument: x position).</summary>
    public event EventHandler<double>? ScrollRequested;

    public void Rebuild()
    {
        Rows.Clear();
        Days.Clear();
        Links.Clear();
        Unscheduled.Clear();
        var board = _main.CurrentBoard;
        if (board == null)
        {
            IsEmpty = true;
            return;
        }

        var cards = board.Lists.SelectMany(l => l.Cards.Where(_main.Filter.Matches).Select(c => (l, c))).ToList();
        var dated = cards.Where(t => t.c.StartDate != null || t.c.DueDate != null).ToList();
        var today = DateTime.Today;
        var first = dated.Count == 0 ? today : dated.Min(t => (t.c.StartDate ?? t.c.DueDate)!.Value.Date);
        var last = dated.Count == 0 ? today : dated.Max(t => (t.c.DueDate ?? t.c.StartDate)!.Value.Date);
        var start = Min(today.AddDays(-14), first.AddDays(-7));
        var end = Max(today.AddDays(60), last.AddDays(21));
        if ((end - start).TotalDays > 730) end = start.AddDays(730);
        // Start on a Monday so weeks line up.
        start = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));
        RangeStart = start;

        var w = DayWidth;
        var dayCount = (int)(end - start).TotalDays + 1;
        for (int i = 0; i < dayCount; i++)
        {
            var d = start.AddDays(i);
            Days.Add(new TimelineDay
            {
                Date = d, X = i * w, Width = w,
                DayText = ShowDayNumbers || d.DayOfWeek == DayOfWeek.Monday ? d.Day.ToString() : string.Empty,
                MonthText = d.Day == 1 || i == 0 ? d.ToString(d.Month == 1 || i == 0 ? "MMMM yyyy" : "MMMM") : string.Empty,
            });
        }

        TotalWidth = dayCount * w;
        TodayX = (today - start).TotalDays * w;

        double y = 0;
        var byCard = new Dictionary<Guid, TimelineRow>();
        foreach (var list in board.Lists)
        {
            var listCards = cards.Where(t => t.l == list).Select(t => t.c).ToList();
            var scheduled = listCards.Where(c => c.StartDate != null || c.DueDate != null).ToList();
            foreach (var c in listCards.Except(scheduled))
                Unscheduled.Add(new TimelineRow { List = list, Card = c, Color = ColorOf(board, list, c) });
            if (scheduled.Count == 0) continue;

            Rows.Add(new TimelineRow { List = list, Top = y, Height = HeaderRowHeight, Count = scheduled.Count, Color = list.Color ?? "#8B5CF6" });
            y += HeaderRowHeight;
            foreach (var c in scheduled.OrderBy(c => c.StartDate ?? c.DueDate))
            {
                var row = new TimelineRow { List = list, Card = c, Top = y, Height = RowHeight, Color = ColorOf(board, list, c) };
                Place(row);
                Rows.Add(row);
                byCard[c.Id] = row;
                y += RowHeight;
            }
        }

        TotalHeight = Math.Max(y, 200);
        IsEmpty = Rows.Count == 0;
        UpdateLinks(byCard);
        OnPropertyChanged(nameof(UnscheduledCount));
        OnPropertyChanged(nameof(ShowDayNumbers));
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static string ColorOf(Board board, BoardList list, Card c)
    {
        if (c.ResolvedLabels.FirstOrDefault() is { } l) return l.Color;
        if (!string.IsNullOrEmpty(list.Color)) return list.Color!;
        return c.Priority switch
        {
            Priority.Urgent => "#EF4444",
            Priority.High => "#F97316",
            Priority.Medium => "#F59E0B",
            Priority.Low => "#3B82F6",
            _ => "#8B5CF6",
        };
    }

    public (DateTime Start, DateTime End) SpanOf(Card c)
    {
        var s = (c.StartDate ?? c.DueDate ?? DateTime.Today).Date;
        var e = (c.DueDate ?? c.StartDate ?? DateTime.Today).Date;
        return e < s ? (e, s) : (s, e);
    }

    private void Place(TimelineRow row, int moveDays = 0, int startDays = 0, int endDays = 0)
    {
        if (row.Card == null) return;
        var (s, e) = SpanOf(row.Card);
        s = s.AddDays(moveDays + startDays);
        e = e.AddDays(moveDays + endDays);
        if (e < s) (s, e) = startDays != 0 ? (e, e) : (s, s);
        row.Left = (s - RangeStart).TotalDays * DayWidth;
        row.Width = ((e - s).TotalDays + 1) * DayWidth;
        row.HasBar = true;
        var days = (int)(e - s).TotalDays + 1;
        row.DatesText = s == e ? s.ToString("ddd d MMM") : $"{s:d MMM} → {e:d MMM} · {days} days";
        row.IsBlocked = row.Card.BlockedByIds.Any(id => _main.Workspace.FindCard(id, out _, out _) is { IsCompleted: false });
    }

    private void UpdateLinks(Dictionary<Guid, TimelineRow> byCard)
    {
        Links.Clear();
        foreach (var row in byCard.Values)
        {
            foreach (var blockerId in row.Card!.BlockedByIds)
            {
                if (!byCard.TryGetValue(blockerId, out var blocker)) continue;
                var a = new Box(blocker.Left, blocker.Top + 8, blocker.Width, RowHeight - 16);
                var b = new Box(row.Left, row.Top + 8, row.Width, RowHeight - 16);
                // Always leave from the blocker's end and arrive at the dependent's start.
                var shape = CanvasGeometry.Route(
                    new Box(a.Right - 1, a.Y, 1, a.H),
                    new Box(b.X, b.Y, 1, b.H),
                    b.X > a.Right + 16 ? EdgeStyle.Curved : EdgeStyle.Elbow, arrow: true);
                var late = SpanOf(row.Card).Start <= SpanOf(blocker.Card!).End;
                Links.Add(new TimelineLink(shape.Path, shape.Arrow, late));
            }
        }
    }

    // ================= dragging =================

    /// <summary>Live preview while dragging a bar (days are relative to the original dates).</summary>
    public void Preview(TimelineRow row, string mode, int days)
    {
        switch (mode)
        {
            case "start": Place(row, startDays: days); break;
            case "end": Place(row, endDays: days); break;
            default: Place(row, moveDays: days); break;
        }
    }

    public void Commit(TimelineRow row, string mode, int days)
    {
        if (row.Card is not { } c || _main.CurrentBoard is not { } board) return;
        if (days == 0)
        {
            Place(row);
            return;
        }

        _main.Undo.Checkpoint(_main.Workspace, board, "Reschedule card");
        var (s, e) = SpanOf(c);
        var dueTime = c.DueDate?.TimeOfDay ?? TimeSpan.Zero;
        switch (mode)
        {
            case "start":
                var ns = s.AddDays(days);
                c.StartDate = ns > e ? e : ns;
                c.DueDate ??= e;
                break;
            case "end":
                var ne = e.AddDays(days);
                if (ne < s) ne = s;
                c.DueDate = ne + dueTime;
                break;
            default:
                if (c.StartDate != null) c.StartDate = c.StartDate.Value.AddDays(days);
                if (c.DueDate != null) c.DueDate = c.DueDate.Value.AddDays(days);
                break;
        }

        c.ReminderSent = false;
        Rebuild();
    }

    // ================= commands =================

    [RelayCommand]
    private void ZoomIn() => SetDayWidth(DayWidth * 1.35);

    [RelayCommand]
    private void ZoomOut() => SetDayWidth(DayWidth / 1.35);

    private void SetDayWidth(double w)
    {
        if (_main.CurrentBoard is not { } b) return;
        b.TimelineDayWidth = Math.Round(Math.Clamp(w, 8, 96));
        OnPropertyChanged(nameof(DayWidth));
        Rebuild();
        ScrollToToday();
    }

    [RelayCommand]
    public void ScrollToToday() => ScrollRequested?.Invoke(this, TodayX - 7 * DayWidth);

    [RelayCommand]
    private void Open(TimelineRow row)
    {
        if (row.Card != null) _main.OpenCard(row.Card);
    }

    /// <summary>Gives an unscheduled card a 3-day bar starting today.</summary>
    [RelayCommand]
    private void Schedule(TimelineRow row)
    {
        if (row.Card is not { } c || _main.CurrentBoard is not { } board) return;
        _main.Undo.Checkpoint(_main.Workspace, board, "Schedule card");
        c.StartDate = DateTime.Today;
        c.DueDate = DateTime.Today.AddDays(2);
        Rebuild();
    }
}
