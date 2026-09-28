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

public class ProjectPackageTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "fb-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Flowboard_files_are_packages_that_round_trip()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "Film.flowboard");
        var ws = new Workspace { MediaFolder = "Film files" };
        ws.Boards.Add(new Board { Name = "Shots" });
        ProjectPackage.Write(file, System.Text.Json.JsonSerializer.Serialize(ws, Json.Options));

        Assert.True(ProjectPackage.IsPackage(file));
        Assert.DoesNotContain("\"Shots\"", File.ReadAllText(file)); // compressed, not readable JSON
        var back = ProjectPackage.Read(file);
        Assert.Equal("Shots", back.Boards.Single().Name);
        Assert.Equal("Film files", back.MediaFolder);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Plain_json_projects_still_open()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "data.json");
        var ws = new Workspace();
        ws.Notes.Add(new NotePage { Title = "Old" });
        ProjectPackage.Write(file, System.Text.Json.JsonSerializer.Serialize(ws, Json.Options));
        Assert.False(ProjectPackage.IsPackage(file)); // data.json stays JSON
        Assert.Equal("Old", ProjectPackage.Read(file).Notes.Single().Title);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Media_folder_sits_next_to_the_file_and_survives_renames()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "Renamed.flowboard");
        Assert.Equal(Path.Combine(dir, "Renamed files"), ProjectPackage.MediaDirFor(file, null));
        Directory.CreateDirectory(Path.Combine(dir, "Original files"));
        Assert.Equal(Path.Combine(dir, "Original files"), ProjectPackage.MediaDirFor(file, "Original files"));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Older_project_folders_keep_media_beside_the_file()
    {
        var dir = TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "attachments"));
        Assert.Equal(dir, ProjectPackage.MediaDirFor(Path.Combine(dir, "X.flowboard"), null));
        Assert.Equal(dir, ProjectPackage.MediaDirFor(Path.Combine(dir, "data.json"), null));
        Directory.Delete(dir, true);
    }
}

public class DisplayNameTests
{
    [Fact]
    public void Name_follows_windows_until_customised()
    {
        var s = new AppSettings { DisplayName = "old-pc-name" };
        s.SyncDisplayName("Gladius_Tyrfing");          // never synced before: follows Windows
        Assert.Equal("Gladius_Tyrfing", s.DisplayName);

        s.SyncDisplayName("NewPC");                     // still the Windows name: follows again
        Assert.Equal("NewPC", s.DisplayName);

        s.DisplayName = "Gladius";                      // typed by the user
        s.SyncDisplayName("OtherPC");
        Assert.Equal("Gladius", s.DisplayName);
    }
}

public class LinkSyncTests
{
    [Theory]
    [InlineData(true, "Planned", "Done")]
    [InlineData(true, "Approved", null)]     // already done: leave "Approved" alone
    [InlineData(false, "Done", "In progress")]
    [InlineData(false, "Planned", null)]
    public void Shot_status_follows_its_card(bool cardDone, string current, string? expected) =>
        Assert.Equal(expected, LinkSync.ShotStatusFor(cardDone, current));

    [Fact]
    public void Finds_cards_shots_and_todos_that_belong_together()
    {
        var ws = new Workspace();
        var sb = new Storyboard();
        var shot = new Shot { Title = "Cartwheel" };
        sb.Shots.Add(shot);
        ws.Storyboards.Add(sb);

        var card = new Card { Title = "Film the cartwheel" };
        card.Links.Add(new ItemLink(LinkTarget.Shot, shot.Id));
        var board = new Board();
        var list = new BoardList();
        list.Cards.Add(card);
        board.Lists.Add(list);
        ws.Boards.Add(board);

        var page = new NotePage();
        var todo = new NoteBlock { Type = BlockType.Todo, Text = "Film it", LinkKind = LinkTarget.Card, LinkId = card.Id };
        page.Blocks.Add(todo);
        ws.Notes.Add(page);

        Assert.Same(card, LinkSync.CardsLinkedTo(ws, LinkTarget.Shot, shot.Id).Single());
        Assert.Same(shot, LinkSync.ShotsOf(ws, card).Single());
        Assert.Same(todo, LinkSync.TodosFor(ws, card.Id).Single());
        Assert.True(LinkSync.ContainsShot(ws, shot));
        Assert.False(LinkSync.ContainsShot(ws, new Shot { Id = shot.Id })); // a copy (e.g. an undo snapshot) doesn't count
        Assert.True(LinkSync.ContainsBlock(ws, todo));
    }

    [Fact]
    public void Card_links_survive_saving()
    {
        var card = new Card();
        card.Links.Add(new ItemLink(LinkTarget.Canvas, Guid.NewGuid()));
        var back = System.Text.Json.JsonSerializer.Deserialize<Card>(System.Text.Json.JsonSerializer.Serialize(card, Json.Options), Json.Options)!;
        Assert.Equal(card.Links[0].Id, back.Links.Single().Id);
        Assert.Equal(LinkTarget.Canvas, back.Links[0].Kind);
    }
}
