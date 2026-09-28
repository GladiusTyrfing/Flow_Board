using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;

namespace FlowBoard.ViewModels;

public sealed record StatTile(string Title, string Value, string Sub, string Icon, string Color);

public sealed record ChartBar(string Label, string Tooltip, double Height, int Value, bool IsToday, bool IsWeekend);

public sealed record MeterRow(string Name, string Value, double Fraction, string Color);

public sealed record PrioritySlice(string Name, int Count, double Fraction, string Color);

public sealed record RecentItem(string Title, string Subtitle, string Icon, Action Open);

/// <summary>Home overview: what's due, what got done, where the time went.</summary>
public sealed partial class DashboardViewModel : DocumentViewModel
{
    public DashboardViewModel(MainViewModel main) : base(main) => Refresh();

    public override object? Model => null;
    public override ActiveView Kind => ActiveView.Dashboard;

    [ObservableProperty] private string _greeting = string.Empty;
    [ObservableProperty] private string _dateText = string.Empty;
    [ObservableProperty] private IReadOnlyList<StatTile> _tiles = [];
    [ObservableProperty] private IReadOnlyList<ChartBar> _bars = [];
    [ObservableProperty] private string _chartMax = "0";
    [ObservableProperty] private string _chartSummary = string.Empty;
    [ObservableProperty] private IReadOnlyList<MeterRow> _timeRows = [];
    [ObservableProperty] private IReadOnlyList<PrioritySlice> _priorities = [];
    [ObservableProperty] private IReadOnlyList<AgendaEntry> _agenda = [];
    [ObservableProperty] private IReadOnlyList<RecentItem> _recent = [];

    [RelayCommand]
    public void Refresh()
    {
        var now = DateTime.Now;
        var s = DashboardStats.Compute(Main.Workspace, now);
        var name = Main.Settings.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var part = now.Hour switch { < 5 => "Good night", < 12 => "Good morning", < 18 => "Good afternoon", _ => "Good evening" };
        Greeting = string.IsNullOrEmpty(name) ? part : $"{part}, {name}";
        DateText = now.ToString("dddd, d MMMM");

        var trend = s.CompletedThisWeek - s.CompletedLastWeek;
        Tiles =
        [
            new("Open cards", s.OpenCards.ToString(), $"across {Main.Workspace.Boards.Count} boards", "TaskListLtr24", "#8B5CF6"),
            new("Done this week", s.CompletedThisWeek.ToString(),
                trend == 0 ? "same as last week" : trend > 0 ? $"▲ {trend} vs last week" : $"▼ {-trend} vs last week", "CheckmarkCircle24", "#10B981"),
            new("Overdue", s.Overdue.ToString(), s.Overdue == 0 ? "nothing late 🎉" : "need attention", "Warning24", s.Overdue == 0 ? "#64748B" : "#EF4444"),
            new("Due in 7 days", s.DueSoon.ToString(), "coming up", "CalendarClock24", "#F59E0B"),
            new("Focus time", FormatMinutes(s.FocusMinutesThisWeek), "tracked in 7 days", "Timer24", "#06B6D4"),
            new("Streak", s.Streak.ToString(), s.Streak == 1 ? "day with a finished card" : "days in a row", "Fire24", "#F97316"),
        ];

        var max = Math.Max(1, s.Days.Max(d => d.Completed));
        Bars = s.Days.Select(d => new ChartBar(
            d.Day.ToString("ddd")[..2],
            $"{d.Day:ddd d MMM}: {d.Completed} completed, {d.Created} created",
            (double)d.Completed / max,
            d.Completed,
            d.Day == now.Date,
            d.Day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
        ChartMax = max.ToString();
        var total = s.Days.Sum(d => d.Completed);
        ChartSummary = $"{total} cards completed in the last {s.Days.Count} days · {Math.Round(total / (double)s.Days.Count, 1)} a day";

        var topMinutes = s.TimePerBoard.Count == 0 ? 1 : s.TimePerBoard.Max(t => t.Minutes);
        TimeRows = s.TimePerBoard.Take(6).Select(t => new MeterRow(t.Board.Name, FormatMinutes(t.Minutes), t.Minutes / topMinutes, BoardColor(t.Board))).ToList();

        var open = Math.Max(1, s.OpenCards);
        Priorities =
        [
            new("Urgent", s.OpenByPriority[Priority.Urgent], s.OpenByPriority[Priority.Urgent] / (double)open, "#EF4444"),
            new("High", s.OpenByPriority[Priority.High], s.OpenByPriority[Priority.High] / (double)open, "#F97316"),
            new("Medium", s.OpenByPriority[Priority.Medium], s.OpenByPriority[Priority.Medium] / (double)open, "#F59E0B"),
            new("Low", s.OpenByPriority[Priority.Low], s.OpenByPriority[Priority.Low] / (double)open, "#3B82F6"),
            new("None", s.OpenByPriority[Priority.None], s.OpenByPriority[Priority.None] / (double)open, "#64748B"),
        ];

        Agenda = s.Agenda;

        var recent = new List<(DateTime When, RecentItem Item)>();
        foreach (var b in Main.Workspace.Boards)
        {
            var x = b;
            recent.Add((b.LastOpened, new RecentItem(b.Name, $"Board · {b.ActiveCardCount} cards", "Board24", () => Main.SelectBoard(x))));
        }

        foreach (var n in Main.Workspace.Notes)
        {
            var x = n;
            recent.Add((n.UpdatedAt, new RecentItem(n.Title, $"Page · edited {n.UpdatedAt:d MMM}", n.Icon, () => Main.OpenNote(x))));
        }

        foreach (var sb in Main.Workspace.Storyboards)
        {
            var x = sb;
            recent.Add((sb.CreatedAt, new RecentItem(sb.Name, $"Storyboard · {sb.Shots.Count} shots", "VideoClip24", () => Main.OpenStoryboard(x))));
        }

        foreach (var c in Main.Workspace.Canvases)
        {
            var x = c;
            recent.Add((c.CreatedAt, new RecentItem(c.Name, $"Canvas · {c.Nodes.Count} shapes", "Flowchart24", () => Main.OpenCanvas(x))));
        }

        Recent = recent.OrderByDescending(r => r.When).Take(6).Select(r => r.Item).ToList();
    }

    private static string BoardColor(Board b)
    {
        // First color of the board background, so bars match the sidebar thumbnails.
        var spec = b.Background;
        var start = spec.IndexOf('#');
        return start >= 0 && spec.Length >= start + 7 ? spec.Substring(start, 7) : "#8B5CF6";
    }

    public static string FormatMinutes(double minutes)
    {
        if (minutes < 1) return "0m";
        var t = TimeSpan.FromMinutes(minutes);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes}m";
    }

    [RelayCommand]
    private void OpenAgenda(AgendaEntry e) => Main.OpenTarget(LinkTarget.Card, e.Card.Id);

    [RelayCommand]
    private void OpenRecent(RecentItem r) => r.Open();
}
