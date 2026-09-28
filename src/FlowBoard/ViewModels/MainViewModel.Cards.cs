using System.IO;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;

namespace FlowBoard.ViewModels;

public sealed partial class MainViewModel
{
    private string Me => Settings.DisplayName;

    // ================= Lists =================

    [RelayCommand]
    private void StartAddList()
    {
        if (CurrentBoard == null) return;
        ViewMode = BoardViewMode.Board;
        NewListName = string.Empty;
        IsAddingList = true;
    }

    [RelayCommand]
    private void CommitAddList()
    {
        if (CurrentBoard == null) return;
        var name = NewListName.Trim();
        if (name.Length == 0)
        {
            IsAddingList = false;
            return;
        }

        Undo.Checkpoint(Workspace, CurrentBoard, "Add list");
        CurrentBoard.Lists.Add(new BoardList { Name = name });
        NewListName = string.Empty;
        // Stay in "adding" mode so several lists can be added in a row, like Trello.
    }

    [RelayCommand]
    private void CancelAddList()
    {
        IsAddingList = false;
        NewListName = string.Empty;
    }

    [RelayCommand]
    private void StartRenameList(BoardList list) => list.IsEditingName = true;

    [RelayCommand]
    private void CommitListName(BoardList list)
    {
        list.IsEditingName = false;
        if (string.IsNullOrWhiteSpace(list.Name)) list.Name = "Untitled list";
    }

    [RelayCommand]
    private void ToggleCollapseList(BoardList list) => list.IsCollapsed = !list.IsCollapsed;

    [RelayCommand]
    private void ToggleDoneList(BoardList list) => list.IsDoneList = !list.IsDoneList;

    [RelayCommand]
    private void SetListColor(object? parameter)
    {
        if (parameter is not object[] { Length: 2 } args || args[0] is not BoardList list) return;
        list.Color = args[1] as string;
    }

    [RelayCommand]
    private async Task SetWipLimit(BoardList list)
    {
        var text = await PromptAsync(
            "Work-in-progress limit",
            "The card count turns red when a list holds more cards than this. Enter 0 for no limit.",
            "e.g. 5", "Set limit", list.WipLimit > 0 ? list.WipLimit.ToString() : string.Empty);
        if (text != null && int.TryParse(text, out var n) && n >= 0) list.WipLimit = n;
    }

    [RelayCommand]
    private void CopyList(BoardList list)
    {
        if (CurrentBoard == null) return;
        Undo.Checkpoint(Workspace, CurrentBoard, "Copy list");
        var copy = new BoardList { Name = list.Name + " (copy)", Color = list.Color, WipLimit = list.WipLimit, IsDoneList = list.IsDoneList };
        foreach (var c in list.Cards)
        {
            var clone = CloneCard(c);
            copy.Cards.Add(clone);
        }

        CurrentBoard.Lists.Insert(CurrentBoard.Lists.IndexOf(list) + 1, copy);
    }

