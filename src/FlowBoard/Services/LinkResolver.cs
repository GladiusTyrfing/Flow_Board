using FlowBoard.Models;

namespace FlowBoard.Services;

/// <summary>A search hit for linking.</summary>
public sealed record LinkHit(LinkTarget Kind, Guid Id, string Title, string Subtitle, string Icon, int Score);

/// <summary>Finds and describes anything that can be linked: cards, boards, storyboards, shots, canvases and pages.</summary>
public static class LinkResolver
{
    public static readonly LinkTarget[] All =
        [LinkTarget.Card, LinkTarget.Board, LinkTarget.Storyboard, LinkTarget.Shot, LinkTarget.Canvas, LinkTarget.Note];

    public static string ShotTitle(Storyboard sb, Shot shot)
    {
        var word = sb.Mode == StoryboardMode.Animation ? "Frame" : "Shot";
        var n = sb.Shots.IndexOf(shot) + 1;
        return string.IsNullOrWhiteSpace(shot.Title) ? $"{word} {n}" : shot.Title;
    }

    public static Shot? FindShot(Workspace ws, Guid id, out Storyboard? storyboard)
    {
        foreach (var sb in ws.Storyboards)
        {
            var shot = sb.Shots.FirstOrDefault(s => s.Id == id);
            if (shot != null)
            {
                storyboard = sb;
                return shot;
            }
        }

        storyboard = null;
        return null;
    }

    /// <summary>Current title of a link target (null when it was deleted).</summary>
    public static string? TitleOf(Workspace ws, LinkTarget kind, Guid id) => kind switch
    {
        LinkTarget.Card => ws.FindCard(id, out _, out _)?.Title,
        LinkTarget.Board => ws.Boards.FirstOrDefault(b => b.Id == id)?.Name,
        LinkTarget.Storyboard => ws.Storyboards.FirstOrDefault(s => s.Id == id)?.Name,
        LinkTarget.Shot => FindShot(ws, id, out var sb) is { } shot && sb != null ? ShotTitle(sb, shot) : null,
        LinkTarget.Canvas => ws.Canvases.FirstOrDefault(c => c.Id == id)?.Name,
        LinkTarget.Note => ws.Notes.FirstOrDefault(n => n.Id == id)?.Title,
        _ => null,
    };

    /// <summary>Preview card content for a link target.</summary>
    public static LinkPreview Describe(Workspace ws, LinkTarget kind, Guid id, string fallbackTitle = "")
    {
        switch (kind)
        {
            case LinkTarget.Board when ws.Boards.FirstOrDefault(x => x.Id == id) is { } board:
                return new LinkPreview
                {
                    Title = board.Name, Icon = "Board24", Background = board.Background,
                    Subtitle = $"Board · {board.Lists.Count} lists · {board.ActiveCardCount} cards · {board.AllActiveCards.Count(c => c.IsCompleted)} done",
                };
            case LinkTarget.Card when ws.FindCard(id, out var cb, out var cl) is { } card:
                var due = card.DueDate is { } d ? $" · due {Card.FormatDate(d)}" : string.Empty;
                return new LinkPreview
                {
                    Title = card.Title, Icon = card.IsCompleted ? "CheckmarkCircle24" : "TaskListLtr24",
                    Subtitle = $"Card · {cb?.Name} › {cl?.Name ?? "archived"}{due}",
                    Images = card.CoverImagePath is { } cover ? [cover] : [],
                };
            case LinkTarget.Storyboard when ws.Storyboards.FirstOrDefault(x => x.Id == id) is { } sb:
                return new LinkPreview
                {
                    Title = sb.Name, Icon = "VideoClip24",
                    Subtitle = $"{sb.Mode} storyboard · {sb.Shots.Count} {(sb.Mode == StoryboardMode.Animation ? "frames" : "shots")}",
                    Images = sb.Shots.Where(x => x.ImageFullPath != null).Select(x => x.ImageFullPath!).Take(4).ToList(),
                };
            case LinkTarget.Shot when FindShot(ws, id, out var owner) is { } shot && owner != null:
                var details = owner.Mode == StoryboardMode.Animation
                    ? $"{shot.DurationSeconds:0.#}s · {shot.Transition}"
                    : $"{shot.ShotType} · {shot.Angle}";
                return new LinkPreview
                {
                    Title = ShotTitle(owner, shot), Icon = "SlideLayout24",
                    Subtitle = $"{(owner.Mode == StoryboardMode.Animation ? "Frame" : "Shot")} {owner.Shots.IndexOf(shot) + 1} in {owner.Name} · {details}",
                    Images = shot.ImageFullPath is { } img ? [img] : [],
                };
            case LinkTarget.Canvas when ws.Canvases.FirstOrDefault(x => x.Id == id) is { } cv:
                return new LinkPreview
                {
                    Title = cv.Name, Icon = "Flowchart24",
                    Subtitle = $"Canvas · {cv.Nodes.Count} shapes · {cv.Edges.Count} connectors",
                    Images = cv.Nodes.Where(n => n.ImageFullPath != null).Select(n => n.ImageFullPath!).Take(4).ToList(),
                };
            case LinkTarget.Note when ws.Notes.FirstOrDefault(x => x.Id == id) is { } note:
                return new LinkPreview
                {
                    Title = note.Title, Icon = note.Icon,
                    Subtitle = $"Page · edited {note.UpdatedAt:d MMM}",
                    Images = note.CoverFullPath is { } c ? [c] : [],
                };
            default:
                return new LinkPreview { Title = fallbackTitle, Subtitle = "This item was deleted", Icon = "LinkDismiss24", IsMissing = true };
        }
    }

