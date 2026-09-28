using System.Text.Json;
using FlowBoard.Models;

namespace FlowBoard.Services;

public static class TemplateService
{
    public static IReadOnlyList<BoardTemplate> BuiltIn { get; } =
    [
        new()
        {
            Name = "Basic Kanban", Description = "To Do, Doing, Done — the classic.", Icon = "Board24", IsBuiltIn = true,
            Background = "gradient:#0C66E4,#9F6FEF", Lists = ["To Do", "Doing", "Done"],
        },
        new()
        {
            Name = "Blank board", Description = "Start from scratch.", Icon = "Square24", IsBuiltIn = true,
            Background = "gradient:#172B4D,#44546F", Lists = [],
        },
        new()
        {
            Name = "Project", Description = "Backlog through review and release.", Icon = "Rocket24", IsBuiltIn = true,
            Background = "gradient:#0B3D91,#37B4C3", Lists = ["Backlog", "To Do", "In Progress", "Review", "Done"],
            Labels = [new("Feature", "#579DFF"), new("Improvement", "#4BCE97"), new("Research", "#9F8FEF"), new("Blocked", "#F87168")],
        },
        new()
        {
            Name = "Bug tracker", Description = "Triage, fix and verify bugs.", Icon = "Bug24", IsBuiltIn = true,
            Background = "gradient:#AE2E24,#6E5DC6", Lists = ["Reported", "Triaged", "Fixing", "Testing", "Closed"],
            Labels = [new("Critical", "#F87168"), new("Major", "#FEA362"), new("Minor", "#F5CD47"), new("UI", "#579DFF"), new("Backend", "#9F8FEF"), new("Needs info", "#8590A2")],
        },
        new()
        {
            Name = "Weekly planner", Description = "One list per day of the week.", Icon = "CalendarLtr24", IsBuiltIn = true,
            Background = "gradient:#1F845A,#94C748", Lists = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Weekend"],
            Labels = [new("Work", "#579DFF"), new("Personal", "#4BCE97"), new("Health", "#F87168"), new("Errand", "#F5CD47")],
        },
        new()
        {
            Name = "Content pipeline", Description = "Ideas to published.", Icon = "DocumentEdit24", IsBuiltIn = true,
            Background = "gradient:#943D73,#FEA362", Lists = ["Ideas", "Outlining", "Writing", "Editing", "Published"],
            Labels = [new("Blog", "#579DFF"), new("Video", "#F87168"), new("Social", "#E774BB"), new("Newsletter", "#F5CD47")],
        },
        new()
        {
            Name = "Personal", Description = "Inbox, this week, today.", Icon = "Person24", IsBuiltIn = true,
            Background = "gradient:#227D9B,#6CC3E0", Lists = ["Inbox", "This week", "Today", "Done"],
        },
    ];

    public static Board Create(BoardTemplate template, string name, string? background = null)
    {
        Board board;
        if (template.BoardJson != null)
        {
            var source = JsonSerializer.Deserialize<Board>(template.BoardJson, Json.Options) ?? new Board();
            source.Hydrate();
            board = Json.CloneWithNewIds(source, includeCards: true);
            foreach (var card in board.AllActiveCards)
            {
                // Template attachments point at files of the original card; drop them so deleting one card never affects another.
                card.Attachments.Clear();
                card.CoverAttachmentId = null;
                card.Comments.Clear();
                card.TimeEntries.Clear();
                card.CreatedAt = DateTime.Now;
            }
        }
        else
        {
            board = new Board();
            foreach (var l in template.Lists) board.Lists.Add(new BoardList { Name = l, IsDoneList = IsDoneName(l) });
            if (template.Labels.Count > 0)
                foreach (var l in template.Labels) board.Labels.Add(new Label { Name = l.Name, Color = l.Color });
            else
                foreach (var l in Board.DefaultLabels()) board.Labels.Add(l);
        }

        board.Name = string.IsNullOrWhiteSpace(name) ? template.Name : name.Trim();
        board.Background = background ?? template.Background;
        board.LastOpened = DateTime.Now;
        board.Hydrate();
        return board;
    }

