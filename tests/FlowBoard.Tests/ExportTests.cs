using System.Text.Json;
using System.Xml.Linq;
using FlowBoard.Models;
using FlowBoard.Services;
using Xunit;

namespace FlowBoard.Tests;

public class ExportTests
{
    private static CanvasDoc SampleCanvas()
    {
        var doc = new CanvasDoc { Name = "Flow & \"plan\"" };
        var frame = new CanvasNode { Shape = NodeShape.Frame, Text = "Act 1", X = 0, Y = 0, Width = 500, Height = 300, Fill = "#8B5CF6" };
        var a = new CanvasNode { Text = "Start <here>", X = 20, Y = 40, FrameId = frame.Id, Shape = NodeShape.Ellipse, Fill = "#10B981" };
        var b = new CanvasNode { Text = "Decide?", X = 260, Y = 40, FrameId = frame.Id, Shape = NodeShape.Diamond, Fill = "#FDE68A" };
        var c = new CanvasNode { Text = "End", X = 700, Y = 40, Shape = NodeShape.Circle, Fill = "#EF4444" };
        foreach (var n in new[] { frame, a, b, c }) doc.Nodes.Add(n);
        doc.Edges.Add(new CanvasEdge { FromId = a.Id, ToId = b.Id, Label = "go" });
        doc.Edges.Add(new CanvasEdge { FromId = b.Id, ToId = c.Id, Dashed = true, Arrow = false });
        doc.Edges.Add(new CanvasEdge { FromId = Guid.Empty, ToId = Guid.Empty, FromX = 0, FromY = 400, ToX = 200, ToY = 420 });
        return doc;
    }

    [Fact]
    public void Canvas_mermaid_has_sections_shapes_and_links()
    {
        var m = Exporters.CanvasToMermaid(SampleCanvas());
        Assert.Contains("flowchart LR", m);
        Assert.Contains("subgraph n1[\"Act 1\"]", m);
        Assert.Contains("([\"Start <here>\"])", m);   // ellipse
        Assert.Contains("{\"Decide?\"}", m);          // diamond
        Assert.Contains("((\"End\"))", m);            // circle
        Assert.Contains("-->|\"go\"|", m);
        Assert.Contains("-.-", m);                    // dashed, no arrow
        Assert.Equal(1, m.Split('\n').Count(l => l.Trim() == "end"));
    }

    [Fact]
    public void Canvas_drawio_is_valid_xml_with_nested_sections()
    {
        var doc = SampleCanvas();
        var xml = XDocument.Parse(Exporters.CanvasToDrawio(doc));
        var cells = xml.Descendants("mxCell").ToList();
        var frameId = doc.Nodes[0].Id.ToString("N");
        var inner = cells.Single(c => (string?)c.Attribute("value") == "Start <here>");
        Assert.Equal(frameId, (string?)inner.Attribute("parent"));
        Assert.Equal("20", inner.Element("mxGeometry")!.Attribute("x")!.Value); // relative to the section
        Assert.Equal(3, cells.Count(c => (string?)c.Attribute("edge") == "1"));
        Assert.Contains(cells, c => c.Descendants("mxPoint").Any(p => (string?)p.Attribute("as") == "sourcePoint"));
    }

    [Fact]
    public void Canvas_svg_is_valid_xml()
    {
        var svg = XDocument.Parse(Exporters.CanvasToSvg(SampleCanvas()));
        Assert.Equal("svg", svg.Root!.Name.LocalName);
        Assert.Contains(svg.Descendants(), e => e.Name.LocalName == "path");
        Assert.Contains(svg.Descendants(), e => e.Name.LocalName == "tspan" && e.Value == "Decide?");
    }

    [Fact]
    public void Canvas_json_lists_nodes_and_edges()
    {
        using var j = JsonDocument.Parse(Exporters.CanvasToJson(SampleCanvas()));
        Assert.Equal(4, j.RootElement.GetProperty("nodes").GetArrayLength());
        Assert.Equal(3, j.RootElement.GetProperty("edges").GetArrayLength());
        Assert.Equal("frame", j.RootElement.GetProperty("nodes")[0].GetProperty("shape").GetString());
    }

