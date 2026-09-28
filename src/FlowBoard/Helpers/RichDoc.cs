using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FlowBoard.Models;
using FlowBoard.Services;

namespace FlowBoard.Helpers;

/// <summary>Converts between note <see cref="TextSpan"/>s and a RichTextBox document, and maps caret positions to text offsets.</summary>
public static class RichDoc
{
    public static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas");

    /// <summary>Loads spans into the editor (one paragraph; line breaks for "\n").</summary>
    public static void Load(RichTextBox box, IEnumerable<TextSpan> spans)
    {
        var doc = box.Document;
        doc.PagePadding = new Thickness(0);
        // The document follows the editor's font, so block styles (headings, quotes, code) apply to it.
        if (!System.Windows.Data.BindingOperations.IsDataBound(doc, TextElement.FontSizeProperty))
        {
            foreach (var dp in new[] { TextElement.FontSizeProperty, TextElement.FontWeightProperty, TextElement.FontStyleProperty, TextElement.FontFamilyProperty, TextElement.ForegroundProperty })
                System.Windows.Data.BindingOperations.SetBinding(doc, dp, new System.Windows.Data.Binding(dp.Name) { Source = box });
        }

        doc.Blocks.Clear();
        var para = new Paragraph { Margin = new Thickness(0) };
        foreach (var s in spans)
        {
            var lines = s.Text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) para.Inlines.Add(new LineBreak());
                if (lines[i].Length == 0) continue;
                var run = new Run(lines[i]);
                if (s.Bold) run.FontWeight = FontWeights.Bold;
                if (s.Italic) run.FontStyle = FontStyles.Italic;
                if (s.Underline || s.Strike)
                {
                    var deco = new TextDecorationCollection();
                    if (s.Underline) deco.Add(TextDecorations.Underline);
                    if (s.Strike) deco.Add(TextDecorations.Strikethrough);
                    run.TextDecorations = deco;
                }

                if (s.Code) run.FontFamily = CodeFont;
                if (s.Color != null && ThemeService.TryParseColor(s.Color, out var fg)) run.Foreground = new SolidColorBrush(fg);
                if (s.Highlight != null && ThemeService.TryParseColor(s.Highlight, out var bg)) run.Background = new SolidColorBrush(bg);
                para.Inlines.Add(run);
            }
        }

        doc.Blocks.Add(para);
    }

    /// <summary>Reads the editor back into spans. Only formatting set on the text itself counts (not the block's heading style).</summary>
    public static List<TextSpan> Read(RichTextBox box)
    {
        var list = new List<TextSpan>();
        var first = true;
        foreach (var block in box.Document.Blocks)
        {
            if (block is not Paragraph p) continue;
            if (!first) list.Add(new TextSpan { Text = "\n" });
            first = false;
            ReadInlines(p.Inlines, list, p);
        }

        return RichText.Normalize(list);
    }

    private static void ReadInlines(InlineCollection inlines, List<TextSpan> list, Paragraph para)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    list.Add(Style(run, para, run.Text));
                    break;
                case LineBreak:
                    list.Add(new TextSpan { Text = "\n" });
                    break;
                case Span span:
                    ReadInlines(span.Inlines, list, para);
                    break;
                case InlineUIContainer:
                    break;
            }
        }
    }

    private static TextSpan Style(Run run, Paragraph para, string text)
    {
        var span = new TextSpan { Text = text };
        for (DependencyObject? d = run; d != null && d != para; d = (d as TextElement)?.Parent)
        {
            if (d is not TextElement te) break;
            if (!span.Bold && te.ReadLocalValue(TextElement.FontWeightProperty) is FontWeight fw && fw.ToOpenTypeWeight() >= 600) span.Bold = true;
            if (!span.Italic && te.ReadLocalValue(TextElement.FontStyleProperty) is FontStyle fs && fs != FontStyles.Normal) span.Italic = true;
            if (!span.Code && te.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily ff
                && (ff.Source.Contains("Mono", StringComparison.OrdinalIgnoreCase) || ff.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase)))
                span.Code = true;
            if (span.Color == null && te.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush fg) span.Color = Hex(fg.Color);
            if (span.Highlight == null && te.ReadLocalValue(TextElement.BackgroundProperty) is SolidColorBrush bg && bg.Color.A > 0) span.Highlight = Hex(bg.Color);
            if (te is Inline inl && inl.ReadLocalValue(Inline.TextDecorationsProperty) is TextDecorationCollection deco)
            {
                foreach (var t in deco)
                {
                    if (t.Location == TextDecorationLocation.Underline) span.Underline = true;
                    if (t.Location == TextDecorationLocation.Strikethrough) span.Strike = true;
                }
            }
        }

        return span;
    }

    private static string Hex(Color c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    // ---------- caret offsets ----------

    /// <summary>Character offset of a position ("\n" counts 1 for line breaks and paragraph breaks).</summary>
    public static int OffsetOf(RichTextBox box, TextPointer p)
    {
        var off = 0;
        var first = true;
        foreach (var block in box.Document.Blocks)
        {
            if (block is not Paragraph para) continue;
            if (!first) off++;
            first = false;
            if (p.CompareTo(para.ContentEnd) <= 0 && p.CompareTo(para.ContentStart) >= 0)
            {
                Walk(para.Inlines, p, ref off);
                return off;
            }

            off += Length(para.Inlines);
        }

        return off;
    }

    private static bool Walk(InlineCollection inlines, TextPointer p, ref int off)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    if (p.CompareTo(run.ContentStart) <= 0) return true;
                    if (p.CompareTo(run.ContentEnd) <= 0)
                    {
                        off += run.ContentStart.GetOffsetToPosition(p);
                        return true;
                    }

                    off += run.Text.Length;
                    break;
                case LineBreak lb:
                    if (p.CompareTo(lb.ElementStart) <= 0) return true;
                    off++;
                    break;
                case Span span:
                    if (Walk(span.Inlines, p, ref off)) return true;
                    break;
            }
        }

        return false;
    }

    private static int Length(InlineCollection inlines)
    {
        var n = 0;
        foreach (var inline in inlines)
        {
            n += inline switch
            {
                Run r => r.Text.Length,
                LineBreak => 1,
                Span s => Length(s.Inlines),
                _ => 0,
            };
        }

        return n;
    }

    /// <summary>Position at a character offset (clamped to the end).</summary>
    public static TextPointer PointerAt(RichTextBox box, int offset)
    {
        if (offset < 0) return box.Document.ContentEnd;
        var remaining = offset;
        var first = true;
        foreach (var block in box.Document.Blocks)
        {
            if (block is not Paragraph para) continue;
            if (!first)
            {
                if (remaining == 0) return para.ContentStart;
                remaining--;
            }

            first = false;
            if (Find(para.Inlines, ref remaining) is { } found) return found;
            if (remaining == 0) return para.ContentEnd;
        }

        return box.Document.ContentEnd;
    }

    private static TextPointer? Find(InlineCollection inlines, ref int remaining)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    if (remaining <= run.Text.Length) return run.ContentStart.GetPositionAtOffset(remaining);
                    remaining -= run.Text.Length;
                    break;
                case LineBreak lb:
                    if (remaining == 0) return lb.ElementStart;
                    remaining--;
                    break;
                case Span span:
                    if (Find(span.Inlines, ref remaining) is { } p) return p;
                    break;
            }
        }

        return null;
    }

    public static bool IsOnFirstLine(RichTextBox box)
    {
        box.CaretPosition.GetLineStartPosition(-1, out var moved);
        return moved == 0;
    }

    public static bool IsOnLastLine(RichTextBox box)
    {
        box.CaretPosition.GetLineStartPosition(1, out var moved);
        return moved == 0;
    }
}
