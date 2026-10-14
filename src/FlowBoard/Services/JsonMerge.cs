using System.Text.Json.Nodes;

namespace FlowBoard.Services;

/// <summary>What a merge did, for the "updated by …" message.</summary>
public sealed class MergeStats
{
    /// <summary>Places where both sides changed the same value differently (this side's value was kept).</summary>
    public int Conflicts { get; set; }
}

/// <summary>
/// Three-way merge of two edited copies of a project (ours and theirs) against the version both started from (base).
/// Works on the JSON of the whole project, so it covers every section:
/// <list type="bullet">
/// <item>objects merge property by property: a value changed on one side wins; changed differently on both sides, ours wins (counted as a conflict);</item>
/// <item>lists of things with an <c>Id</c> (boards, lists, cards, shots, shapes, blocks…) merge item by item: additions from both sides are kept,
/// deletions are applied, an item deleted on one side but edited on the other is kept, and an item moved (e.g. a card to another list) moves;</item>
/// <item>lists of plain values (label ids, linked card ids…) merge as sets;</item>
/// <item>other lists are treated as one value.</item>
/// </list>
/// Pure (no file or UI access) so it can be tested.
/// </summary>
public static class JsonMerge
{
    private const string IdKey = "Id";

    public static JsonNode? Merge(JsonNode? @base, JsonNode? ours, JsonNode? theirs, MergeStats? stats = null)
    {
        stats ??= new MergeStats();
        var ctx = new Context(stats, Index(@base), Index(ours), Index(theirs));
        return MergeNode(@base, ours, theirs, ctx);
    }

    public static string Merge(string @base, string ours, string theirs, MergeStats? stats = null) =>
        Merge(JsonNode.Parse(@base), JsonNode.Parse(ours), JsonNode.Parse(theirs), stats)?.ToJsonString(Json.Options) ?? "null";

    private sealed record Context(MergeStats Stats, Dictionary<string, JsonObject> Base, Dictionary<string, JsonObject> Ours, Dictionary<string, JsonObject> Theirs);

