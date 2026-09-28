using System.Windows.Input;
using FlowBoard.Models;

namespace FlowBoard.ViewModels;

/// <summary>
/// Keyboard hotkeys that don't fit a plain KeyBinding: single-letter keys (only when not typing),
/// board navigation, and Trello-style "hover a card and press a key" actions.
/// </summary>
public sealed partial class MainViewModel
{
    private Card? _hoveredCard;
    private Card? _stickyCard;

    /// <summary>The card under the mouse on the board (set by the board view).</summary>
    public Card? HoveredCard
    {
        get => _hoveredCard;
        set
        {
            if (value != null && _stickyCard != null && _stickyCard != value) ClearSticky();
            _hoveredCard = value;
        }
    }

    /// <summary>The card hotkeys act on: hovered card, the card just moved by keyboard, or the table's selected row.</summary>
    public Card? HotkeyTarget =>
        _hoveredCard ?? _stickyCard ?? (ViewMode == BoardViewMode.Table ? Table.SelectedRow?.Card : null);

    private void ClearSticky()
    {
        if (_stickyCard != null) _stickyCard.IsHighlighted = false;
        _stickyCard = null;
    }

    private void MakeSticky(Card card)
    {
        ClearSticky();
        _stickyCard = card;
        card.IsHighlighted = true;
    }

