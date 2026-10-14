using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FlowBoard.Services;

/// <summary>
/// Updates live model objects from JSON <em>in place</em>: objects keep their identity (open dialogs, views and
/// timers stay attached), only values that differ are set, and lists of things with an <c>Id</c> are matched by id —
/// existing items are updated, new ones inserted, removed ones taken out, and the order fixed with moves.
/// </summary>
public static class ModelReconciler
{
    /// <summary>Applies <paramref name="source"/> to <paramref name="target"/>. Returns the objects whose own values changed.</summary>
    public static HashSet<object> Apply(object target, JsonObject source)
    {
        var changed = new HashSet<object>(ReferenceEqualityComparer.Instance);
        // Every existing item by id, so something moved to another list (a card to another column) stays the same object.
        _pool = new Dictionary<(Type, Guid), object>();
        try
        {
            Collect(target, new HashSet<object>(ReferenceEqualityComparer.Instance));
            // Two passes: some setters adjust other values (e.g. new text drops old formatting), the second pass settles them.
            ApplyObject(target, source, changed);
            ApplyObject(target, source, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }
        finally
        {
            _pool = null;
        }

        return changed;
    }

    [ThreadStatic] private static Dictionary<(Type, Guid), object>? _pool;

    private static void Collect(object obj, HashSet<object> seen)
    {
        if (!seen.Add(obj)) return;
        foreach (var prop in Props(obj.GetType()))
        {
            if (prop.GetValue(obj) is not IList list || ElementType(prop.PropertyType) is not { } elem || IdProperty(elem) is not { } idProp) continue;
            foreach (var item in list)
            {
                if (item == null) continue;
                if (idProp.GetValue(item) is Guid id) _pool!.TryAdd((elem, id), item);
                Collect(item, seen);
            }
        }
    }

    private static readonly Dictionary<Type, PropertyInfo[]> PropsCache = new();

    private static PropertyInfo[] Props(Type t)
    {
        lock (PropsCache)
        {
            if (PropsCache.TryGetValue(t, out var p)) return p;
            p = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.GetIndexParameters().Length == 0 && x.CanRead && x.GetCustomAttribute<JsonIgnoreAttribute>() == null)
                .ToArray();
            PropsCache[t] = p;
            return p;
        }
    }

    private static void ApplyObject(object target, JsonObject source, HashSet<object> changed)
    {
        foreach (var prop in Props(target.GetType()))
        {
            var current = prop.GetValue(target);
            source.TryGetPropertyValue(prop.Name, out var node);
            var type = prop.PropertyType;

            if (node == null)
            {
                // Nulls are left out of the file: a missing value means "empty".
                if (current != null && prop.CanWrite && (!type.IsValueType || Nullable.GetUnderlyingType(type) != null))
                {
                    prop.SetValue(target, null);
                    changed.Add(target);
                }

                continue;
            }

            if (current is IList list && node is JsonArray arr && ElementType(type) is { } elem)
            {
                if (IdProperty(elem) is { } idProp) ReconcileList(list, arr, elem, idProp, changed);
                else if (!JsonNode.DeepEquals(ToNode(current, type), arr))
                {
                    // Plain lists (label ids, tags without ids…): refill in place.
                    var fresh = (IList?)arr.Deserialize(type, Json.Options);
                    list.Clear();
                    if (fresh != null) foreach (var x in fresh) list.Add(x);
                    changed.Add(target);
                }

                continue;
            }

            if (!prop.CanWrite) continue;

            if (current != null && node is JsonObject obj && IsModel(type) && SameId(current, obj))
            {
                ApplyObject(current, obj, changed);
                continue;
            }

            if (!JsonNode.DeepEquals(ToNode(current, type), node))
            {
                prop.SetValue(target, node.Deserialize(type, Json.Options));
                changed.Add(target);
            }
        }
    }

    private static void ReconcileList(IList list, JsonArray arr, Type elem, PropertyInfo idProp, HashSet<object> changed)
    {
        var existing = new Dictionary<Guid, object>();
        foreach (var item in list)
            if (item != null && idProp.GetValue(item) is Guid id) existing.TryAdd(id, item);

        var desired = new List<object>();
        foreach (var n in arr)
        {
            if (n is not JsonObject o) continue;
            var id = o.TryGetPropertyValue(idProp.Name, out var idNode) && idNode is JsonValue v && v.TryGetValue<Guid>(out var g) ? g : Guid.Empty;
            if (id != Guid.Empty && existing.Remove(id, out var match))
            {
                _pool?.Remove((elem, id)); // stays in this list: not available to others
                ApplyObject(match, o, changed);
                desired.Add(match);
            }
            else if (id != Guid.Empty && _pool != null && _pool.Remove((elem, id), out var moved))
            {
                // Moved here from another list: keep the same object.
                ApplyObject(moved, o, changed);
                desired.Add(moved);
                changed.Add(moved);
            }
            else if (o.Deserialize(elem, Json.Options) is { } created)
            {
                desired.Add(created);
                changed.Add(created);
            }
        }

        if (SameSequence(list, desired)) return;

        // Take out what's gone, then put every item in its place (moves keep the objects and their views).
        var keep = new HashSet<object>(desired, ReferenceEqualityComparer.Instance);
        for (int i = list.Count - 1; i >= 0; i--)
            if (!keep.Contains(list[i]!)) list.RemoveAt(i);

        var move = list.GetType().GetMethod("Move", [typeof(int), typeof(int)]);
        for (int i = 0; i < desired.Count; i++)
        {
            var at = IndexOf(list, desired[i]);
            if (at == i) continue;
            if (at < 0) list.Insert(i, desired[i]);
            else if (move != null) move.Invoke(list, [at, i]);
            else
            {
                list.RemoveAt(at);
                list.Insert(i, desired[i]);
            }
        }
    }

    private static bool SameSequence(IList list, List<object> desired)
    {
        if (list.Count != desired.Count) return false;
        for (int i = 0; i < desired.Count; i++)
            if (!ReferenceEquals(list[i], desired[i])) return false;
        return true;
    }

    private static int IndexOf(IList list, object item)
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], item)) return i;
        return -1;
    }

    private static JsonNode? ToNode(object? value, Type type) => JsonSerializer.SerializeToNode(value, type, Json.Options);

    private static Type? ElementType(Type t)
    {
        if (t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(ObservableCollection<>) || t.GetGenericTypeDefinition() == typeof(List<>)))
            return t.GetGenericArguments()[0];
        return null;
    }

    private static PropertyInfo? IdProperty(Type t) =>
        IsModel(t) && t.GetProperty("Id") is { PropertyType: var pt } p && pt == typeof(Guid) ? p : null;

    private static bool IsModel(Type t) => t.IsClass && t != typeof(string) && t.Namespace == "FlowBoard.Models";

    private static bool SameId(object current, JsonObject obj)
    {
        var idProp = current.GetType().GetProperty("Id");
        if (idProp == null || idProp.PropertyType != typeof(Guid)) return true; // no id: same slot, same object
        return obj.TryGetPropertyValue("Id", out var n) && n is JsonValue v && v.TryGetValue<Guid>(out var g) && g == (Guid)idProp.GetValue(current)!;
    }
}