    private static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);

    private static JsonNode? Clone(JsonNode? n) => n?.DeepClone();

    private static JsonNode? MergeNode(JsonNode? b, JsonNode? o, JsonNode? t, Context ctx, string? arrayName = null)
    {
        if (Same(o, t)) return Clone(o);
        // Only one side changed this part. Items inside it may still have been edited by the other side
        // somewhere else (e.g. a card they moved into a list we didn't touch), so those are merged too.
        if (Same(o, b)) return Reconcile(Clone(t), ctx);
        if (Same(t, b)) return Reconcile(Clone(o), ctx);

        if (o is JsonObject oo && t is JsonObject to) return MergeObject(b as JsonObject, oo, to, ctx);
        if (o is JsonArray oa && t is JsonArray ta) return MergeArray(b as JsonArray, oa, ta, ctx, arrayName);

        ctx.Stats.Conflicts++;
        return Clone(o);
    }

    /// <summary>Re-merges items (anywhere inside <paramref name="node"/>) that both sides changed.</summary>
    private static JsonNode? Reconcile(JsonNode? node, Context ctx, string? name = null)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    var child = obj[key];
                    var fixedChild = Reconcile(child, ctx, key);
                    if (!ReferenceEquals(child, fixedChild)) obj[key] = fixedChild;
                }

                return obj;
            case JsonArray arr:
                for (int i = 0; i < arr.Count; i++)
                {
                    if (name != null && IdOf(arr[i]) is { } id)
                    {
                        var key = name + ":" + id;
                        if (ctx.Ours.TryGetValue(key, out var o) && ctx.Theirs.TryGetValue(key, out var t) && !Same(o, t))
                        {
                            ctx.Base.TryGetValue(key, out var b);
                            arr[i] = MergeNode(b, o, t, ctx);
                            continue;
                        }
                    }

                    var item = arr[i];
                    var fixedItem = Reconcile(item, ctx, name);
                    if (!ReferenceEquals(item, fixedItem)) arr[i] = fixedItem;
                }

                return arr;
            default:
                return node;
        }
    }

    private static JsonObject MergeObject(JsonObject? b, JsonObject o, JsonObject t, Context ctx)
    {
        var result = new JsonObject();
        // Keep ours' property order, then anything only theirs has.
        var keys = o.Select(p => p.Key).Concat(t.Select(p => p.Key).Where(k => !o.ContainsKey(k))).ToList();
        foreach (var key in keys)
        {
            var bv = b != null && b.TryGetPropertyValue(key, out var x) ? x : null;
            var hasB = b != null && b.ContainsKey(key);
            var hasO = o.TryGetPropertyValue(key, out var ov);
            var hasT = t.TryGetPropertyValue(key, out var tv);

            // Missing properties (nulls are left out when saving): treat like the value being removed.
            if (!hasO && !hasT) continue;
            if (!hasO && hasB && Same(tv, bv)) continue;  // ours removed it, theirs untouched
            if (!hasT && hasB && Same(ov, bv)) continue;  // theirs removed it, ours untouched

            var merged = MergeNode(bv, hasO ? ov : null, hasT ? tv : null, ctx, key);
            if (merged != null) result[key] = merged;
        }

        return result;
    }

    private static JsonNode? MergeArray(JsonArray? b, JsonArray o, JsonArray t, Context ctx, string? name)
    {
        if (IsIdArray(o) && IsIdArray(t) && (b == null || IsIdArray(b))) return MergeIdArray(b, o, t, ctx, name ?? "");
        if (IsValueArray(o) && IsValueArray(t) && (b == null || IsValueArray(b))) return MergeSet(b, o, t);
        ctx.Stats.Conflicts++;
        return Clone(o);
    }

    // ---------- lists of items with an Id ----------

    private static bool IsIdArray(JsonArray a) => a.Count > 0 && a.All(n => n is JsonObject obj && IdOf(obj) != null) || a.Count == 0;

    private static string? IdOf(JsonNode? n) => n is JsonObject o && o.TryGetPropertyValue(IdKey, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;

    private static JsonArray MergeIdArray(JsonArray? b, JsonArray o, JsonArray t, Context ctx, string name)
    {
        var bIds = (b ?? []).Select(IdOf).OfType<string>().ToList();
        var oIds = o.Select(IdOf).OfType<string>().ToList();
        var tIds = t.Select(IdOf).OfType<string>().ToList();
        var bSet = bIds.ToHashSet();
        var oSet = oIds.ToHashSet();
        var tSet = tIds.ToHashSet();

        bool Keep(string id)
        {
            var inO = oSet.Contains(id);
            var inT = tSet.Contains(id);
            if (!bSet.Contains(id)) return inO || inT;  // added on a side
            if (inO && inT) return true;
            // Removed from this list on one side.
            var removedByOurs = !inO;
            var remover = removedByOurs ? ctx.Ours : ctx.Theirs;
            var other = removedByOurs ? ctx.Theirs : ctx.Ours;
            var key = name + ":" + id;
            if (remover.ContainsKey(key)) return false;               // moved somewhere else: follow the move
            // Deleted: stays deleted unless the other side changed it meanwhile.
            return other.TryGetValue(key, out var edited) && ctx.Base.TryGetValue(key, out var original) && !Same(edited, original);
        }

        // Order: whichever side reordered wins; new items from the other side go before the item that follows them.
        var theirOrderChanged = !bIds.Where(tSet.Contains).SequenceEqual(tIds.Where(bSet.Contains));
        var ourOrderChanged = !bIds.Where(oSet.Contains).SequenceEqual(oIds.Where(bSet.Contains));
        var useTheirs = theirOrderChanged && !ourOrderChanged;
        var primary = useTheirs ? tIds : oIds;
        var secondary = useTheirs ? oIds : tIds;
        var order = primary.Where(Keep).ToList();
        for (int i = 0; i < secondary.Count; i++)
        {
            var id = secondary[i];
            if (order.Contains(id) || !Keep(id)) continue;
            var next = secondary.Skip(i + 1).FirstOrDefault(order.Contains);
            order.Insert(next == null ? order.Count : order.IndexOf(next), id);
        }

        var result = new JsonArray();
        foreach (var id in order.Distinct())
        {
            var key = name + ":" + id;
            // The item's content merges from wherever it lives on each side (it may have moved lists).
            ctx.Base.TryGetValue(key, out var bi);
            ctx.Ours.TryGetValue(key, out var oi);
            ctx.Theirs.TryGetValue(key, out var ti);
            var merged = MergeNode(bi, oi ?? (JsonNode?)ti, ti ?? (JsonNode?)oi, ctx);
            if (merged != null) result.Add(merged);
        }

        return result;
    }

    /// <summary>Every object with an Id, by "listName:id" (ids are unique within a kind of list, e.g. all "Cards").</summary>
    private static Dictionary<string, JsonObject> Index(JsonNode? root)
    {
        var map = new Dictionary<string, JsonObject>();

        void Walk(JsonNode? n, string? name)
        {
            switch (n)
            {
                case JsonObject obj:
                    foreach (var (k, v) in obj) Walk(v, k);
                    break;
                case JsonArray arr:
                    foreach (var item in arr)
                    {
                        if (item is JsonObject io && IdOf(io) is { } id && name != null) map.TryAdd(name + ":" + id, io);
                        Walk(item, name);
                    }

                    break;
            }
        }

        Walk(root, null);
        return map;
    }

    // ---------- lists of plain values ----------

    private static bool IsValueArray(JsonArray a) => a.All(n => n is JsonValue);

    private static JsonArray MergeSet(JsonArray? b, JsonArray o, JsonArray t)
    {
        string K(JsonNode? n) => n?.ToJsonString() ?? "null";
        var bSet = (b ?? []).Select(K).ToHashSet();
        var tSet = t.Select(K).ToHashSet();
        var oSet = o.Select(K).ToHashSet();
        var result = new JsonArray();
        var seen = new HashSet<string>();
        foreach (var n in o)
        {
            var k = K(n);
            // Removed by theirs (was there before, now gone) and not re-added by us.
            if (bSet.Contains(k) && !tSet.Contains(k)) continue;
            if (seen.Add(k)) result.Add(Clone(n));
        }

        foreach (var n in t)
        {
            var k = K(n);
            if (!bSet.Contains(k) && !oSet.Contains(k) && seen.Add(k)) result.Add(Clone(n)); // added by theirs
        }

        return result;
    }
}
