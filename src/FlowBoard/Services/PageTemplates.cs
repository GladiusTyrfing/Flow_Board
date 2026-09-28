using FlowBoard.Models;

namespace FlowBoard.Services;

/// <summary>Starter content for new note pages.</summary>
public static class PageTemplates
{
    public static List<NoteBlock> Build(string name, DateTime now)
    {
        NoteBlock B(BlockType t, string text = "") => new() { Type = t, Text = text };
        NoteBlock Bold(BlockType t, string label, string rest = "")
        {
            var b = new NoteBlock { Type = t };
            b.SetSpans([new TextSpan { Text = label, Bold = true }, new TextSpan { Text = rest }]);
            return b;
        }

        return name switch
        {
            "Meeting notes" =>
            [
                Bold(BlockType.Paragraph, "Date: ", now.ToString("dddd d MMMM yyyy")),
                Bold(BlockType.Paragraph, "Attendees: "),
                B(BlockType.Heading2, "Agenda"),
                B(BlockType.Numbered),
                B(BlockType.Heading2, "Notes"),
                B(BlockType.Bullet),
                B(BlockType.Heading2, "Decisions"),
                B(BlockType.Bullet),
                B(BlockType.Heading2, "Action items"),
                B(BlockType.Todo),
            ],
            "Project brief" =>
            [
                B(BlockType.Callout, "One sentence: what are we making and why?"),
                B(BlockType.Heading2, "Goals"),
                B(BlockType.Bullet),
                B(BlockType.Heading2, "Audience"),
                B(BlockType.Paragraph),
                B(BlockType.Heading2, "Scope"),
                Bold(BlockType.Bullet, "In: "),
                Bold(BlockType.Bullet, "Out: "),
                B(BlockType.Heading2, "Milestones"),
                B(BlockType.Todo),
                B(BlockType.Heading2, "Links"),
                B(BlockType.Paragraph, "Type @ to link a board, storyboard, canvas or page."),
            ],
            "Script / screenplay" =>
            [
                Bold(BlockType.Paragraph, "Logline: "),
                B(BlockType.Divider),
                B(BlockType.Heading2, "INT. LOCATION — DAY"),
                B(BlockType.Paragraph, "Action: describe what we see."),
                Bold(BlockType.Quote, "CHARACTER: ", "Dialogue goes here."),
                B(BlockType.Heading2, "EXT. LOCATION — NIGHT"),
                B(BlockType.Paragraph),
            ],
            "Shot list" =>
            [
                Bold(BlockType.Paragraph, "Shoot day: ", now.ToString("d MMM yyyy")),
                Bold(BlockType.Paragraph, "Location: "),
                B(BlockType.Heading2, "Shots"),
                B(BlockType.Todo, "1 — Wide establishing"),
                B(BlockType.Todo, "2 — Medium"),
                B(BlockType.Todo, "3 — Close-up"),
                B(BlockType.Heading2, "Gear"),
                B(BlockType.Bullet),
            ],
            "To-do list" =>
            [
                B(BlockType.Heading2, "Today"),
                B(BlockType.Todo),
                B(BlockType.Heading2, "This week"),
                B(BlockType.Todo),
                B(BlockType.Heading2, "Later"),
                B(BlockType.Todo),
            ],
            "Journal" =>
            [
                B(BlockType.Heading2, now.ToString("dddd d MMMM")),
                Bold(BlockType.Paragraph, "Mood: "),
                B(BlockType.Heading3, "Grateful for"),
                B(BlockType.Bullet),
                B(BlockType.Heading3, "What happened"),
                B(BlockType.Paragraph),
                B(BlockType.Heading3, "Tomorrow"),
                B(BlockType.Todo),
            ],
            _ => [B(BlockType.Paragraph)],
        };
    }
}
