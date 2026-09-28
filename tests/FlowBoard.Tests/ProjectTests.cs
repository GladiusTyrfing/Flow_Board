using FlowBoard.Models;
using FlowBoard.Services;
using Xunit;

namespace FlowBoard.Tests;

public class ProjectTests
{
    [Fact]
    public void ProjectStats_count_every_section()
    {
        var now = new DateTime(2026, 3, 10, 12, 0, 0);
        var ws = new Workspace();

        var board = new Board { Name = "B" };
        var list = new BoardList { Name = "L" };
        list.Cards.Add(new Card { Title = "a" });
        list.Cards.Add(new Card { Title = "b", IsCompleted = true });
        board.Lists.Add(list);
        ws.Boards.Add(board);

        var sb = new Storyboard { Name = "S" };
        sb.Shots.Add(new Shot { DurationSeconds = 2.5, Status = "Done", ImagePath = "x.png" });
        sb.Shots.Add(new Shot { DurationSeconds = 4, Status = "Planned" });
        sb.Shots.Add(new Shot { DurationSeconds = 1.5, Status = "Approved" });
        ws.Storyboards.Add(sb);

        var canvas = new CanvasDoc { Name = "C" };
        var frame = new CanvasNode { Shape = NodeShape.Frame };
        var a = new CanvasNode { FrameId = frame.Id };
        var b = new CanvasNode { Shape = NodeShape.Circle };
        canvas.Nodes.Add(frame);
        canvas.Nodes.Add(a);
        canvas.Nodes.Add(b);
        canvas.Edges.Add(new CanvasEdge { FromId = a.Id, ToId = b.Id });
        ws.Canvases.Add(canvas);

        var page = new NotePage { UpdatedAt = now.AddDays(-1) };
        page.Blocks.Add(new NoteBlock { Text = "Hello there  world" });
        page.Blocks.Add(new NoteBlock { Type = BlockType.Todo, Text = "do it", IsChecked = true });
        page.Blocks.Add(new NoteBlock { Type = BlockType.Todo, Text = "later" });
        ws.Notes.Add(page);
        ws.Notes.Add(new NotePage { UpdatedAt = now.AddDays(-30) });

        var s = ProjectStats.Compute(ws, now);

        Assert.Equal(1, s.Boards);
        Assert.Equal(1, s.Lists);
        Assert.Equal(2, s.Cards);
        Assert.Equal(1, s.CardsDone);
        Assert.Equal(1, s.Storyboards);
        Assert.Equal(3, s.Shots);
        Assert.Equal(2, s.ShotsDone);
        Assert.Equal(1, s.ShotsWithPicture);
        Assert.Equal(8, s.RuntimeSeconds);
        Assert.Equal(1, s.Canvases);
        Assert.Equal(2, s.Shapes);
        Assert.Equal(1, s.Frames);
        Assert.Equal(1, s.Connections);
        Assert.Equal(2, s.Pages);
        Assert.Equal(6, s.Words);
        Assert.Equal(2, s.Todos);
        Assert.Equal(1, s.TodosDone);
        Assert.Equal(1, s.PagesEditedThisWeek);
    }

    [Fact]
    public void ProjectStats_empty_workspace_is_all_zero()
    {
        var s = ProjectStats.Compute(new Workspace(), DateTime.Now);
        Assert.Equal(0, s.Boards + s.Cards + s.Shots + s.Shapes + s.Pages + s.Words);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65, "1:05")]
    [InlineData(3725, "1:02:05")]
    public void Runtime_is_formatted_like_a_clock(double seconds, string expected) =>
        Assert.Equal(expected, ProjectStats.FormatRuntime(seconds));

    [Fact]
    public void Project_names_come_from_the_file_or_its_folder()
    {
        Assert.Equal("Film", AppPaths.ProjectNameOf(Path.Combine(Path.GetTempPath(), "Film", "Film.flowboard")));
        Assert.Equal("Old", AppPaths.ProjectNameOf(Path.Combine(Path.GetTempPath(), "Old", "data.json")));
        Assert.Equal("My workspace", AppPaths.ProjectNameOf(AppPaths.LegacyDataFile));
    }

    [Theory]
    [InlineData("My: film?", "My_ film_")]
    [InlineData("   ", "Untitled project")]
    [InlineData("Trailing.", "Trailing")]
    public void Safe_names_have_no_invalid_characters(string name, string expected)
    {
        // Invalid characters differ per OS; only check the ones that are invalid everywhere we run.
        var safe = AppPaths.SafeName(name);
        Assert.DoesNotContain(safe, c => Path.GetInvalidFileNameChars().Contains(c));
        if (OperatingSystem.IsWindows() || !name.Contains(':')) Assert.Equal(expected, safe);
    }
}

public class BoxTests
{
    [Fact]
    public void Box_contains_only_boxes_fully_inside()
    {
        var outer = new Box(0, 0, 100, 100);
        Assert.True(outer.Contains(new Box(10, 10, 20, 20)));
        Assert.False(outer.Contains(new Box(90, 10, 20, 20)));
        Assert.True(outer.Intersects(new Box(90, 10, 20, 20)));
    }
}
