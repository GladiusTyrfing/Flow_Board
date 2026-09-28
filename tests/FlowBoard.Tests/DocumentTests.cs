using System.Text.Json;
using FlowBoard.Models;
using FlowBoard.Services;
using Xunit;

namespace FlowBoard.Tests;

public class CanvasGeometryTests
{
    [Fact]
    public void Side_by_side_boxes_connect_right_to_left()
    {
        var (x1, y1, nx1, _, x2, y2, nx2, _) = CanvasGeometry.Anchors(new Box(0, 0, 100, 50), new Box(300, 10, 100, 50));
        Assert.Equal(100, x1);
        Assert.Equal(25, y1);
        Assert.Equal(1, nx1);
        Assert.Equal(300, x2);
        Assert.Equal(35, y2);
        Assert.Equal(-1, nx2);
    }

    [Fact]
    public void Stacked_boxes_connect_bottom_to_top()
    {
        var (x1, y1, _, ny1, x2, y2, _, ny2) = CanvasGeometry.Anchors(new Box(0, 0, 100, 50), new Box(0, 200, 100, 50));
        Assert.Equal((50.0, 50.0, 1.0), (x1, y1, ny1));
        Assert.Equal((50.0, 200.0, -1.0), (x2, y2, ny2));
    }

    [Theory]
    [InlineData(EdgeStyle.Curved, "C ")]
    [InlineData(EdgeStyle.Straight, "L ")]
    [InlineData(EdgeStyle.Elbow, "L ")]
    public void Routes_use_invariant_numbers_and_the_right_segments(EdgeStyle style, string segment)
    {
        var prev = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE"); // decimal comma must not leak in
        try
        {
            var shape = CanvasGeometry.Route(new Box(0.5, 0, 100, 51), new Box(300, 0, 100, 50), style, arrow: true);
            Assert.StartsWith("M 100.5,25.5", shape.Path);
            Assert.Contains(segment, shape.Path);
            Assert.StartsWith("M 300,25", shape.Arrow);
            Assert.EndsWith("Z", shape.Arrow);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    [Fact]
    public void No_arrow_when_disabled_and_label_sits_between_the_boxes()
    {
        var shape = CanvasGeometry.Route(new Box(0, 0, 100, 50), new Box(300, 0, 100, 50), EdgeStyle.Straight, arrow: false);
        Assert.Equal(string.Empty, shape.Arrow);
        Assert.Equal(200, shape.LabelX);
        Assert.Equal(25, shape.LabelY);
    }

    [Fact]
    public void Snap_rounds_to_grid() => Assert.Equal(40, CanvasGeometry.Snap(47, 20));

    [Fact]
    public void Layered_layout_places_children_right_of_parents()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();
        var pos = CanvasLayout.Layered(
            [(a, 100, 50), (b, 100, 50), (c, 100, 50), (d, 100, 50)],
            [(a, b), (a, c), (b, d)]);
        Assert.True(pos[b].X > pos[a].X);
        Assert.Equal(pos[b].X, pos[c].X);
        Assert.NotEqual(pos[b].Y, pos[c].Y);
        Assert.True(pos[d].X > pos[b].X);
    }

    [Fact]
    public void Layered_layout_survives_cycles()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var pos = CanvasLayout.Layered([(a, 100, 50), (b, 100, 50)], [(a, b), (b, a)]);
        Assert.Equal(2, pos.Count);
    }
}

public class NoteMarkdownTests
{
    [Theory]
    [InlineData("# Title", BlockType.Heading1, "Title")]
    [InlineData("## Sub", BlockType.Heading2, "Sub")]
    [InlineData("### Small", BlockType.Heading3, "Small")]
    [InlineData("- item", BlockType.Bullet, "item")]
    [InlineData("* item", BlockType.Bullet, "item")]
    [InlineData("1. first", BlockType.Numbered, "first")]
    [InlineData("[] task", BlockType.Todo, "task")]
    [InlineData("[ ] task", BlockType.Todo, "task")]
    [InlineData("> said", BlockType.Quote, "said")]
    [InlineData("```", BlockType.Code, "")]
    [InlineData("---", BlockType.Divider, "")]
    public void Detects_markdown_shortcuts(string text, BlockType type, string rest)
    {
        var hit = NoteMarkdown.DetectShortcut(text);
        Assert.NotNull(hit);
        Assert.Equal(type, hit!.Value.Type);
        Assert.Equal(rest, hit.Value.Text);
    }

    [Theory]
    [InlineData("#hashtag")]
    [InlineData("-dash")]
    [InlineData("```code")]
    [InlineData("plain text")]
    public void Leaves_normal_text_alone(string text) => Assert.Null(NoteMarkdown.DetectShortcut(text));

    [Fact]
    public void Renumbers_lists_and_restarts_after_a_paragraph()
    {
        var blocks = new List<NoteBlock>
        {
            new() { Type = BlockType.Numbered },
            new() { Type = BlockType.Numbered },
            new() { Type = BlockType.Bullet, Indent = 1 },
            new() { Type = BlockType.Numbered },
            new() { Type = BlockType.Paragraph },
            new() { Type = BlockType.Numbered },
        };
        NoteMarkdown.Renumber(blocks);
        Assert.Equal([1, 2, 3, 1], new[] { blocks[0].Number, blocks[1].Number, blocks[3].Number, blocks[5].Number });
    }

