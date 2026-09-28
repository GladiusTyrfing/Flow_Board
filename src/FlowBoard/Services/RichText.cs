using System.Text;
using FlowBoard.Models;

namespace FlowBoard.Services;

/// <summary>Operations on formatted text (lists of <see cref="TextSpan"/>), used by the note editor. Pure, so it's tested.</summary>
public static class RichText
{
    public static string PlainText(IEnumerable<TextSpan> spans) => string.Concat(spans.Select(s => s.Text));

    /// <summary>Drops empty spans and merges neighbours with the same formatting.</summary>
    public static List<TextSpan> Normalize(IEnumerable<TextSpan> spans)
    {
        var result = new List<TextSpan>();
        foreach (var s in spans)
        {
            if (string.IsNullOrEmpty(s.Text)) continue;
            if (result.Count > 0 && result[^1].SameStyle(s)) result[^1] = result[^1].With(result[^1].Text + s.Text);
            else result.Add(s.With(s.Text));
        }

        return result;
    }

    /// <summary>Splits formatted text at a character offset.</summary>
    public static (List<TextSpan> Left, List<TextSpan> Right) Split(IEnumerable<TextSpan> spans, int offset)
    {
        var left = new List<TextSpan>();
        var right = new List<TextSpan>();
        var pos = 0;
        foreach (var s in spans)
        {
            var end = pos + s.Text.Length;
            if (end <= offset) left.Add(s.With(s.Text));
            else if (pos >= offset) right.Add(s.With(s.Text));
            else
            {
                left.Add(s.With(s.Text[..(offset - pos)]));
                right.Add(s.With(s.Text[(offset - pos)..]));
            }

            pos = end;
        }

        return (Normalize(left), Normalize(right));
    }

    public static List<TextSpan> Concat(IEnumerable<TextSpan> a, IEnumerable<TextSpan> b) => Normalize(a.Concat(b));

    public static List<TextSpan> RemovePrefix(IEnumerable<TextSpan> spans, int count) => Split(spans, count).Right;

    /// <summary>Markdown with the common inline marks (colors have no Markdown equivalent and are dropped).</summary>
    public static string ToMarkdown(IEnumerable<TextSpan> spans)
    {
        var sb = new StringBuilder();
        foreach (var s in spans)
        {
            if (s.Text.Length == 0) continue;
            if (s.IsLink)
            {
                sb.Append('[').Append(s.Text.Trim()).Append(']');
                continue;
            }

            // Keep surrounding spaces outside the markers so "**bold** text" stays valid Markdown.
            var core = s.Text.Trim();
            if (core.Length == 0 || s.IsPlain)
            {
                sb.Append(s.Text);
                continue;
            }

            var lead = s.Text[..s.Text.IndexOf(core, StringComparison.Ordinal)];
            var trail = s.Text[(lead.Length + core.Length)..];
            var t = core;
            if (s.Code) t = $"`{t}`";
            if (s.Bold) t = $"**{t}**";
            if (s.Italic) t = $"*{t}*";
            if (s.Strike) t = $"~~{t}~~";
            if (s.Underline) t = $"<u>{t}</u>";
            if (s.Highlight != null) t = $"=={t}==";
            sb.Append(lead).Append(t).Append(trail);
        }

        return sb.ToString();
    }
}