    public static BoardTemplate SaveAsTemplate(Board board, string name, bool includeCards)
    {
        var copy = Json.CloneWithNewIds(board, includeCards);
        if (copy.Background.StartsWith("image:")) copy.Background = "gradient:#0C66E4,#9F6FEF";
        return new BoardTemplate
        {
            Name = name,
            Description = $"{copy.Lists.Count} lists{(includeCards ? $", {copy.ActiveCardCount} cards" : string.Empty)}",
            Icon = "Bookmark24",
            Background = copy.Background,
            Lists = copy.Lists.Select(l => l.Name).ToList(),
            BoardJson = Json.SerializeBoard(copy),
        };
    }

    private static bool IsDoneName(string name) =>
        name.Equals("Done", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Closed", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Published", StringComparison.OrdinalIgnoreCase);

    /// <summary>A small tour board created on first launch.</summary>
    public static Board CreateWelcomeBoard(string author)
    {
        var board = Create(BuiltIn[0], "Welcome to FlowBoard");
        var labels = board.Labels;
        labels[0].Name = "Tip";
        labels[5].Name = "Feature";
        labels[3].Name = "Important";

        Card Make(string title, string desc, params Label[] ls)
        {
            var c = new Card { Title = title, Description = desc, Board = board };
            foreach (var l in ls) c.LabelIds.Add(l.Id);
            c.AddActivity("created this card", author);
            return c;
        }

        var todo = board.Lists[0];
        var doing = board.Lists[1];
        var done = board.Lists[2];

        todo.Cards.Add(Make("👋 Click a card to open it",
            "Cards hold a description, checklists, labels, dates with reminders, file/image attachments, voice notes, comments and time tracking.",
            labels[0]));
        todo.Cards.Add(Make("Drag cards between lists",
            "Grab a card and drop it anywhere. Lists can be dragged too — grab a list by its header.\n\nTip: drag the empty board background to scroll sideways.",
            labels[0]));

        var shortcuts = Make("Keyboard shortcuts",
            "Ctrl+Alt+Space  Quick add from ANY app (global)\nCtrl+Alt+F  Show / hide FlowBoard (global)\nCtrl+K or /  Command palette & search everywhere\nCtrl+F  Filter this board\nN  New card · Ctrl+Shift+N  New board\nCtrl+Z / Ctrl+Y  Undo / Redo\nCtrl+Tab  Next board · Alt+1..9  Jump to board\nCtrl+1/2/3  Board / Table / Calendar view\n\nHover a card and press: X complete · C archive · 1-9 labels · P priority · T timer · Alt+arrows move\n\nF1  Full shortcut cheat sheet",
            labels[5]);
        todo.Cards.Add(shortcuts);

        var voice = Make("Record a voice note 🎙️",
            "Open this card and press \"Voice\" to record from your microphone. Paste images with Ctrl+V or drop files onto an open card.",
            labels[5]);
        voice.DueDate = DateTime.Today.AddDays(2);
        voice.ReminderMinutes = 15;
        doing.Cards.Add(voice);

        var focus = Make("Try a focus session 🍅",
            "Open a card and press \"Focus\" to start a Pomodoro linked to it. Finished focus time is logged on the card automatically.",
            labels[5], labels[3]);
        var cl = new Checklist { Title = "Get started" };
        cl.Items.Add(new ChecklistItem { Text = "Create your own board", IsDone = false });
        cl.Items.Add(new ChecklistItem { Text = "Pick a background", IsDone = false });
        cl.Items.Add(new ChecklistItem { Text = "Open FlowBoard", IsDone = true });
        focus.Checklists.Add(cl);
        doing.Cards.Add(focus);

        var finished = Make("Install FlowBoard", "Everything is stored locally on this PC.", labels[0]);
        finished.IsCompleted = true;
        done.Cards.Add(finished);

        board.Hydrate();
        return board;
    }
}
