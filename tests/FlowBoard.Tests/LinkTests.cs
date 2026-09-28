using System.Text.Json;
using FlowBoard.Models;
using FlowBoard.Services;
using Xunit;

namespace FlowBoard.Tests;

public class LinkTests
{
    private static (Workspace Ws, Storyboard Sb, Shot Shot) Sample()
    {
        var ws = new Workspace();
        var sb = new Storyboard { Name = "Trailer" };
        sb.Shots.Add(new Shot { Title = "Opening" });
        var shot = new Shot { Title = "Cartwheel", ShotType = "Wide" };
        sb.Shots.Add(shot);
        sb.Shots.Add(new Shot());
        ws.Storyboards.Add(sb);
        var board = new Board { Name = "Launch" };
        var list = new BoardList { Name = "To do" };
        list.Cards.Add(new Card { Title = "Cartwheel rehearsal" });
        board.Lists.Add(list);
        ws.Boards.Add(board);
        ws.Canvases.Add(new CanvasDoc { Name = "Cart flow" });
        ws.Notes.Add(new NotePage { Title = "Cast notes" });
        return (ws, sb, shot);
    }

    [Fact]
    public void Search_finds_individual_shots_and_ranks_prefix_matches_first()
    {
        var (ws, _, shot) = Sample();
        var hits = LinkResolver.Search(ws, "cartw", LinkResolver.All);
        Assert.Equal(shot.Id, hits[0].Id);
        Assert.Equal(LinkTarget.Shot, hits[0].Kind);
        Assert.Contains(hits, h => h.Kind == LinkTarget.Card);
    }

    [Fact]
    public void Search_respects_kinds_and_exclusions()
    {
        var (ws, sb, _) = Sample();
        var hits = LinkResolver.Search(ws, string.Empty, [LinkTarget.Storyboard, LinkTarget.Note], exclude: [sb.Id]);
        Assert.All(hits, h => Assert.Equal(LinkTarget.Note, h.Kind));
    }

    [Fact]
    public void Untitled_shots_get_a_numbered_name()
    {
        var (ws, sb, _) = Sample();
        Assert.Equal("Shot 3", LinkResolver.TitleOf(ws, LinkTarget.Shot, sb.Shots[2].Id));
        sb.Mode = StoryboardMode.Animation;
        Assert.Equal("Frame 3", LinkResolver.TitleOf(ws, LinkTarget.Shot, sb.Shots[2].Id));
    }

    [Fact]
    public void Describe_a_shot_and_a_missing_item()
    {
        var (ws, _, shot) = Sample();
        var p = LinkResolver.Describe(ws, LinkTarget.Shot, shot.Id);
        Assert.Equal("Cartwheel", p.Title);
        Assert.Contains("Shot 2 in Trailer", p.Subtitle);
        Assert.False(p.IsMissing);

        var gone = LinkResolver.Describe(ws, LinkTarget.Board, Guid.NewGuid(), "Old board");
        Assert.True(gone.IsMissing);
        Assert.Equal("Old board", gone.Title);
    }

    [Fact]
    public void Inline_links_survive_split_and_serialization()
    {
        var id = Guid.NewGuid();
        var spans = new List<TextSpan>
        {
            new() { Text = "See " },
            new() { Text = "Cartwheel", LinkKind = LinkTarget.Shot, LinkId = id },
            new() { Text = " today" },
        };
        var (left, right) = RichText.Split(spans, 7);
        Assert.Equal(id, left[^1].LinkId);
        Assert.Equal(id, right[0].LinkId);

        var b = new NoteBlock();
        b.SetSpans(spans);
        var back = JsonSerializer.Deserialize<NoteBlock>(JsonSerializer.Serialize(b, Json.Options), Json.Options)!;
        Assert.Equal(LinkTarget.Shot, back.GetSpans()[1].LinkKind);
        Assert.Equal("See [Cartwheel] today", RichText.ToMarkdown(back.GetSpans()));
    }

    [Fact]
    public void Bent_lines_curve_through_the_handle()
    {
        var shape = CanvasGeometry.Route(new Box(0, 0, 0, 0), new Box(100, 0, 0, 0), EdgeStyle.Straight, arrow: true, bendX: 0, bendY: 40);
        Assert.StartsWith("M 0,0 Q 50,80 100,0", shape.Path);
        Assert.Equal((50.0, 40.0), (shape.LabelX, shape.LabelY));
    }

    [Fact]
    public void Document_style_defaults_and_round_trip()
    {
        var page = new NotePage { Theme = "Plum", Background = "gradient:#111111,#222222", BackgroundDim = 0.4 };
        Assert.True(page.HasWallpaper);
        var back = JsonSerializer.Deserialize<NotePage>(JsonSerializer.Serialize(page, Json.Options), Json.Options)!;
        Assert.Equal("Plum", back.Theme);
        Assert.Equal(0.4, back.BackgroundDim);
        Assert.False(new CanvasDoc().HasWallpaper);
    }

    [Fact]
    public void Section_membership_is_saved()
    {
        var frameId = Guid.NewGuid();
        var node = new CanvasNode { FrameId = frameId, Locked = true, Shape = NodeShape.Circle };
        var back = JsonSerializer.Deserialize<CanvasNode>(JsonSerializer.Serialize(node, Json.Options), Json.Options)!;
        Assert.Equal(frameId, back.FrameId);
        Assert.True(back.Locked);
        Assert.Equal(NodeShape.Circle, back.Shape);
    }
}
