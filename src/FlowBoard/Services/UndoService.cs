using CommunityToolkit.Mvvm.ComponentModel;
using FlowBoard.Models;

namespace FlowBoard.Services;

/// <summary>
/// Snapshot-based undo/redo. Before a change, the affected board is serialized; undoing swaps the
/// board back from that snapshot. A null snapshot means "the board did not exist" (creation/deletion).
/// </summary>
public sealed partial class UndoService : ObservableObject
{
    private const int MaxEntries = 80;

    private sealed record Entry(Guid BoardId, int Index, string? Json, string Description);

    private readonly LinkedList<Entry> _undo = new();
    private readonly LinkedList<Entry> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.Last?.Value.Description;
    public string? RedoDescription => _redo.Last?.Value.Description;

    public void Checkpoint(Workspace ws, Board board, string description)
    {
        Push(_undo, new Entry(board.Id, ws.Boards.IndexOf(board), Json.SerializeBoard(board), description));
        _redo.Clear();
        Raise();
    }

    /// <summary>Records a snapshot taken earlier (e.g. when a card dialog was opened).</summary>
    public void PushSnapshot(Guid boardId, int index, string json, string description)
    {
        Push(_undo, new Entry(boardId, index, json, description));
        _redo.Clear();
        Raise();
    }

    /// <summary>Records that a board is about to be created (undo removes it).</summary>
    public void CheckpointBoardCreated(Guid boardId, string description)
    {
        Push(_undo, new Entry(boardId, -1, null, description));
        _redo.Clear();
        Raise();
    }

    public Board? Undo(Workspace ws, out string? description) => Swap(ws, _undo, _redo, out description);

    public Board? Redo(Workspace ws, out string? description) => Swap(ws, _redo, _undo, out description);

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Raise();
    }

    private Board? Swap(Workspace ws, LinkedList<Entry> from, LinkedList<Entry> to, out string? description)
    {
        description = null;
        if (from.Last is not { } node) return null;
        from.RemoveLast();
        var entry = node.Value;
        description = entry.Description;

        var existing = ws.Boards.FirstOrDefault(b => b.Id == entry.BoardId);
        var currentIndex = existing != null ? ws.Boards.IndexOf(existing) : entry.Index;
        Push(to, new Entry(entry.BoardId, currentIndex, existing != null ? Json.SerializeBoard(existing) : null, entry.Description));

        Board? result = null;
        if (entry.Json == null)
        {
            if (existing != null) ws.Boards.Remove(existing);
        }
        else
        {
            var restored = Json.DeserializeBoard(entry.Json);
            if (existing != null)
            {
                ws.Boards[ws.Boards.IndexOf(existing)] = restored;
            }
            else
            {
                var idx = Math.Clamp(entry.Index, 0, ws.Boards.Count);
                ws.Boards.Insert(idx, restored);
            }

            result = restored;
        }

        Raise();
        return result;
    }

    private static void Push(LinkedList<Entry> list, Entry e)
    {
        list.AddLast(e);
        while (list.Count > MaxEntries) list.RemoveFirst();
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoDescription));
        OnPropertyChanged(nameof(RedoDescription));
    }
}
