using System.Text;
using FlowBoard.Models;

namespace FlowBoard.Services;

/// <summary>Markdown shortcuts for the block editor and export of a page to Markdown (pure, testable).</summary>
public static class NoteMarkdown
{
    /// <summary>
    /// If <paramref name="text"/> starts with a Markdown shortcut ("# ", "- ", "[] ", "> "...), returns the block
    /// type it stands for and the text without the prefix.
    /// </summary>
    public static (BlockType Type, string Text)? DetectShortcut(string text)
    {
        if (text == "---" || text == "***") return (BlockType.Divider, string.Empty);
        (string Prefix, BlockType Type)[] map =
        [
            ("### ", BlockType.Heading3), ("## ", BlockType.Heading2), ("# ", BlockType.Heading1),
            ("- [ ] ", BlockType.Todo), ("[ ] ", BlockType.Todo), ("[] ", BlockType.Todo),
            ("- ", BlockType.Bullet), ("* ", BlockType.Bullet), ("• ", BlockType.Bullet),
            ("1. ", BlockType.Numbered), ("1) ", BlockType.Numbered),
            ("> ", BlockType.Quote), ("``` ", BlockType.Code), ("```", BlockType.Code), ("! ", BlockType.Callout),
        ];
        foreach (var (prefix, type) in map)
        {
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (prefix == "```" && text.Length > 3) continue; // only a bare ``` turns into code
            return (type, text[prefix.Length..]);
        }

        return null;
    }

    public static bool IsListType(BlockType t) => t is BlockType.Bullet or BlockType.Numbered or BlockType.Todo;

    public static bool HasText(BlockType t) => t is not (BlockType.Divider or BlockType.Image or BlockType.Link);

    /// <summary>Recomputes 1., 2., 3. for consecutive numbered blocks (restarting per indent level).</summary>
    public static void Renumber(IList<NoteBlock> blocks)
    {
        var counters = new int[8];
        foreach (var b in blocks)
        {
            var level = Math.Clamp(b.Indent, 0, 7);
            if (b.Type == BlockType.Numbered)
            {
                counters[level]++;
                b.Number = counters[level];
                for (int i = level + 1; i < counters.Length; i++) counters[i] = 0;
            }
            else
            {
                // Any other block ends the numbering at its level (and deeper); a nested bullet keeps the parent list going.
                for (int i = level; i < counters.Length; i++) counters[i] = 0;
            }
        }
    }

    private static string Md(NoteBlock b) => RichText.ToMarkdown(b.GetSpans());

    public static string ToMarkdown(NotePage page, Func<LinkTarget, Guid, string?>? linkTitle = null)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(page.Title).AppendLine();
        BlockType? previous = null;
        foreach (var b in page.Blocks)
        {
            var pad = new string(' ', Math.Max(0, b.Indent) * 2);
            var isList = IsListType(b.Type);
            if (previous != null && !(isList && IsListType(previous.Value))) sb.AppendLine();
            switch (b.Type)
            {
                case BlockType.Heading1: sb.Append("## ").AppendLine(Md(b)); break;
                case BlockType.Heading2: sb.Append("### ").AppendLine(Md(b)); break;
                case BlockType.Heading3: sb.Append("#### ").AppendLine(Md(b)); break;
                case BlockType.Bullet: sb.Append(pad).Append("- ").AppendLine(Md(b)); break;
                case BlockType.Numbered: sb.Append(pad).Append(b.Number).Append(". ").AppendLine(Md(b)); break;
                case BlockType.Todo: sb.Append(pad).Append(b.IsChecked ? "- [x] " : "- [ ] ").AppendLine(Md(b)); break;
                case BlockType.Quote:
                    foreach (var line in Md(b).Split('\n')) sb.Append("> ").AppendLine(line.TrimEnd('\r'));
                    break;
                case BlockType.Callout:
                    foreach (var line in Md(b).Split('\n')) sb.Append("> 💡 ").AppendLine(line.TrimEnd('\r'));
                    break;
                case BlockType.Code: sb.AppendLine("```").AppendLine(b.Text).AppendLine("```"); break;
                case BlockType.Divider: sb.AppendLine("---"); break;
                case BlockType.Image: sb.Append("![").Append(b.Text).Append("](").Append(b.ImagePath?.Replace('\\', '/')).AppendLine(")"); break;
                case BlockType.Link:
                    var title = b.LinkId is { } id ? linkTitle?.Invoke(b.LinkKind, id) ?? b.Text : b.Text;
                    sb.Append("→ ").Append(title).Append(" (").Append(b.LinkKind).AppendLine(")");
                    break;
                default: sb.Append(pad).AppendLine(Md(b)); break;
            }

            previous = b.Type;
        }

        return sb.ToString();
    }
}