    /// <summary>Handles a key press from the main window. Returns true when it was used.</summary>
    /// <param name="isTyping">True when a text box has focus: then only modifier shortcuts apply.</param>
    public bool HandleKey(Key key, ModifierKeys mods, bool isTyping)
    {
        // ----- Board navigation (work everywhere) -----
        if (key == Key.Tab && mods.HasFlag(ModifierKeys.Control))
        {
            CycleBoard(mods.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            return true;
        }

        if (mods == ModifierKeys.Alt && key is >= Key.D1 and <= Key.D9 && !HasDialog)
        {
            var boards = StarredBoards.Concat(Workspace.Boards.Where(b => !b.IsStarred)).ToList();
            var i = key - Key.D1;
            if (i < boards.Count) SelectBoard(boards[i]);
            return true;
        }

        if (isTyping || HasDialog) return false;

        // ----- Single keys (not while typing) -----
        if (mods == ModifierKeys.None)
        {
            switch (key)
            {
                case Key.Oem2: // "/"
                    OpenCommandPalette();
                    return true;
                case Key.OemOpenBrackets: // "["
                    ToggleSidebar();
                    return true;
                case Key.N:
                    AddCardNearHovered();
                    return true;
                case Key.Q:
                    OpenQuickAdd();
                    return true;
            }
        }

        if (mods == ModifierKeys.Shift && key == Key.Oem2) // "?"
        {
            OpenShortcuts();
            return true;
        }

        // ----- Card hotkeys -----
        if (!Settings.CardHoverHotkeys || HotkeyTarget is not { } card || card.Board is not { } board) return false;
        var list = board.FindListOf(card);
        if (list == null) return false;

        if (mods == ModifierKeys.Alt)
        {
            switch (key)
            {
                case Key.Left: MoveCardSideways(card, board, list, -1); return true;
                case Key.Right: MoveCardSideways(card, board, list, +1); return true;
                case Key.Up: MoveCardWithinList(card, board, list, -1); return true;
                case Key.Down: MoveCardWithinList(card, board, list, +1); return true;
                case Key.Home: MoveCardWithinList(card, board, list, int.MinValue); return true;
                case Key.End: MoveCardWithinList(card, board, list, int.MaxValue); return true;
            }

            return false;
        }

        if (mods == ModifierKeys.Control && key == Key.D)
        {
            DuplicateCard(card);
            return true;
        }

        if (mods != ModifierKeys.None) return false;

        switch (key)
        {
            case Key.Enter:
            case Key.Space:
            case Key.E:
                OpenCard(card);
                return true;
            case Key.X:
                ToggleComplete(card);
                return true;
            case Key.V:
                card.IsExpanded = !card.IsExpanded;
                return true;
            case Key.C:
                ArchiveCard(card);
                ClearSticky();
                return true;
            case Key.Delete:
                DeleteCardCommand.Execute(card);
                return true;
            case Key.P:
                Undo.Checkpoint(Workspace, board, "Change priority");
                card.Priority = card.Priority == Priority.Urgent ? Priority.None : card.Priority + 1;
                ShowToast($"Priority: {(card.Priority == Priority.None ? "none" : card.Priority.ToString())}");
                return true;
            case Key.T:
                ToggleCardTimer(card);
                ShowToast(card.IsTimerRunning ? $"Timer started for \"{card.Title}\"" : "Timer stopped");
                return true;
            case Key.F:
                Pomodoro.LinkCard(card);
                if (!Pomodoro.IsRunning) Pomodoro.Start();
                ShowToast($"Focus session started for \"{card.Title}\"");
                return true;
            case Key.D:
                OpenCardWithPopup(card, "dates");
                return true;
            case Key.L:
                OpenCardWithPopup(card, "labels");
                return true;
            case >= Key.D1 and <= Key.D9:
            case >= Key.NumPad1 and <= Key.NumPad9:
                var index = key >= Key.NumPad1 ? key - Key.NumPad1 : key - Key.D1;
                ToggleLabelByIndex(card, board, index);
                return true;
        }

        return false;
    }

    public void OpenCardWithPopup(Card card, string popup)
    {
        OpenCard(card);
        if (Dialogs.OfType<CardDetailViewModel>().LastOrDefault() is { } detail) detail.PendingPopup = popup;
    }

    private void CycleBoard(int dir)
    {
        if (Workspace.Boards.Count == 0) return;
        CloseAllDialogs();
        var i = CurrentBoard == null ? 0 : Workspace.Boards.IndexOf(CurrentBoard);
        i = (i + dir + Workspace.Boards.Count) % Workspace.Boards.Count;
        SelectBoard(Workspace.Boards[i]);
        ShowToast(Workspace.Boards[i].Name);
    }

    private void AddCardNearHovered()
    {
        if (CurrentBoard == null) return;
        ViewMode = BoardViewMode.Board;
        var list = HotkeyTarget is { } c ? CurrentBoard.FindListOf(c) : null;
        list ??= CurrentBoard.Lists.FirstOrDefault();
        if (list == null) StartAddList();
        else StartAddCard(list);
    }

    private void ToggleLabelByIndex(Card card, Board board, int index)
    {
        if (index < 0 || index >= board.Labels.Count)
        {
            ShowToast($"This board has {board.Labels.Count} labels.");
            return;
        }

        var label = board.Labels[index];
        Undo.Checkpoint(Workspace, board, "Toggle label");
        if (!card.LabelIds.Remove(label.Id)) card.LabelIds.Add(label.Id);
        ApplyFilter();
    }

    private void MoveCardSideways(Card card, Board board, BoardList from, int dir)
    {
        var target = board.Lists.IndexOf(from) + dir;
        if (target < 0 || target >= board.Lists.Count) return;
        var to = board.Lists[target];
        Undo.Checkpoint(Workspace, board, "Move card");
        var pos = from.Cards.IndexOf(card);
        from.Cards.Remove(card);
        to.Cards.Insert(Math.Min(pos, to.Cards.Count), card);
        to.IsCollapsed = false;
        AfterCardMoved(card, from, to);
        MakeSticky(card);
    }

    private void MoveCardWithinList(Card card, Board board, BoardList list, int delta)
    {
        var i = list.Cards.IndexOf(card);
        var j = delta switch
        {
            int.MinValue => 0,
            int.MaxValue => list.Cards.Count - 1,
            _ => i + delta,
        };
        if (j < 0 || j >= list.Cards.Count || j == i) return;
        Undo.Checkpoint(Workspace, board, "Reorder card");
        list.Cards.Move(i, j);
        MakeSticky(card);
    }
}
