using FlowBoard.Models;

namespace FlowBoard.Services;

/// <summary>
/// Finds what is connected to what across sections, so completion can follow links:
/// cards ⇄ storyboard shots (status Done / Approved) and cards ⇄ to-dos on pages.
/// </summary>
public static class LinkSync
{
    public static bool IsDoneStatus(string? status) => status != null && ProjectStats.DoneShotStatuses.Contains(status);

    /// <summary>Status a shot gets when its card is completed / reopened (null = leave it as it is).</summary>
    public static string? ShotStatusFor(bool cardDone, string? current) =>
        cardDone
            ? IsDoneStatus(current) ? null : "Done"
            : IsDoneStatus(current) ? "In progress" : null;

    public static IEnumerable<Card> AllCards(Workspace ws) =>
        ws.Boards.SelectMany(b => b.Lists.SelectMany(l => l.Cards).Concat(b.ArchivedCards));

    /// <summary>Cards that link to the given item.</summary>
    public static IEnumerable<Card> CardsLinkedTo(Workspace ws, LinkTarget kind, Guid id) =>
        AllCards(ws).Where(c => c.Links.Any(l => l.Kind == kind && l.Id == id));

    /// <summary>Shots a card links to.</summary>
    public static IEnumerable<Shot> ShotsOf(Workspace ws, Card card) =>
        card.Links.Where(l => l.Kind == LinkTarget.Shot)
            .Select(l => LinkResolver.FindShot(ws, l.Id, out _))
            .OfType<Shot>();

    /// <summary>To-do blocks on pages that stand for a card (made with "turn to-dos into cards" or linked by hand).</summary>
    public static IEnumerable<NoteBlock> TodosFor(Workspace ws, Guid cardId) =>
        ws.Notes.SelectMany(n => n.Blocks).Where(b => b.Type == BlockType.Todo && b.LinkKind == LinkTarget.Card && b.LinkId == cardId);

    public static bool ContainsShot(Workspace ws, Shot shot) => LinkResolver.FindShot(ws, shot.Id, out _) == shot;

    public static bool ContainsBlock(Workspace ws, NoteBlock block) => ws.Notes.Any(n => n.Blocks.Contains(block));
}
