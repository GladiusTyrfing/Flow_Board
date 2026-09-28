using System.IO;
using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowBoard.Models;

namespace FlowBoard.Services;

/// <summary>
/// Turns pages, canvases, boards and storyboards into files other apps understand: plain text, JSON,
/// CSV, Markdown, Mermaid (GitHub, Notion, Obsidian…), draw.io / diagrams.net and SVG. Pure, so it can be tested.
/// </summary>
public static class Exporters
{
    private static readonly JsonSerializerOptions JsonOut = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static string F(double v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

    private static string X(string? s) => SecurityElement.Escape(s ?? string.Empty) ?? string.Empty;

    // =====================================================================
    // Pages
    // =====================================================================

    public static string PageToText(NotePage page, Workspace? ws = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(page.Title);
        sb.AppendLine(new string('=', Math.Max(3, page.Title.Length)));
        sb.AppendLine();
        int number = 0;
        foreach (var b in page.Blocks)
        {
            number = b.Type == BlockType.Numbered ? number + 1 : 0;
            var pad = new string(' ', Math.Max(0, b.Indent) * 2);
            switch (b.Type)
            {
                case BlockType.Heading1:
                    sb.AppendLine().AppendLine(b.Text.ToUpperInvariant()).AppendLine(new string('-', Math.Max(3, b.Text.Length)));
                    break;
                case BlockType.Heading2:
                    sb.AppendLine().AppendLine(b.Text).AppendLine(new string('-', Math.Max(3, b.Text.Length)));
                    break;
                case BlockType.Heading3:
                    sb.AppendLine().AppendLine(b.Text);
                    break;
                case BlockType.Bullet: sb.AppendLine($"{pad}• {b.Text}"); break;
                case BlockType.Numbered: sb.AppendLine($"{pad}{number}. {b.Text}"); break;
                case BlockType.Todo: sb.AppendLine($"{pad}[{(b.IsChecked ? "x" : " ")}] {b.Text}"); break;
                case BlockType.Quote: sb.AppendLine($"{pad}> {b.Text}"); break;
                case BlockType.Callout: sb.AppendLine($"{pad}! {b.Text}"); break;
                case BlockType.Code:
                    foreach (var line in b.Text.Split('\n')) sb.AppendLine($"{pad}    {line}");
                    break;
                case BlockType.Divider: sb.AppendLine("----------"); break;
                case BlockType.Image: sb.AppendLine($"{pad}[Image: {Path.GetFileName(b.ImagePath ?? "")}]"); break;
                case BlockType.Link:
                    var title = b.LinkId is { } id && ws != null ? LinkResolver.TitleOf(ws, b.LinkKind, id) ?? b.Text : b.Text;
                    sb.AppendLine($"{pad}[{b.LinkKind}: {title}]");
                    break;
                default: sb.AppendLine(pad + b.Text); break;
            }
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string PageToJson(NotePage page, Workspace? ws = null) => JsonSerializer.Serialize(new
    {
        type = "flowboard.page",
        id = page.Id,
        title = page.Title,
        icon = page.Icon,
        created = page.CreatedAt,
        updated = page.UpdatedAt,
        blocks = page.Blocks.Select(b => new
        {
            type = b.Type,
            text = b.Text,
            @checked = b.Type == BlockType.Todo ? b.IsChecked : (bool?)null,
            indent = b.Indent > 0 ? b.Indent : (int?)null,
            image = b.ImagePath,
            link = b.LinkId is { } id
                ? new { kind = b.LinkKind, id, title = ws == null ? null : LinkResolver.TitleOf(ws, b.LinkKind, id) }
                : null,
            // Formatting (bold, colors, inline links…) when the text has any.
            spans = b.Spans is { Count: > 0 } s && s.Any(x => !x.IsPlain) ? s : null,
        }),
    }, JsonOut);

    // =====================================================================
    // Canvas
    // =====================================================================

    private static string NodeLabel(CanvasNode n, Workspace? ws) => n.Shape switch
    {
        NodeShape.Card => n.Card?.Title ?? (n.CardId is { } cid && ws?.FindCard(cid, out _, out _) is { } c ? c.Title : n.Text),
        NodeShape.Link => n.LinkId is { } id && ws != null ? LinkResolver.TitleOf(ws, n.LinkKind, id) ?? n.Text : n.Text,
        NodeShape.Image => string.IsNullOrWhiteSpace(n.Text) ? Path.GetFileName(n.ImagePath ?? "image") : n.Text,
        NodeShape.Ink => string.IsNullOrWhiteSpace(n.Text) ? "Drawing" : n.Text,
        _ => n.Text,
    };

    public static string CanvasToJson(CanvasDoc doc, Workspace? ws = null) => JsonSerializer.Serialize(new
    {
        type = "flowboard.canvas",
        id = doc.Id,
        name = doc.Name,
        nodes = doc.Nodes.OrderBy(n => n.Z).Select(n => new
        {
            id = n.Id,
            label = NodeLabel(n, ws),
            shape = n.Shape,
            x = Math.Round(n.X, 1),
            y = Math.Round(n.Y, 1),
            width = Math.Round(n.Width, 1),
            height = Math.Round(n.Height, 1),
            color = n.Fill,
            section = n.FrameId,
            image = n.ImagePath,
            card = n.CardId,
            link = n.LinkId is { } id ? new { kind = n.LinkKind, id } : null,
            path = n.Shape == NodeShape.Ink ? n.PathData : null,
        }),
        edges = doc.Edges.Select(e => new
        {
            id = e.Id,
            from = e.IsFreeFrom ? (Guid?)null : e.FromId,
            to = e.IsFreeTo ? (Guid?)null : e.ToId,
            fromPoint = e.IsFreeFrom ? new[] { Math.Round(e.FromX, 1), Math.Round(e.FromY, 1) } : null,
            toPoint = e.IsFreeTo ? new[] { Math.Round(e.ToX, 1), Math.Round(e.ToY, 1) } : null,
            label = string.IsNullOrEmpty(e.Label) ? null : e.Label,
            style = e.Style,
            arrow = e.Arrow,
            startArrow = e.StartArrow,
            dashed = e.Dashed,
            color = e.Color,
            thickness = e.Thickness,
        }),
    }, JsonOut);

    private static string MermaidText(string? s) =>
        string.IsNullOrWhiteSpace(s) ? " " : s.Replace("\"", "#quot;").Replace("\r", "").Replace("\n", "<br/>");

    public static string CanvasToMermaid(CanvasDoc doc, Workspace? ws = null)
    {
        var ids = new Dictionary<Guid, string>();
        int i = 0;
        foreach (var n in doc.Nodes.OrderBy(n => n.Z)) ids[n.Id] = $"n{++i}";
        var sb = new StringBuilder();
        sb.AppendLine($"%% {doc.Name} — exported from FlowBoard");
        sb.AppendLine("flowchart LR");

        string Shape(CanvasNode n)
        {
            var t = "\"" + MermaidText(NodeLabel(n, ws)) + "\"";
            return n.Shape switch
            {
                NodeShape.Rectangle or NodeShape.Text => $"[{t}]",
                NodeShape.Ellipse => $"([{t}])",
                NodeShape.Circle => $"(({t}))",
                NodeShape.Diamond => $"{{{t}}}",
                NodeShape.Sticky => $"[/{t}/]",
                NodeShape.Image or NodeShape.Ink => $"[[{t}]]",
                _ => $"({t})",
            };
        }

        void Write(Guid? frame, string indent)
        {
            foreach (var n in doc.Nodes.Where(n => n.FrameId == frame || (frame == null && n.FrameId is { } f && doc.Nodes.All(x => x.Id != f))).OrderBy(n => n.Z))
            {
                if (n.Shape == NodeShape.Frame)
                {
                    sb.AppendLine($"{indent}subgraph {ids[n.Id]}[\"{MermaidText(n.Text)}\"]");
                    Write(n.Id, indent + "  ");
                    sb.AppendLine($"{indent}end");
                }
                else
                {
                    sb.AppendLine($"{indent}{ids[n.Id]}{Shape(n)}");
                }
            }
        }

        Write(null, "  ");

        foreach (var e in doc.Edges.Where(e => !e.IsFreeFrom && !e.IsFreeTo && ids.ContainsKey(e.FromId) && ids.ContainsKey(e.ToId)))
        {
            var link = (e.Dashed, e.Arrow, e.StartArrow) switch
            {
                (false, true, true) => "<-->",
                (false, true, false) => "-->",
                (false, false, _) => "---",
                (true, true, true) => "<-.->",
                (true, true, false) => "-.->",
                (true, false, _) => "-.-",
            };
            var label = string.IsNullOrWhiteSpace(e.Label) ? "" : $"|\"{MermaidText(e.Label)}\"|";
            sb.AppendLine($"  {ids[e.FromId]} {link}{label} {ids[e.ToId]}");
        }

        // Colors, so the diagram looks like the canvas.
        foreach (var n in doc.Nodes.Where(n => n.Shape is not (NodeShape.Frame or NodeShape.Text or NodeShape.Image or NodeShape.Ink or NodeShape.Card or NodeShape.Link)))
            sb.AppendLine($"  style {ids[n.Id]} fill:{n.Fill},stroke:{n.Fill},color:{ContrastText(n.Fill)}");
        return sb.ToString();
    }

    /// <summary>A diagrams.net (draw.io) file with the same shapes, colors, positions, sections and lines.</summary>
    public static string CanvasToDrawio(CanvasDoc doc, Workspace? ws = null)
    {
        var sb = new StringBuilder();
        BeginDrawio(sb, doc.Name);
        var byId = doc.Nodes.ToDictionary(n => n.Id);
        // Parents first, so every section exists before what's inside it.
        int Depth(CanvasNode n)
        {
            int d = 0;
            var seen = new HashSet<Guid>();
            while (n.FrameId is { } f && byId.TryGetValue(f, out var p) && seen.Add(p.Id)) { d++; n = p; }
            return d;
        }

        foreach (var n in doc.Nodes.OrderBy(Depth).ThenBy(n => n.Z))
        {
            var parent = n.FrameId is { } f && byId.TryGetValue(f, out var p) ? p : null;
            double x = n.X - (parent?.X ?? 0), y = n.Y - (parent?.Y ?? 0);
            var style = n.Shape switch
            {
                NodeShape.Frame => $"rounded=1;arcSize=4;dashed=1;fillColor={n.Fill};opacity=15;strokeColor={n.Fill};verticalAlign=top;align=left;spacingLeft=10;spacingTop=-26;fontStyle=1;container=1;collapsible=0;",
                NodeShape.Rectangle => $"rounded=0;fillColor={n.Fill};strokeColor=none;",
                NodeShape.Ellipse => $"ellipse;fillColor={n.Fill};strokeColor=none;",
                NodeShape.Circle => $"ellipse;aspect=fixed;fillColor={n.Fill};strokeColor=none;",
                NodeShape.Diamond => $"rhombus;fillColor={n.Fill};strokeColor=none;",
                NodeShape.Sticky => $"shape=note;size=14;fillColor={n.Fill};strokeColor=none;shadow=1;",
                NodeShape.Text => "text;strokeColor=none;fillColor=none;",
                NodeShape.Image or NodeShape.Ink => "rounded=1;dashed=1;fillColor=#F4F4F5;strokeColor=#A1A1AA;",
                NodeShape.Card or NodeShape.Link => "rounded=1;arcSize=8;fillColor=#FFFFFF;strokeColor=#D4D4D8;shadow=1;align=left;spacingLeft=10;",
                _ => $"rounded=1;fillColor={n.Fill};strokeColor=none;",
            };
            var fontColor = n.Shape is NodeShape.Frame or NodeShape.Text or NodeShape.Image or NodeShape.Ink or NodeShape.Card or NodeShape.Link ? "#18181B" : ContrastText(n.Fill);
            sb.Append($"        <mxCell id=\"{n.Id:N}\" value=\"{X(NodeLabel(n, ws))}\" style=\"{style}whiteSpace=wrap;html=0;fontColor={fontColor};fontSize={F(n.FontSize)};\" vertex=\"1\" parent=\"{(parent == null ? "1" : parent.Id.ToString("N"))}\">");
            sb.AppendLine($"<mxGeometry x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(n.Width)}\" height=\"{F(n.Height)}\" as=\"geometry\" /></mxCell>");
        }

        foreach (var e in doc.Edges)
        {
            var style = new StringBuilder(e.Style switch
            {
                EdgeStyle.Elbow => "edgeStyle=orthogonalEdgeStyle;rounded=1;",
                EdgeStyle.Curved => "curved=1;",
                _ => "",
            });
            style.Append($"endArrow={(e.Arrow ? "classic" : "none")};startArrow={(e.StartArrow ? "classic" : "none")};");
            style.Append($"strokeColor={e.Color};strokeWidth={F(e.Thickness)};html=0;");
            if (e.Dashed) style.Append("dashed=1;");
            var src = !e.IsFreeFrom && byId.ContainsKey(e.FromId) ? $" source=\"{e.FromId:N}\"" : "";
            var tgt = !e.IsFreeTo && byId.ContainsKey(e.ToId) ? $" target=\"{e.ToId:N}\"" : "";
            sb.Append($"        <mxCell id=\"{e.Id:N}\" value=\"{X(e.Label)}\" style=\"{style}\" edge=\"1\" parent=\"1\"{src}{tgt}>");
            sb.Append("<mxGeometry relative=\"1\" as=\"geometry\">");
            if (e.IsFreeFrom) sb.Append($"<mxPoint x=\"{F(e.FromX)}\" y=\"{F(e.FromY)}\" as=\"sourcePoint\" />");
            if (e.IsFreeTo) sb.Append($"<mxPoint x=\"{F(e.ToX)}\" y=\"{F(e.ToY)}\" as=\"targetPoint\" />");
            sb.AppendLine("</mxGeometry></mxCell>");
        }

        EndDrawio(sb);
        return sb.ToString();
    }

    /// <summary>A vector picture of the canvas (opens in browsers, Figma, Illustrator, Inkscape…).</summary>
    public static string CanvasToSvg(CanvasDoc doc, Workspace? ws = null, Func<string, string?>? imageData = null)
    {
        var byId = doc.Nodes.ToDictionary(n => n.Id);
        var routes = doc.Edges.Select(e =>
        {
            Box? a = e.IsFreeFrom ? new Box(e.FromX, e.FromY, 0, 0) : byId.TryGetValue(e.FromId, out var fn) ? fn.Bounds : null;
            Box? b = e.IsFreeTo ? new Box(e.ToX, e.ToY, 0, 0) : byId.TryGetValue(e.ToId, out var tn) ? tn.Bounds : null;
            return (Edge: e, Shape: a != null && b != null ? CanvasGeometry.Route(a.Value, b.Value, e.Style, e.Arrow, e.StartArrow, e.Thickness, e.BendX, e.BendY) : (EdgeShape?)null);
        }).Where(r => r.Shape != null).ToList();

        double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, btm = double.MinValue;
        foreach (var n in doc.Nodes)
        {
            l = Math.Min(l, n.X); t = Math.Min(t, n.Y - (n.Shape == NodeShape.Frame ? 32 : 0));
            r = Math.Max(r, n.X + n.Width); btm = Math.Max(btm, n.Y + n.Height);
        }

        foreach (var (_, s) in routes)
        {
            l = Math.Min(l, Math.Min(s!.X1, s.X2)); t = Math.Min(t, Math.Min(s.Y1, s.Y2));
            r = Math.Max(r, Math.Max(s.X1, s.X2)); btm = Math.Max(btm, Math.Max(s.Y1, s.Y2));
        }

        if (l == double.MaxValue) { l = t = 0; r = btm = 100; }
        const double pad = 40;
        l -= pad; t -= pad; r += pad; btm += pad;

        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"{F(l)} {F(t)} {F(r - l)} {F(btm - t)}\" width=\"{F(r - l)}\" height=\"{F(btm - t)}\" font-family=\"Segoe UI, Arial, sans-serif\">");
        sb.AppendLine($"  <title>{X(doc.Name)}</title>");

        void Text(double cx, double cy, string text, string color, double size, string anchor = "middle", string weight = "normal")
        {
            var lines = text.Replace("\r", "").Split('\n');
            var y0 = cy - (lines.Length - 1) * size * 0.6;
            sb.Append($"  <text x=\"{F(cx)}\" y=\"{F(y0)}\" fill=\"{color}\" font-size=\"{F(size)}\" font-weight=\"{weight}\" text-anchor=\"{anchor}\" dominant-baseline=\"middle\">");
            for (int k = 0; k < lines.Length; k++)
                sb.Append(k == 0 ? $"<tspan>{X(lines[k])}</tspan>" : $"<tspan x=\"{F(cx)}\" dy=\"{F(size * 1.2)}\">{X(lines[k])}</tspan>");
            sb.AppendLine("</text>");
        }

        foreach (var n in doc.Nodes.OrderBy(n => n.Z))
        {
            var label = NodeLabel(n, ws);
            double cx = n.X + n.Width / 2, cy = n.Y + n.Height / 2;
            switch (n.Shape)
            {
                case NodeShape.Frame:
                    sb.AppendLine($"  <rect x=\"{F(n.X)}\" y=\"{F(n.Y)}\" width=\"{F(n.Width)}\" height=\"{F(n.Height)}\" rx=\"10\" fill=\"{n.Fill}\" fill-opacity=\"0.08\" stroke=\"{n.Fill}\" stroke-width=\"2\" stroke-dasharray=\"5 3\" />");
                    var w = Math.Max(40, label.Length * 7.5 + 20);
                    sb.AppendLine($"  <rect x=\"{F(n.X)}\" y=\"{F(n.Y - 30)}\" width=\"{F(w)}\" height=\"24\" rx=\"5\" fill=\"{n.Fill}\" />");
                    Text(n.X + 10, n.Y - 18, label, ContrastText(n.Fill), 13, "start", "bold");
                    continue;
                case NodeShape.Ellipse or NodeShape.Circle:
                    sb.AppendLine($"  <ellipse cx=\"{F(cx)}\" cy=\"{F(cy)}\" rx=\"{F(n.Width / 2)}\" ry=\"{F(n.Height / 2)}\" fill=\"{n.Fill}\" />");
                    break;
                case NodeShape.Diamond:
                    sb.AppendLine($"  <polygon points=\"{F(cx)},{F(n.Y)} {F(n.X + n.Width)},{F(cy)} {F(cx)},{F(n.Y + n.Height)} {F(n.X)},{F(cy)}\" fill=\"{n.Fill}\" />");
                    break;
                case NodeShape.Text:
                    Text(cx, cy, label, "#18181B", n.FontSize);
                    continue;
                case NodeShape.Ink when n.PathData != null:
                    var sx = n.InkWidth > 0 ? n.Width / n.InkWidth : 1;
                    var sy = n.InkHeight > 0 ? n.Height / n.InkHeight : 1;
                    sb.AppendLine($"  <path d=\"{X(n.PathData)}\" transform=\"translate({F(n.X)} {F(n.Y)}) scale({F(sx)} {F(sy)})\" fill=\"none\" stroke=\"{n.Fill}\" stroke-width=\"{F(n.StrokeWidth)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />");
                    continue;
                case NodeShape.Image:
                    var data = n.ImagePath != null ? imageData?.Invoke(n.ImagePath) : null;
                    if (data != null)
                        sb.AppendLine($"  <image x=\"{F(n.X)}\" y=\"{F(n.Y)}\" width=\"{F(n.Width)}\" height=\"{F(n.Height)}\" preserveAspectRatio=\"xMidYMid slice\" href=\"{data}\" xlink:href=\"{data}\" />");
                    else
                        sb.AppendLine($"  <rect x=\"{F(n.X)}\" y=\"{F(n.Y)}\" width=\"{F(n.Width)}\" height=\"{F(n.Height)}\" rx=\"6\" fill=\"#F4F4F5\" stroke=\"#A1A1AA\" stroke-dasharray=\"4 3\" />");
                    continue;
                case NodeShape.Card or NodeShape.Link:
                    sb.AppendLine($"  <rect x=\"{F(n.X)}\" y=\"{F(n.Y)}\" width=\"{F(n.Width)}\" height=\"{F(n.Height)}\" rx=\"8\" fill=\"#FFFFFF\" stroke=\"#D4D4D8\" />");
                    Text(n.X + 12, cy, label, "#18181B", 13, "start", "600");
                    continue;
                case NodeShape.Rectangle:
                    sb.AppendLine($"  <rect x=\"{F(n.X)}\" y=\"{F(n.Y)}\" width=\"{F(n.Width)}\" height=\"{F(n.Height)}\" rx=\"3\" fill=\"{n.Fill}\" />");
                    break;
                default:
                    sb.AppendLine($"  <rect x=\"{F(n.X)}\" y=\"{F(n.Y)}\" width=\"{F(n.Width)}\" height=\"{F(n.Height)}\" rx=\"{(n.Shape == NodeShape.Sticky ? 2 : 12)}\" fill=\"{n.Fill}\" />");
                    break;
            }

            if (!string.IsNullOrWhiteSpace(label)) Text(cx, cy, label, ContrastText(n.Fill), n.FontSize);
        }

        foreach (var (e, s) in routes)
        {
            var dash = e.Dashed ? " stroke-dasharray=\"6 4\"" : "";
            sb.AppendLine($"  <path d=\"{s!.Path}\" fill=\"none\" stroke=\"{e.Color}\" stroke-width=\"{F(e.Thickness)}\" stroke-linecap=\"round\"{dash} />");
            if (!string.IsNullOrEmpty(s.Arrow)) sb.AppendLine($"  <path d=\"{s.Arrow}\" fill=\"{e.Color}\" />");
            if (!string.IsNullOrWhiteSpace(e.Label))
            {
                var w = e.Label.Length * 7 + 14;
                sb.AppendLine($"  <rect x=\"{F(s.LabelX - w / 2.0)}\" y=\"{F(s.LabelY - 11)}\" width=\"{F(w)}\" height=\"22\" rx=\"11\" fill=\"#FFFFFF\" stroke=\"{e.Color}\" />");
                Text(s.LabelX, s.LabelY, e.Label, "#18181B", 12);
            }
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    // =====================================================================
    // Boards
    // =====================================================================

    private static string LabelNames(Board b, Card c) =>
        string.Join(", ", c.LabelIds.Select(id => b.Labels.FirstOrDefault(l => l.Id == id)?.Name).Where(n => !string.IsNullOrEmpty(n)));

    public static string BoardToJson(Board board, Workspace? ws = null) => JsonSerializer.Serialize(new
    {
        type = "flowboard.board",
        id = board.Id,
        name = board.Name,
        labels = board.Labels.Select(l => new { l.Id, l.Name, l.Color }),
        lists = board.Lists.Select(l => new
        {
            id = l.Id,
            name = l.Name,
            isDone = l.IsDoneList ? true : (bool?)null,
            cards = l.Cards.Select(c => new
            {
                id = c.Id,
                title = c.Title,
                description = string.IsNullOrEmpty(c.Description) ? null : c.Description,
                done = c.IsCompleted,
                priority = c.Priority,
                start = c.StartDate,
                due = c.DueDate,
                labels = c.LabelIds.Select(id => board.Labels.FirstOrDefault(x => x.Id == id)?.Name).Where(n => n != null),
                checklists = c.Checklists.Count == 0 ? null : c.Checklists.Select(cl => new { cl.Title, items = cl.Items.Select(i => new { i.Text, done = i.IsDone }) }),
                blockedBy = c.BlockedByIds.Count == 0 ? null : c.BlockedByIds,
                related = c.LinkedCardIds.Count == 0 ? null : c.LinkedCardIds,
                links = c.Links.Count == 0 ? null : c.Links.Select(x => new { kind = x.Kind, id = x.Id, title = ws == null ? null : LinkResolver.TitleOf(ws, x.Kind, x.Id) }),
            }),
        }),
    }, JsonOut);

    private static string Csv(string? s)
    {
        s ??= string.Empty;
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public static string BoardToCsv(Board board)
    {
        var sb = new StringBuilder();
        sb.AppendLine("List,Title,Description,Done,Priority,Start,Due,Labels,Checklist");
        foreach (var l in board.Lists)
            foreach (var c in l.Cards)
            {
                var items = c.Checklists.SelectMany(x => x.Items).ToList();
                sb.AppendLine(string.Join(",",
                    Csv(l.Name), Csv(c.Title), Csv(c.Description), c.IsCompleted ? "yes" : "no", c.Priority,
                    c.StartDate?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "",
                    c.DueDate?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "",
                    Csv(LabelNames(board, c)),
                    items.Count == 0 ? "" : $"{items.Count(i => i.IsDone)}/{items.Count}"));
            }

        return sb.ToString();
    }

    public static string BoardToMarkdown(Board board)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {board.Name}").AppendLine();
        foreach (var l in board.Lists)
        {
            sb.AppendLine($"## {l.Name}").AppendLine();
            foreach (var c in l.Cards)
            {
                var extra = new List<string>();
                if (c.DueDate is { } due) extra.Add($"due {due:yyyy-MM-dd}");
                if (c.Priority != Priority.None) extra.Add(c.Priority.ToString().ToLowerInvariant());
                var labels = LabelNames(board, c);
                if (labels.Length > 0) extra.Add(labels);
                sb.AppendLine($"- [{(c.IsCompleted ? "x" : " ")}] {c.Title}{(extra.Count > 0 ? " — " + string.Join(" · ", extra) : "")}");
                foreach (var i in c.Checklists.SelectMany(x => x.Items)) sb.AppendLine($"  - [{(i.IsDone ? "x" : " ")}] {i.Text}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Lists as groups, cards inside, dependencies and related cards as arrows.</summary>
    public static string BoardToMermaid(Board board)
    {
        var ids = new Dictionary<Guid, string>();
        int i = 0;
        var sb = new StringBuilder();
        sb.AppendLine($"%% {board.Name} — exported from FlowBoard");
        sb.AppendLine("flowchart LR");
        int li = 0;
        foreach (var l in board.Lists)
        {
            sb.AppendLine($"  subgraph L{++li}[\"{MermaidText(l.Name)}\"]");
            sb.AppendLine("    direction TB");
            foreach (var c in l.Cards)
            {
                ids[c.Id] = $"c{++i}";
                sb.AppendLine($"    {ids[c.Id]}(\"{(c.IsCompleted ? "✓ " : "")}{MermaidText(c.Title)}\")");
            }

            sb.AppendLine("  end");
        }

        // Keep the lists in board order.
        for (int k = 1; k < li; k++) sb.AppendLine($"  L{k} ~~~ L{k + 1}");
        foreach (var c in board.Lists.SelectMany(l => l.Cards))
        {
            foreach (var b in c.BlockedByIds.Where(ids.ContainsKey)) sb.AppendLine($"  {ids[b]} -->|blocks| {ids[c.Id]}");
            foreach (var r in c.LinkedCardIds.Where(x => ids.ContainsKey(x) && string.CompareOrdinal(ids[x], ids[c.Id]) > 0)) sb.AppendLine($"  {ids[c.Id]} -.- {ids[r]}");
        }

        return sb.ToString();
    }

    /// <summary>The board as it looks: a column per list with its cards, dependency arrows between cards.</summary>
    public static string BoardToDrawio(Board board)
    {
        var sb = new StringBuilder();
        BeginDrawio(sb, board.Name);
        const double colW = 272, gap = 24, cardW = 248;
        double x = 0;
        foreach (var l in board.Lists)
        {
            double y = 40;
            var cards = new List<(Card Card, double Y, double H)>();
            foreach (var c in l.Cards)
            {
                var h = 44 + Math.Ceiling(Math.Max(1, c.Title.Length) / 30.0) * 16 + (LabelNames(board, c).Length > 0 ? 16 : 0);
                cards.Add((c, y, h));
                y += h + 8;
            }

            var listColor = l.Color ?? "#E4E4E7";
            sb.AppendLine($"        <mxCell id=\"{l.Id:N}\" value=\"{X($"{l.Name} ({l.Cards.Count})")}\" style=\"swimlane;startSize=32;rounded=1;arcSize=6;fillColor={listColor};swimlaneFillColor=#F4F4F5;strokeColor=#D4D4D8;fontStyle=1;fontColor=#18181B;html=0;collapsible=0;\" vertex=\"1\" parent=\"1\"><mxGeometry x=\"{F(x)}\" y=\"0\" width=\"{F(colW)}\" height=\"{F(Math.Max(120, y + 12))}\" as=\"geometry\" /></mxCell>");
            foreach (var (c, cy, h) in cards)
            {
                var labels = LabelNames(board, c);
                var text = (c.IsCompleted ? "✓ " : "") + c.Title + (labels.Length > 0 ? "\n" + labels : "") + (c.DueDate is { } d ? $"\nDue {d:d MMM}" : "");
                var stroke = c.Priority switch { Priority.Urgent => "#EF4444", Priority.High => "#F97316", Priority.Medium => "#F59E0B", Priority.Low => "#3B82F6", _ => "#D4D4D8" };
                sb.AppendLine($"        <mxCell id=\"{c.Id:N}\" value=\"{X(text)}\" style=\"rounded=1;arcSize=8;whiteSpace=wrap;html=0;fillColor=#FFFFFF;strokeColor={stroke};align=left;verticalAlign=top;spacingLeft=10;spacingTop=6;fontColor={(c.IsCompleted ? "#71717A" : "#18181B")};shadow=1;\" vertex=\"1\" parent=\"{l.Id:N}\"><mxGeometry x=\"12\" y=\"{F(cy)}\" width=\"{F(cardW)}\" height=\"{F(h)}\" as=\"geometry\" /></mxCell>");
            }

            x += colW + gap;
        }

        var all = board.Lists.SelectMany(l => l.Cards).Select(c => c.Id).ToHashSet();
        foreach (var c in board.Lists.SelectMany(l => l.Cards))
            foreach (var b in c.BlockedByIds.Where(all.Contains))
                sb.AppendLine($"        <mxCell id=\"{b:N}-{c.Id:N}\" value=\"blocks\" style=\"curved=1;endArrow=classic;strokeColor=#EF4444;dashed=1;html=0;\" edge=\"1\" parent=\"1\" source=\"{b:N}\" target=\"{c.Id:N}\"><mxGeometry relative=\"1\" as=\"geometry\" /></mxCell>");

        EndDrawio(sb);
        return sb.ToString();
    }

    // =====================================================================
    // Storyboards
    // =====================================================================

    public static string StoryboardToJson(Storyboard sb) => JsonSerializer.Serialize(new
    {
        type = "flowboard.storyboard",
        id = sb.Id,
        name = sb.Name,
        mode = sb.Mode,
        aspect = sb.Aspect,
        runtimeSeconds = sb.Shots.Sum(s => s.DurationSeconds),
        shots = sb.Shots.Select((s, i) => new
        {
            number = i + 1,
            id = s.Id,
            title = s.Title,
            scene = s.Scene,
            status = string.IsNullOrEmpty(s.Status) ? null : s.Status,
            description = s.Description,
            image = s.ImagePath,
            date = s.Date,
            shotType = sb.Mode == StoryboardMode.Film ? s.ShotType : null,
            angle = sb.Mode == StoryboardMode.Film ? s.Angle : null,
            movement = sb.Mode == StoryboardMode.Film ? s.Movement : null,
            lens = sb.Mode == StoryboardMode.Film && s.Lens.Length > 0 ? s.Lens : null,
            location = s.Location.Length > 0 ? s.Location : null,
            action = sb.Mode == StoryboardMode.Animation ? s.Action : null,
            dialogue = sb.Mode == StoryboardMode.Animation ? s.Dialogue : null,
            durationSeconds = s.DurationSeconds,
            transition = s.Transition,
            tags = s.Tags.Select(t => t.Text),
            shotList = s.ShotList.Select(x => new { x.Text, done = x.IsDone }),
        }),
    }, JsonOut);

    public static string StoryboardToCsv(Storyboard sb)
    {
        var o = new StringBuilder();
        o.AppendLine("Shot,Title,Scene,Status,Description,Shot type,Angle,Movement,Lens,Location,Action,Dialogue,Seconds,Transition");
        int i = 0;
        foreach (var s in sb.Shots)
            o.AppendLine(string.Join(",", ++i, Csv(s.Title), Csv(s.Scene), Csv(s.Status), Csv(s.Description), Csv(s.ShotType), Csv(s.Angle),
                Csv(s.Movement), Csv(s.Lens), Csv(s.Location), Csv(s.Action), Csv(s.Dialogue), F(s.DurationSeconds), Csv(s.Transition)));
        return o.ToString();
    }

    // =====================================================================
    // helpers
    // =====================================================================

    private static void BeginDrawio(StringBuilder sb, string name)
    {
        sb.AppendLine($"<mxfile host=\"FlowBoard\" type=\"device\">");
        sb.AppendLine($"  <diagram id=\"flowboard\" name=\"{X(name)}\">");
        sb.AppendLine("    <mxGraphModel grid=\"1\" gridSize=\"20\" guides=\"1\" tooltips=\"1\" connect=\"1\" arrows=\"1\" page=\"0\">");
        sb.AppendLine("      <root>");
        sb.AppendLine("        <mxCell id=\"0\" />");
        sb.AppendLine("        <mxCell id=\"1\" parent=\"0\" />");
    }

    private static void EndDrawio(StringBuilder sb)
    {
        sb.AppendLine("      </root>");
        sb.AppendLine("    </mxGraphModel>");
        sb.AppendLine("  </diagram>");
        sb.AppendLine("</mxfile>");
    }

    /// <summary>Black or white text, whichever reads better on the color.</summary>
    public static string ContrastText(string? hex)
    {
        if (hex is not { Length: >= 7 } || hex[0] != '#') return "#18181B";
        try
        {
            var o = hex.Length == 9 ? 3 : 1;
            int r = Convert.ToInt32(hex.Substring(o, 2), 16), g = Convert.ToInt32(hex.Substring(o + 2, 2), 16), b = Convert.ToInt32(hex.Substring(o + 4, 2), 16);
            return (0.299 * r + 0.587 * g + 0.114 * b) > 160 ? "#18181B" : "#FFFFFF";
        }
        catch
        {
            return "#18181B";
        }
    }
}