    [RelayCommand]
    private void SortList(object? parameter)
    {
        if (parameter is not object[] { Length: 2 } args || args[0] is not BoardList list || args[1] is not string by) return;
        if (CurrentBoard == null) return;
        Undo.Checkpoint(Workspace, CurrentBoard, "Sort list");
        IEnumerable<Card> sorted = by switch
        {
            "due" => list.Cards.OrderBy(c => c.DueDate ?? DateTime.MaxValue),
            "priority" => list.Cards.OrderByDescending(c => c.Priority),
            "title" => list.Cards.OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase),
            "newest" => list.Cards.OrderByDescending(c => c.CreatedAt),
            "oldest" => list.Cards.OrderBy(c => c.CreatedAt),
            _ => list.Cards,
        };
        var items = sorted.ToList();
        for (int i = 0; i < items.Count; i++)
        {
            var from = list.Cards.IndexOf(items[i]);
            if (from != i) list.Cards.Move(from, i);
        }
    }

    [RelayCommand]
    private void ArchiveList(BoardList list)
    {
        if (CurrentBoard == null) return;
        Undo.Checkpoint(Workspace, CurrentBoard, "Archive list");
        list.ArchivedAt = DateTime.Now;
        CurrentBoard.Lists.Remove(list);
        CurrentBoard.ArchivedLists.Add(list);
        ShowToast($"List \"{list.Name}\" archived", "Undo", () => UndoCommand.Execute(null));
    }

    [RelayCommand]
    private void ArchiveAllCards(BoardList list)
    {
        if (CurrentBoard == null || list.Cards.Count == 0) return;
        Undo.Checkpoint(Workspace, CurrentBoard, "Archive all cards");
        foreach (var c in list.Cards.ToList()) ArchiveCardCore(c, list);
        ShowToast("All cards in the list archived", "Undo", () => UndoCommand.Execute(null));
    }

    [RelayCommand]
    private void RestoreList(BoardList list)
    {
        if (CurrentBoard == null) return;
        Undo.Checkpoint(Workspace, CurrentBoard, "Restore list");
        CurrentBoard.ArchivedLists.Remove(list);
        list.ArchivedAt = null;
        CurrentBoard.Lists.Add(list);
        ApplyFilter();
    }

    [RelayCommand]
    private async Task DeleteList(BoardList list)
    {
        if (CurrentBoard == null) return;
        if (list.Cards.Count > 0)
        {
            var ok = await ConfirmAsync("Delete list?", $"\"{list.Name}\" and its {list.Cards.Count} cards will be deleted. You can undo this with Ctrl+Z.", "Delete", danger: true);
            if (!ok) return;
        }

        Undo.Checkpoint(Workspace, CurrentBoard, "Delete list");
        CurrentBoard.Lists.Remove(list);
        CurrentBoard.ArchivedLists.Remove(list);
        ShowToast($"List \"{list.Name}\" deleted", "Undo", () => UndoCommand.Execute(null));
    }

    // ================= Cards =================

    [RelayCommand]
    private void StartAddCard(BoardList list)
    {
        if (CurrentBoard == null) return;
        foreach (var l in CurrentBoard.Lists) l.IsAddingCard = false;
        list.IsCollapsed = false;
        list.NewCardTitle = string.Empty;
        list.IsAddingCard = true;
    }

    [RelayCommand]
    private void CommitAddCard(BoardList list)
    {
        if (CurrentBoard == null) return;
        var title = list.NewCardTitle.Trim();
        if (title.Length == 0)
        {
            list.IsAddingCard = false;
            return;
        }

        Undo.Checkpoint(Workspace, CurrentBoard, "Add card");
        // Several lines pasted at once become several cards.
        foreach (var line in title.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            var card = new Card { Title = line, Board = CurrentBoard, IsCompleted = list.IsDoneList };
            card.AddActivity($"added this card to {list.Name}", Me);
            list.Cards.Add(card);
            card.IsFilteredOut = !Filter.Matches(card);
        }

        list.NewCardTitle = string.Empty;
    }

    [RelayCommand]
    private void CancelAddCard(BoardList list)
    {
        list.IsAddingCard = false;
        list.NewCardTitle = string.Empty;
    }

    /// <summary>Ctrl+N: start adding a card in the first list.</summary>
    [RelayCommand]
    private void QuickAddCard()
    {
        if (CurrentBoard == null)
        {
            NewBoard();
            return;
        }

        ViewMode = BoardViewMode.Board;
        CloseAllDialogs();
        var list = CurrentBoard.Lists.FirstOrDefault();
        if (list == null)
        {
            StartAddList();
            return;
        }

        StartAddCard(list);
    }

    [RelayCommand]
    public void OpenCard(Card card)
    {
        var board = card.Board ?? CurrentBoard;
        if (board == null) return;
        foreach (var d in Dialogs.OfType<CardDetailViewModel>().ToList()) CloseDialog(d);
        ShowDialog(new CardDetailViewModel(this, card, board));
    }

    public void OnCardDialogClosed()
    {
        ApplyFilter();
    }

    [RelayCommand]
    private void ToggleComplete(Card card)
    {
        if (card.Board is { } b) Undo.Checkpoint(Workspace, b, card.IsCompleted ? "Mark incomplete" : "Mark complete");
        card.IsCompleted = !card.IsCompleted;
        card.AddActivity(card.IsCompleted ? "marked this card as complete" : "marked this card as incomplete", Me);
        if (ViewMode != BoardViewMode.Board) RefreshViews();
    }

    [RelayCommand]
    private void ArchiveCard(Card card)
    {
        var board = card.Board ?? CurrentBoard;
        var list = board?.FindListOf(card);
        if (board == null || list == null) return;
        Undo.Checkpoint(Workspace, board, "Archive card");
        ArchiveCardCore(card, list);
        RefreshViews();
        ShowToast($"Archived \"{card.Title}\"", "Undo", () => UndoCommand.Execute(null));
    }

    private void ArchiveCardCore(Card card, BoardList list)
    {
        if (card.IsTimerRunning) Timer.Stop();
        list.Cards.Remove(card);
        card.ArchivedFromListId = list.Id;
        card.ArchivedAt = DateTime.Now;
        card.AddActivity("archived this card", Me);
        card.Board?.ArchivedCards.Insert(0, card);
    }

    [RelayCommand]
    private void RestoreCard(Card card)
    {
        var board = card.Board ?? CurrentBoard;
        if (board == null || !board.ArchivedCards.Contains(card)) return;
        if (board.Lists.Count == 0)
        {
            ShowToast("Add a list first so the card has somewhere to go.", isError: true);
            return;
        }

        Undo.Checkpoint(Workspace, board, "Restore card");
        board.ArchivedCards.Remove(card);
        var list = board.Lists.FirstOrDefault(l => l.Id == card.ArchivedFromListId) ?? board.Lists[0];
        card.ArchivedFromListId = null;
        card.ArchivedAt = null;
        card.AddActivity("sent this card back to the board", Me);
        list.Cards.Add(card);
        ApplyFilter();
    }

    [RelayCommand]
    private async Task DeleteArchivedCard(Card card)
    {
        var ok = await ConfirmAsync("Delete card?", $"\"{card.Title}\" will be permanently deleted.", "Delete", danger: true);
        if (ok && card.Board is { } board) DeleteCardPermanently(card, board);
    }

    [RelayCommand]
    private async Task DeleteCard(Card card)
    {
        var ok = await ConfirmAsync("Delete card?", $"\"{card.Title}\" will be permanently deleted. You can undo this with Ctrl+Z.", "Delete", danger: true);
        if (ok && (card.Board ?? CurrentBoard) is { } board) DeleteCardPermanently(card, board);
    }

    [RelayCommand]
    private void ToggleCardExpanded(Card card) => card.IsExpanded = !card.IsExpanded;

    [RelayCommand]
    private void ExpandAllCards(BoardList? list) => SetExpanded(list, true);

    [RelayCommand]
    private void CollapseAllCards(BoardList? list) => SetExpanded(list, false);

    private void SetExpanded(BoardList? list, bool expanded)
    {
        var cards = list?.Cards ?? CurrentBoard?.AllActiveCards ?? [];
        foreach (var c in cards) c.IsExpanded = expanded;
    }

    [RelayCommand]
    private void ToggleVoice(Attachment a)
    {
        try
        {
            Player.Toggle(a);
        }
        catch (Exception ex)
        {
            ShowToast($"Couldn't play: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void PreviewImage(Attachment a) => ShowDialog(new ImagePreviewViewModel(a));

    [RelayCommand]
    private void ToggleCardTimer(Card card)
    {
        if (card.IsTimerRunning) Timer.Stop();
        else Timer.Start(card);
    }

    public void DeleteCardPermanently(Card card, Board board)
    {
        Undo.Checkpoint(Workspace, board, "Delete card");
        if (card.IsTimerRunning) Timer.Stop();
        board.FindListOf(card)?.Cards.Remove(card);
        board.ArchivedCards.Remove(card);
        // Attachment files are kept until the next start so that undo can bring the card back intact.
        _pendingAttachmentDeletes.Add(card.Id);
        RefreshViews();
        ShowToast($"Deleted \"{card.Title}\"", "Undo", () => UndoCommand.Execute(null));
    }

    private readonly HashSet<Guid> _pendingAttachmentDeletes = [];

    /// <summary>On exit: remove attachment folders of cards that were deleted and not restored.</summary>
    public void PurgeDeletedAttachments()
    {
        foreach (var id in _pendingAttachmentDeletes)
        {
            if (Workspace.FindCard(id, out _, out _) != null) continue;
            if (Workspace.UserTemplates.Any(t => t.BoardJson?.Contains(id.ToString()) == true)) continue;
            try
            {
                var dir = Path.Combine(AppPaths.AttachmentsDir, id.ToString("N"));
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch
            {
                // Ignore; the folder is harmless.
            }
        }
    }

    [RelayCommand]
    private void DuplicateCard(Card card)
    {
        var board = card.Board ?? CurrentBoard;
        var list = board?.FindListOf(card);
        if (board == null || list == null) return;
        Undo.Checkpoint(Workspace, board, "Duplicate card");
        var copy = CloneCard(card);
        list.Cards.Insert(list.Cards.IndexOf(card) + 1, copy);
        ShowToast("Card duplicated");
    }

    /// <summary>Deep copy (new ids). Attachment files are copied so each card owns its files.</summary>
    private Card CloneCard(Card card)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(card, Json.Compact);
        var copy = System.Text.Json.JsonSerializer.Deserialize<Card>(json, Json.Options)!;
        Json.ReassignIds(copy);
        copy.Title = card.Title;
        copy.Board = card.Board;
        copy.CreatedAt = DateTime.Now;
        copy.TimeEntries.Clear();
        copy.Comments.Clear();
        copy.AddActivity($"copied this card from \"{card.Title}\"", Me);
        foreach (var a in copy.Attachments.Where(a => a.RelativePath != null).ToList())
        {
            try
            {
                var src = AppPaths.ToFull(a.RelativePath!);
                var dest = AppPaths.UniquePath(AppPaths.CardFolder(copy.Id), Path.GetFileName(src));
                File.Copy(src, dest);
                a.RelativePath = AppPaths.ToRelative(dest);
            }
            catch
            {
                copy.Attachments.Remove(a);
            }
        }

        return copy;
    }

    public void MoveCardToBoard(Card card, Board from, Board to)
    {
        if (to.Lists.Count == 0)
        {
            ShowToast($"\"{to.Name}\" has no lists yet.", isError: true);
            return;
        }

        Undo.Checkpoint(Workspace, from, "Move card to another board");
        Undo.Checkpoint(Workspace, to, "Move card to another board");
        from.FindListOf(card)?.Cards.Remove(card);
        from.ArchivedCards.Remove(card);

        // Labels are board-specific: map by name+color, create missing ones.
        var newIds = new List<Guid>();
        foreach (var label in card.ResolvedLabels)
        {
            var match = to.Labels.FirstOrDefault(l => l.Name == label.Name && l.Color == label.Color);
            if (match == null)
            {
                match = new Label { Name = label.Name, Color = label.Color };
                to.Labels.Add(match);
            }

            newIds.Add(match.Id);
        }

        card.LabelIds.Clear();
        foreach (var id in newIds) card.LabelIds.Add(id);
        card.Board = to;
        card.AddActivity($"moved this card from board \"{from.Name}\" to \"{to.Name}\"", Me);
        to.Lists[0].Cards.Insert(0, card);
        RefreshViews();
        ShowToast($"Moved to \"{to.Name}\"", "Go to board", () => SelectBoard(to));
    }

    [RelayCommand]
    private void SetCardPriority(object? parameter)
    {
        if (parameter is not object[] { Length: 2 } args || args[0] is not Card card || args[1] is not Priority p) return;
        if (card.Board is { } b) Undo.Checkpoint(Workspace, b, "Set priority");
        card.Priority = p;
    }

    /// <summary>Common bookkeeping after a card changed lists (drag & drop or the list picker).</summary>
    public void AfterCardMoved(Card card, BoardList? from, BoardList? to)
    {
        if (to == null) return;
        if (from != null && from != to)
        {
            card.AddActivity($"moved this card from {from.Name} to {to.Name}", Me);
            if (to.IsDoneList && !card.IsCompleted)
            {
                card.IsCompleted = true;
                if (card.IsTimerRunning) Timer.Stop();
            }
            else if (from.IsDoneList && !to.IsDoneList && card.IsCompleted)
            {
                card.IsCompleted = false;
            }
        }

        from?.RaiseCounts();
        to.RaiseCounts();
    }

    private void OnFocusLogged(Guid cardId, DateTime start, DateTime end)
    {
        var card = Workspace.FindCard(cardId, out _, out _);
        card?.TimeEntries.Add(new TimeEntry { Start = start, End = end, Source = "Focus" });
    }
}
