using System.Text.Json;
using FlowBoard.Models;
using FlowBoard.Services;
using Xunit;

namespace FlowBoard.Tests;

public class RichTextTests
{
    private static List<TextSpan> Sample() =>
    [
        new() { Text = "Hello " },
        new() { Text = "bold", Bold = true },
        new() { Text = " world", Color = "#EF4444" },
    ];

    [Fact]
    public void Split_keeps_formatting_on_both_sides()
    {
        var (left, right) = RichText.Split(Sample(), 8);
        Assert.Equal("Hello bo", RichText.PlainText(left));
        Assert.True(left[^1].Bold);
        Assert.Equal("ld world", RichText.PlainText(right));
        Assert.True(right[0].Bold);
        Assert.Equal("#EF4444", right[1].Color);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void Split_at_the_edges(int at)
    {
        var (left, right) = RichText.Split(Sample(), at);
        Assert.Equal("Hello bold world", RichText.PlainText(left) + RichText.PlainText(right));
    }

    [Fact]
    public void Normalize_merges_same_style_and_drops_empty()
    {
        var n = RichText.Normalize([new TextSpan { Text = "a" }, new TextSpan { Text = "" }, new TextSpan { Text = "b" }, new TextSpan { Text = "c", Italic = true }]);
        Assert.Equal(2, n.Count);
        Assert.Equal("ab", n[0].Text);
    }

    [Fact]
    public void Concat_and_remove_prefix()
    {
        var joined = RichText.Concat([new TextSpan { Text = "# " }], Sample());
        Assert.Equal("Hello bold world", RichText.PlainText(RichText.RemovePrefix(joined, 2)));
    }

    [Fact]
    public void Markdown_marks_keep_spaces_outside()
    {
        var md = RichText.ToMarkdown(
        [
            new TextSpan { Text = "Make it " },
            new TextSpan { Text = "bold ", Bold = true },
            new TextSpan { Text = "and ", },
            new TextSpan { Text = "marked", Highlight = "#66FACC15" },
            new TextSpan { Text = " x", Strike = true },
        ]);
        Assert.Equal("Make it **bold** and ==marked== ~~x~~", md);
    }

    [Fact]
    public void Block_spans_update_plain_text_and_round_trip()
    {
        var b = new NoteBlock();
        b.SetSpans(Sample());
        Assert.Equal("Hello bold world", b.Text);
        Assert.NotNull(b.Spans);

        var json = JsonSerializer.Serialize(b, Json.Options);
        var back = JsonSerializer.Deserialize<NoteBlock>(json, Json.Options)!;
        Assert.Equal("Hello bold world", back.Text);
        Assert.True(back.GetSpans()[1].Bold);
    }

    [Fact]
    public void Plain_text_blocks_store_no_spans_and_editing_text_drops_stale_formatting()
    {
        var b = new NoteBlock();
        b.SetSpans([new TextSpan { Text = "plain" }]);
        Assert.Null(b.Spans);

        b.SetSpans(Sample());
        var version = b.ContentVersion;
        b.Text = "replaced";
        Assert.Null(b.Spans);
        Assert.True(b.ContentVersion > version);
    }

    [Fact]
    public void Editor_changes_do_not_force_a_reload()
    {
        var b = new NoteBlock();
        var version = b.ContentVersion;
        b.SetSpans(Sample(), fromEditor: true);
        Assert.Equal(version, b.ContentVersion);
    }

    [Theory]
    [InlineData("Meeting notes")]
    [InlineData("Project brief")]
    [InlineData("Script / screenplay")]
    [InlineData("Shot list")]
    [InlineData("To-do list")]
    [InlineData("Journal")]
    public void Page_templates_have_content(string name)
    {
        var blocks = PageTemplates.Build(name, new DateTime(2026, 5, 1));
        Assert.True(blocks.Count >= 4);
        Assert.Contains(blocks, b => b.Type is BlockType.Heading2 or BlockType.Heading3 or BlockType.Callout);
    }

    [Fact]
    public void Storyboard_aspect_parsing()
    {
        Assert.Equal(16.0 / 9, Storyboard.ParseAspect("16:9"), 3);
        Assert.Equal(2.39, Storyboard.ParseAspect("2.39:1"), 3);
        Assert.Equal(16.0 / 9, Storyboard.ParseAspect("nonsense"), 3);
    }
}