    [Fact]
    public void Exports_markdown()
    {
        var page = new NotePage { Title = "Plan" };
        page.Blocks.Add(new NoteBlock { Type = BlockType.Heading1, Text = "Goals" });
        page.Blocks.Add(new NoteBlock { Type = BlockType.Todo, Text = "Ship", IsChecked = true });
        page.Blocks.Add(new NoteBlock { Type = BlockType.Todo, Text = "Celebrate" });
        page.Blocks.Add(new NoteBlock { Type = BlockType.Divider });
        var md = NoteMarkdown.ToMarkdown(page);
        Assert.StartsWith("# Plan", md);
        Assert.Contains("## Goals", md);
        Assert.Contains("- [x] Ship" + Environment.NewLine + "- [ ] Celebrate", md);
        Assert.Contains("---", md);
    }
}

public class DashboardAndDocumentTests
{
    [Fact]
    public void Dashboard_counts_completions_overdue_and_streak()
    {
        var now = new DateTime(2026, 3, 10, 12, 0, 0);
        var ws = new Workspace();
        var board = new Board { Name = "B" };
        var todo = new BoardList { Name = "To do" };
        board.Lists.Add(todo);
        ws.Boards.Add(board);

        Card Done(int daysAgo)
        {
            var c = new Card { Title = "d" + daysAgo, IsCompleted = true };
            c.CompletedAt = now.Date.AddDays(-daysAgo).AddHours(9);
            return c;
        }

        todo.Cards.Add(Done(0));
        todo.Cards.Add(Done(1));
        todo.Cards.Add(Done(2));
        todo.Cards.Add(Done(5));
        todo.Cards.Add(new Card { Title = "late", DueDate = now.AddDays(-1) });
        todo.Cards.Add(new Card { Title = "soon", DueDate = now.AddDays(2), Priority = Priority.High });

        var s = DashboardStats.Compute(ws, now);
        Assert.Equal(2, s.OpenCards);
        Assert.Equal(4, s.CompletedThisWeek);
        Assert.Equal(1, s.Overdue);
        Assert.Equal(1, s.DueSoon);
        Assert.Equal(3, s.Streak);
        Assert.Equal(14, s.Days.Count);
        Assert.Equal(1, s.Days[^1].Completed);
        Assert.Equal(1, s.OpenByPriority[Priority.High]);
        Assert.Equal("late", s.Agenda[0].Card.Title);
    }

    [Fact]
    public void Documents_round_trip_without_runtime_state()
    {
        var ws = new Workspace();
        var sb = new Storyboard { Name = "Short film", Mode = StoryboardMode.Animation };
        var shot = new Shot { Title = "Opening", DurationSeconds = 2.5, NewTag = "typing" };
        shot.Tags.Add(new TextItem { Text = "night" });
        shot.ShotList.Add(new ChecklistItem { Text = "Wide" });
        sb.Shots.Add(shot);
        ws.Storyboards.Add(sb);

        var canvas = new CanvasDoc { Name = "Flow" };
        var n1 = new CanvasNode { Text = "Start", IsSelected = true };
        var n2 = new CanvasNode { Text = "End", X = 300 };
        canvas.Nodes.Add(n1);
        canvas.Nodes.Add(n2);
        canvas.Edges.Add(new CanvasEdge { FromId = n1.Id, ToId = n2.Id, Label = "go", PathData = "M 0,0" });
        ws.Canvases.Add(canvas);

        var note = new NotePage { Title = "Ideas" };
        note.Blocks.Add(new NoteBlock { Type = BlockType.Todo, Text = "Try it", IsChecked = true, Number = 7 });
        ws.Notes.Add(note);

        var json = JsonSerializer.Serialize(ws, Json.Options);
        Assert.DoesNotContain("PathData", json);
        Assert.DoesNotContain("IsSelected", json);
        Assert.DoesNotContain("NewTag", json);

        var back = JsonSerializer.Deserialize<Workspace>(json, Json.Options)!;
        var s2 = Assert.Single(back.Storyboards);
        Assert.Equal(StoryboardMode.Animation, s2.Mode);
        Assert.Equal(2.5, s2.Shots[0].DurationSeconds);
        Assert.Equal("night", s2.Shots[0].Tags[0].Text);
        var c2 = Assert.Single(back.Canvases);
        Assert.Equal(2, c2.Nodes.Count);
        Assert.Equal("go", c2.Edges[0].Label);
        Assert.False(c2.Nodes[0].IsSelected);
        var p2 = Assert.Single(back.Notes);
        Assert.True(p2.Blocks[0].IsChecked);
        Assert.Equal(BlockType.Todo, p2.Blocks[0].Type);
    }

    [Fact]
    public void Old_data_files_without_documents_still_load()
    {
        var back = JsonSerializer.Deserialize<Workspace>("""{"Version":1,"Boards":[]}""", Json.Options)!;
        Assert.Empty(back.Storyboards);
        Assert.Empty(back.Canvases);
        Assert.Empty(back.Notes);
    }

    [Fact]
    public void Duplicated_documents_get_a_new_id()
    {
        var sb = new Storyboard { Name = "A" };
        sb.Shots.Add(new Shot { Title = "1" });
        var copy = Json.CloneDocument(sb);
        Assert.NotEqual(sb.Id, copy.Id);
        Assert.Equal("1", copy.Shots[0].Title);
        Assert.NotSame(sb.Shots[0], copy.Shots[0]);
    }
}