    private static Board SampleBoard()
    {
        var board = new Board { Name = "Film" };
        var label = new Label { Name = "Camera", Color = "#3B82F6" };
        board.Labels.Add(label);
        var todo = new BoardList { Name = "To do" };
        var done = new BoardList { Name = "Done", IsDoneList = true };
        var a = new Card { Title = "Rent, \"lens\"", Priority = Priority.High };
        a.LabelIds.Add(label.Id);
        var b = new Card { Title = "Shoot", IsCompleted = true };
        b.BlockedByIds.Add(a.Id);
        todo.Cards.Add(a);
        done.Cards.Add(b);
        board.Lists.Add(todo);
        board.Lists.Add(done);
        return board;
    }

    [Fact]
    public void Board_exports_keep_lists_cards_and_dependencies()
    {
        var board = SampleBoard();
        var csv = Exporters.BoardToCsv(board).Split(Environment.NewLine);
        Assert.StartsWith("List,Title", csv[0]);
        Assert.Contains("\"Rent, \"\"lens\"\"\"", csv[1]);
        Assert.Contains("Camera", csv[1]);

        var mermaid = Exporters.BoardToMermaid(board);
        Assert.Contains("subgraph L1[\"To do\"]", mermaid);
        Assert.Contains("c1 -->|blocks| c2", mermaid);

        var drawio = XDocument.Parse(Exporters.BoardToDrawio(board));
        Assert.Equal(2, drawio.Descendants("mxCell").Count(c => ((string?)c.Attribute("style"))?.StartsWith("swimlane") == true));
        Assert.Single(drawio.Descendants("mxCell"), c => (string?)c.Attribute("edge") == "1");

        using var j = JsonDocument.Parse(Exporters.BoardToJson(board));
        Assert.Equal("Camera", j.RootElement.GetProperty("lists")[0].GetProperty("cards")[0].GetProperty("labels")[0].GetString());

        Assert.Contains("- [x] Shoot", Exporters.BoardToMarkdown(board));
    }

    [Fact]
    public void Pages_export_as_text_and_json()
    {
        var page = new NotePage { Title = "Plan" };
        page.Blocks.Add(new NoteBlock { Type = BlockType.Heading1, Text = "Intro" });
        page.Blocks.Add(new NoteBlock { Type = BlockType.Numbered, Text = "one" });
        page.Blocks.Add(new NoteBlock { Type = BlockType.Numbered, Text = "two" });
        page.Blocks.Add(new NoteBlock { Type = BlockType.Todo, Text = "call", IsChecked = true, Indent = 1 });
        var text = Exporters.PageToText(page);
        Assert.Contains("INTRO", text);
        Assert.Contains("2. two", text);
        Assert.Contains("  [x] call", text);

        using var j = JsonDocument.Parse(Exporters.PageToJson(page));
        var blocks = j.RootElement.GetProperty("blocks");
        Assert.Equal(4, blocks.GetArrayLength());
        Assert.True(blocks[3].GetProperty("checked").GetBoolean());
    }

    [Fact]
    public void Storyboard_exports_a_shot_list()
    {
        var sb = new Storyboard { Name = "Short" };
        sb.Shots.Add(new Shot { Title = "Open", DurationSeconds = 2.5 });
        sb.Shots.Add(new Shot { Title = "Close", Status = "Done" });
        var csv = Exporters.StoryboardToCsv(sb).Split(Environment.NewLine);
        Assert.StartsWith("1,Open", csv[1]);
        using var j = JsonDocument.Parse(Exporters.StoryboardToJson(sb));
        Assert.Equal(5.5, j.RootElement.GetProperty("runtimeSeconds").GetDouble());
    }

    [Theory]
    [InlineData("#FFFFFF", "#18181B")]
    [InlineData("#111827", "#FFFFFF")]
    public void Text_color_contrasts_with_the_fill(string fill, string expected) =>
        Assert.Equal(expected, Exporters.ContrastText(fill));
}
