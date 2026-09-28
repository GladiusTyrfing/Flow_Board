using System.Globalization;
using System.Text;
using FlowBoard.Models;

namespace FlowBoard.Services;

public readonly record struct Box(double X, double Y, double W, double H)
{
    public double CenterX => X + W / 2;
    public double CenterY => Y + H / 2;
    public double Right => X + W;
    public double Bottom => Y + H;

    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
    public bool Intersects(Box o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
    public bool Contains(Box o) => o.X >= X && o.Right <= Right && o.Y >= Y && o.Bottom <= Bottom;
}

/// <summary>Path (WPF path mini-language), arrow head and label position for a connector.</summary>
public sealed record EdgeShape(string Path, string Arrow, double LabelX, double LabelY, double X1 = 0, double Y1 = 0, double X2 = 0, double Y2 = 0);

/// <summary>Connector routing between two boxes (pure math, no WPF so it can be unit tested).</summary>
public static class CanvasGeometry
{
    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Picks the facing sides of the two boxes: left/right when they sit side by side, top/bottom otherwise.</summary>
    public static (double X1, double Y1, double Nx1, double Ny1, double X2, double Y2, double Nx2, double Ny2) Anchors(Box a, Box b)
    {
        var dx = b.CenterX - a.CenterX;
        var dy = b.CenterY - a.CenterY;
        var horizontal = Math.Abs(dx) / Math.Max(1, a.W + b.W) >= Math.Abs(dy) / Math.Max(1, a.H + b.H);
        if (horizontal)
        {
            var s = dx >= 0 ? 1 : -1;
            return (s > 0 ? a.Right : a.X, a.CenterY, s, 0, s > 0 ? b.X : b.Right, b.CenterY, -s, 0);
        }

        var t = dy >= 0 ? 1 : -1;
        return (a.CenterX, t > 0 ? a.Bottom : a.Y, 0, t, b.CenterX, t > 0 ? b.Y : b.Bottom, 0, -t);
    }

    public static EdgeShape Route(Box a, Box b, EdgeStyle style, bool arrow, bool startArrow = false, double thickness = 2, double bendX = 0, double bendY = 0)
    {
        var (x1, y1, nx1, ny1, x2, y2, nx2, ny2) = Anchors(a, b);
        // A free end (a 0-size box) is the exact point.
        if (a.W <= 0.01 && a.H <= 0.01) (x1, y1) = (a.X, a.Y);
        if (b.W <= 0.01 && b.H <= 0.01) (x2, y2) = (b.X, b.Y);
        var sb = new StringBuilder();
        sb.Append("M ").Append(F(x1)).Append(',').Append(F(y1)).Append(' ');
        double dirX, dirY, lx, ly, sdx, sdy;
        var size = 8 + thickness * 1.6;

        if (bendX != 0 || bendY != 0)
        {
            // Bent line: a smooth curve that passes through the handle (the middle point pulled by the bend).
            double hx = (x1 + x2) / 2 + bendX, hy = (y1 + y2) / 2 + bendY;
            double cx = 2 * hx - (x1 + x2) / 2, cy = 2 * hy - (y1 + y2) / 2;
            sb.Append("Q ").Append(F(cx)).Append(',').Append(F(cy)).Append(' ').Append(F(x2)).Append(',').Append(F(y2));
            var bentHeads = (arrow ? ArrowHead(x2, y2, x2 - cx, y2 - cy, size) : string.Empty)
                            + (startArrow ? " " + ArrowHead(x1, y1, x1 - cx, y1 - cy, size) : string.Empty);
            return new EdgeShape(sb.ToString(), bentHeads.Trim(), hx, hy, x1, y1, x2, y2);
        }

        switch (style)
        {
            case EdgeStyle.Straight:
                sb.Append("L ").Append(F(x2)).Append(',').Append(F(y2));
                dirX = x2 - x1;
                dirY = y2 - y1;
                (sdx, sdy) = (x1 - x2, y1 - y2);
                lx = (x1 + x2) / 2;
                ly = (y1 + y2) / 2;
                break;

            case EdgeStyle.Elbow:
                if (nx1 != 0)
                {
                    var mx = (x1 + x2) / 2;
                    sb.Append("L ").Append(F(mx)).Append(',').Append(F(y1)).Append(' ')
                      .Append("L ").Append(F(mx)).Append(',').Append(F(y2)).Append(' ')
                      .Append("L ").Append(F(x2)).Append(',').Append(F(y2));
                    dirX = x2 - mx;
                    dirY = 0;
                    (sdx, sdy) = (x1 - mx, 0);
                    lx = mx;
                    ly = (y1 + y2) / 2;
                }
                else
                {
                    var my = (y1 + y2) / 2;
                    sb.Append("L ").Append(F(x1)).Append(',').Append(F(my)).Append(' ')
                      .Append("L ").Append(F(x2)).Append(',').Append(F(my)).Append(' ')
                      .Append("L ").Append(F(x2)).Append(',').Append(F(y2));
                    dirX = 0;
                    dirY = y2 - my;
                    (sdx, sdy) = (0, y1 - my);
                    lx = (x1 + x2) / 2;
                    ly = my;
                }

                if (Math.Abs(dirX) + Math.Abs(dirY) < 0.01)
                {
                    dirX = -nx2;
                    dirY = -ny2;
                }

                if (Math.Abs(sdx) + Math.Abs(sdy) < 0.01)
                {
                    sdx = -nx1;
                    sdy = -ny1;
                }

                break;

            default:
                if (a.W <= 0.01 && b.W <= 0.01)
                {
                    // Two free points: a gentle curve would have no direction to follow, so draw it straight.
                    goto case EdgeStyle.Straight;
                }

                var dist = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                var k = Math.Max(30, dist * 0.45);
                double c1x = x1 + nx1 * k, c1y = y1 + ny1 * k, c2x = x2 + nx2 * k, c2y = y2 + ny2 * k;
                sb.Append("C ").Append(F(c1x)).Append(',').Append(F(c1y)).Append(' ')
                  .Append(F(c2x)).Append(',').Append(F(c2y)).Append(' ')
                  .Append(F(x2)).Append(',').Append(F(y2));
                dirX = x2 - c2x;
                dirY = y2 - c2y;
                (sdx, sdy) = (x1 - c1x, y1 - c1y);
                lx = 0.125 * x1 + 0.375 * c1x + 0.375 * c2x + 0.125 * x2;
                ly = 0.125 * y1 + 0.375 * c1y + 0.375 * c2y + 0.125 * y2;
                break;
        }

        var heads = (arrow ? ArrowHead(x2, y2, dirX, dirY, size) : string.Empty)
                    + (startArrow ? " " + ArrowHead(x1, y1, sdx, sdy, size) : string.Empty);
        return new EdgeShape(sb.ToString(), heads.Trim(), lx, ly, x1, y1, x2, y2);
    }

    /// <summary>Filled triangle with its tip at (x, y) pointing along (dx, dy).</summary>
    public static string ArrowHead(double x, double y, double dx, double dy, double size = 11)
    {
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001) return string.Empty;
        dx /= len;
        dy /= len;
        double bx = x - dx * size, by = y - dy * size;
        double px = -dy * size * 0.5, py = dx * size * 0.5;
        return $"M {F(x)},{F(y)} L {F(bx + px)},{F(by + py)} L {F(bx - px)},{F(by - py)} Z";
    }

    public static double Snap(double v, double grid) => grid <= 0 ? v : Math.Round(v / grid) * grid;
}

