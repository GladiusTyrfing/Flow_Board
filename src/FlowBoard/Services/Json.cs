using System.Text.Json;
using System.Text.Json.Serialization;
using FlowBoard.Models;

namespace FlowBoard.Services;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    public static readonly JsonSerializerOptions Compact = new(Options) { WriteIndented = false };

    public static string SerializeBoard(Board board) => JsonSerializer.Serialize(board, Compact);

    public static Board DeserializeBoard(string json)
    {
        var board = JsonSerializer.Deserialize<Board>(json, Options) ?? new Board();
        board.Hydrate();
        return board;
    }

    public static Board CloneWithNewIds(Board source, bool includeCards = true)
    {
        var copy = DeserializeBoard(SerializeBoard(source));
        copy.Id = Guid.NewGuid();
        copy.CreatedAt = DateTime.Now;
        var labelMap = new Dictionary<Guid, Guid>();
        foreach (var l in copy.Labels)
        {
            var id = Guid.NewGuid();
            labelMap[l.Id] = id;
            l.Id = id;
        }

        copy.ArchivedCards.Clear();
        copy.ArchivedLists.Clear();
        foreach (var list in copy.Lists)
        {
            list.Id = Guid.NewGuid();
            if (!includeCards)
            {
                list.Cards.Clear();
                continue;
            }

            foreach (var card in list.Cards) ReassignIds(card, labelMap);
        }

        copy.Hydrate();
        return copy;
    }

    public static T CloneDocument<T>(T doc) where T : class
    {
        var copy = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(doc, Compact), Options)!;
        switch (copy)
        {
            case Storyboard s: s.Id = Guid.NewGuid(); break;
            case CanvasDoc c: c.Id = Guid.NewGuid(); break;
            case NotePage n: n.Id = Guid.NewGuid(); break;
        }

        return copy;
    }

    public static void ReassignIds(Card card, IReadOnlyDictionary<Guid, Guid>? labelMap = null)
    {
        card.Id = Guid.NewGuid();
        if (labelMap != null)
        {
            var mapped = card.LabelIds.Where(labelMap.ContainsKey).Select(id => labelMap[id]).ToList();
            card.LabelIds.Clear();
            foreach (var id in mapped) card.LabelIds.Add(id);
        }

        foreach (var cl in card.Checklists)
        {
            cl.Id = Guid.NewGuid();
            foreach (var i in cl.Items) i.Id = Guid.NewGuid();
        }

        Guid? newCover = null;
        foreach (var a in card.Attachments)
        {
            var old = a.Id;
            a.Id = Guid.NewGuid();
            if (card.CoverAttachmentId == old) newCover = a.Id;
        }

        card.CoverAttachmentId = newCover;
        foreach (var c in card.Comments) c.Id = Guid.NewGuid();
        foreach (var t in card.TimeEntries) t.Id = Guid.NewGuid();
    }
}
