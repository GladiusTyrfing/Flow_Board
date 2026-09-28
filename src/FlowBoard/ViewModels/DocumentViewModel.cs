using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowBoard.Models;
using FlowBoard.Services;

namespace FlowBoard.ViewModels;

/// <summary>
/// Base for storyboards, canvases, notes and the dashboard shown in the main area.
/// Provides snapshot undo/redo scoped to the document (Ctrl+Z routes here while it is open).
/// </summary>
public abstract partial class DocumentViewModel : ObservableObject
{
    private readonly LinkedList<string> _undo = new();
    private readonly LinkedList<string> _redo = new();

    protected DocumentViewModel(MainViewModel main) => Main = main;

    public MainViewModel Main { get; }

    /// <summary>The persisted model (Storyboard, CanvasDoc, NotePage) or null for the dashboard.</summary>
    public abstract object? Model { get; }

    public abstract ActiveView Kind { get; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    protected virtual string Serialize() => Model == null ? string.Empty : JsonSerializer.Serialize(Model, Model.GetType(), Json.Compact);

    /// <summary>Replace the document content with a snapshot (copy fields into the existing model).</summary>
    protected virtual void Restore(string json)
    {
    }

    /// <summary>Call before a change that should be undoable as one step.</summary>
    public void Checkpoint()
    {
        if (Model == null) return;
        _undo.AddLast(Serialize());
        while (_undo.Count > 80) _undo.RemoveFirst();
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public bool Undo() => Swap(_undo, _redo);

    public bool Redo() => Swap(_redo, _undo);

    private bool Swap(LinkedList<string> from, LinkedList<string> to)
    {
        if (from.Last is not { } node) return false;
        from.RemoveLast();
        to.AddLast(Serialize());
        Restore(node.Value);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        return true;
    }

    /// <summary>F2: start renaming the document (if it has a name).</summary>
    public virtual void BeginRename()
    {
    }

    /// <summary>Called when the document is closed or another one is opened.</summary>
    public virtual void OnDeactivated()
    {
    }
}