/// <summary>Tidy left-to-right layered layout for flowcharts and mind maps.</summary>
public static class CanvasLayout
{
    public static Dictionary<Guid, (double X, double Y)> Layered(
        IReadOnlyList<(Guid Id, double W, double H)> nodes,
        IReadOnlyList<(Guid From, Guid To)> edges,
        double gapX = 90,
        double gapY = 36)
    {
        var result = new Dictionary<Guid, (double X, double Y)>();
        if (nodes.Count == 0) return result;
        var ids = nodes.Select(n => n.Id).ToHashSet();
        var size = nodes.ToDictionary(n => n.Id, n => (n.W, n.H));
        var valid = edges.Where(e => e.From != e.To && ids.Contains(e.From) && ids.Contains(e.To)).Distinct().ToList();
        var children = ids.ToDictionary(i => i, _ => new List<Guid>());
        var parents = ids.ToDictionary(i => i, _ => new List<Guid>());
        foreach (var (from, to) in valid)
        {
            children[from].Add(to);
            parents[to].Add(from);
        }

        // Layer = longest distance from a root, following edges in document order and ignoring cycles.
        var layer = new Dictionary<Guid, int>();
        var order = nodes.Select(n => n.Id).ToList();
        var roots = order.Where(i => parents[i].Count == 0).ToList();
        if (roots.Count == 0) roots.Add(order[0]);
        var visiting = new HashSet<Guid>();

        void Visit(Guid id, int depth)
        {
            if (layer.TryGetValue(id, out var d) && d >= depth) return;
            if (!visiting.Add(id)) return; // cycle
            layer[id] = depth;
            foreach (var c in children[id]) Visit(c, depth + 1);
            visiting.Remove(id);
        }

        foreach (var r in roots) Visit(r, 0);
        foreach (var id in order.Where(i => !layer.ContainsKey(i))) Visit(id, 0);

        // Order inside a layer by the average position of the parents (barycenter), keeping document order otherwise.
        var layers = layer.GroupBy(kv => kv.Value).OrderBy(g => g.Key).Select(g => g.Select(kv => kv.Key).OrderBy(order.IndexOf).ToList()).ToList();
        var rank = new Dictionary<Guid, double>();
        foreach (var l in layers)
        {
            var sorted = l.Select((id, i) => (id, key: parents[id].Where(rank.ContainsKey).Select(p => rank[p]).DefaultIfEmpty(i).Average() + i * 1e-3))
                .OrderBy(t => t.key).Select(t => t.id).ToList();
            for (int i = 0; i < sorted.Count; i++) rank[sorted[i]] = i;
            l.Clear();
            l.AddRange(sorted);
        }

        double x = 0;
        foreach (var l in layers)
        {
            var colW = l.Max(id => size[id].W);
            var total = l.Sum(id => size[id].H) + gapY * (l.Count - 1);
            var y = -total / 2;
            foreach (var id in l)
            {
                result[id] = (x + (colW - size[id].W) / 2, y);
                y += size[id].H + gapY;
            }

            x += colW + gapX;
        }

        return result;
    }
}