    /// <summary>Fuzzy search over everything linkable (best matches first).</summary>
    public static List<LinkHit> Search(Workspace ws, string query, IEnumerable<LinkTarget> kinds, ICollection<Guid>? exclude = null, Board? current = null, int limit = 80)
    {
        var q = query.Trim();
        var wanted = kinds.ToHashSet();
        var items = new List<LinkHit>();

        void Add(LinkTarget kind, Guid id, string title, string sub, string icon, int bonus)
        {
            if (!wanted.Contains(kind) || exclude?.Contains(id) == true) return;
            var s = Score(title, q);
            if (q.Length > 0 && s <= 0) return;
            items.Add(new LinkHit(kind, id, title, sub, icon, s + bonus));
        }

        foreach (var (board, list, card) in ws.EnumerateActiveCards())
            Add(LinkTarget.Card, card.Id, card.Title, $"{board.Name} › {list.Name}", card.IsCompleted ? "CheckmarkCircle24" : "TaskListLtr24", board == current ? 6 : 0);
        foreach (var b in ws.Boards) Add(LinkTarget.Board, b.Id, b.Name, $"Board · {b.ActiveCardCount} cards", "Board24", 4);
        foreach (var sb in ws.Storyboards)
        {
            Add(LinkTarget.Storyboard, sb.Id, sb.Name, $"Storyboard · {sb.Shots.Count} shots", "VideoClip24", 3);
            foreach (var shot in sb.Shots)
                Add(LinkTarget.Shot, shot.Id, ShotTitle(sb, shot), $"{(sb.Mode == StoryboardMode.Animation ? "Frame" : "Shot")} {sb.Shots.IndexOf(shot) + 1} · {sb.Name}", "SlideLayout24", 2);
        }

        foreach (var c in ws.Canvases) Add(LinkTarget.Canvas, c.Id, c.Name, $"Canvas · {c.Nodes.Count} shapes", "Flowchart24", 3);
        foreach (var n in ws.Notes) Add(LinkTarget.Note, n.Id, n.Title, "Page", n.Icon, 3);

        return items.OrderByDescending(i => i.Score).Take(limit).ToList();
    }

    /// <summary>Simple fuzzy score: substring beats in-order character match; 0 = no match.</summary>
    public static int Score(string text, string q)
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
}
