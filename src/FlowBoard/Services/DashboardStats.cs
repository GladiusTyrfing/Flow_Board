using FlowBoard.Models;

namespace FlowBoard.Services;

public sealed record DayCount(DateTime Day, int Completed, int Created);

public sealed record BoardMinutes(Board Board, double Minutes);

public sealed record AgendaEntry(Board Board, BoardList List, Card Card);

/// <summary>Numbers for the dashboard, computed from the workspace (pure, so it can be tested).</summary>
public sealed record DashboardStats(
    int OpenCards,
    int CompletedThisWeek,
    int CompletedLastWeek,
    int Overdue,
    int DueSoon,
    double FocusMinutesThisWeek,
    int Streak,
    IReadOnlyList<DayCount> Days,
    IReadOnlyList<BoardMinutes> TimePerBoard,
    IReadOnlyList<AgendaEntry> Agenda,
    IReadOnlyDictionary<Priority, int> OpenByPriority)
{
    public static DashboardStats Compute(Workspace ws, DateTime now, int days = 14)
    {
        var today = now.Date;
        var all = ws.EnumerateActiveCards().ToList();
        var archived = ws.Boards.SelectMany(b => b.ArchivedCards).ToList();
        var everything = all.Select(t => t.Card).Concat(archived).ToList();
        var open = all.Where(t => !t.Card.IsCompleted).ToList();

        var weekStart = today.AddDays(-6);
        int completedThis = everything.Count(c => c.IsCompleted && c.CompletedAt >= weekStart);
        int completedLast = everything.Count(c => c.IsCompleted && c.CompletedAt >= weekStart.AddDays(-7) && c.CompletedAt < weekStart);

        int overdue = open.Count(t => t.Card.DueDate is { } d && Card.EffectiveDue(d) < now);
        int soon = open.Count(t => t.Card.DueDate is { } d && Card.EffectiveDue(d) >= now && d.Date <= today.AddDays(7));

        var series = new List<DayCount>();
        for (int i = days - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            series.Add(new DayCount(
                day,
                everything.Count(c => c.IsCompleted && c.CompletedAt?.Date == day),
                everything.Count(c => c.CreatedAt.Date == day)));
        }

        // Days in a row (ending today, or yesterday if nothing is done yet today) with at least one completion.
        var doneDays = everything.Where(c => c.IsCompleted && c.CompletedAt != null).Select(c => c.CompletedAt!.Value.Date).ToHashSet();
        int streak = 0;
        var cursor = doneDays.Contains(today) ? today : today.AddDays(-1);
        while (doneDays.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        var perBoard = new List<BoardMinutes>();
        double focus = 0;
        foreach (var b in ws.Boards)
        {
            var minutes = b.Lists.SelectMany(l => l.Cards).Concat(b.ArchivedCards)
                .SelectMany(c => c.TimeEntries)
                .Where(t => t.Start >= weekStart)
                .Sum(t => t.Duration.TotalMinutes);
            focus += minutes;
            if (minutes >= 1) perBoard.Add(new BoardMinutes(b, minutes));
        }

        var agenda = open
            .Where(t => t.Card.DueDate is { } d && d.Date <= today.AddDays(7))
            .OrderBy(t => t.Card.DueDate)
            .Take(12)
            .Select(t => new AgendaEntry(t.Board, t.List, t.Card))
            .ToList();

        var byPriority = Enum.GetValues<Priority>().ToDictionary(p => p, p => open.Count(t => t.Card.Priority == p));

        return new DashboardStats(open.Count, completedThis, completedLast, overdue, soon, focus, streak, series,
            perBoard.OrderByDescending(p => p.Minutes).ToList(), agenda, byPriority);
    }
}

/// <summary>Size and progress of every section of a project (pure, so it can be tested).</summary>
public sealed record ProjectStats(
    int Boards, int Lists, int Cards, int CardsDone,
    int Storyboards, int Shots, int ShotsDone, int ShotsWithPicture, int VoiceClips, double RuntimeSeconds,
    int Canvases, int Shapes, int Frames, int Connections,
    int Pages, int Words, int Todos, int TodosDone, int PagesEditedThisWeek)
{
    public static readonly string[] DoneShotStatuses = ["Done", "Approved"];

    public static ProjectStats Compute(Workspace ws, DateTime now)
    {
        var cards = ws.Boards.SelectMany(b => b.Lists).SelectMany(l => l.Cards).ToList();
        var shots = ws.Storyboards.SelectMany(s => s.Shots).ToList();
        var nodes = ws.Canvases.SelectMany(c => c.Nodes).ToList();
        var blocks = ws.Notes.SelectMany(n => n.Blocks).ToList();
        var todos = blocks.Where(b => b.Type == BlockType.Todo).ToList();

        return new ProjectStats(
            ws.Boards.Count,
            ws.Boards.Sum(b => b.Lists.Count),
            cards.Count,
            cards.Count(c => c.IsCompleted),
            ws.Storyboards.Count,
            shots.Count,
            shots.Count(s => DoneShotStatuses.Contains(s.Status)),
            shots.Count(s => s.ImagePath != null),
            shots.Count(s => s.HasVoice),
            shots.Sum(s => s.DurationSeconds),
            ws.Canvases.Count,
            nodes.Count(n => n.Shape != NodeShape.Frame),
            nodes.Count(n => n.Shape == NodeShape.Frame),
            ws.Canvases.Sum(c => c.Edges.Count),
            ws.Notes.Count,
            blocks.Sum(b => CountWords(b.Text)),
            todos.Count,
            todos.Count(t => t.IsChecked),
            ws.Notes.Count(n => n.UpdatedAt >= now.Date.AddDays(-6)));
    }

    public static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>"1:05" style runtime, with hours when needed.</summary>
    public static string FormatRuntime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}
