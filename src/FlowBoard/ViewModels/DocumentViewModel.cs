using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowBoard.Models;
using FlowBoard.Services;

namespace FlowBoard.ViewModels;

public abstract partial class DocumentViewModel : ObservableObject
{
    private readonly LinkedList<string> _undo = new();
    private readonly LinkedList<string> _redo = new();

    protected DocumentViewModel(MainViewModel main) => Main = main;

    public MainViewModel Main { get; }

    public abstract object? Model { get; }

    public abstract ActiveView Kind { get; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    protected virtual string Serialize() => Model == null ? string.Empty : JsonSerializer.Serialize(Model, Model.GetType(), Json.Compact);

    protected virtual void Restore(string json)
    {
    }

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

    public void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public virtual void OnExternalChange()
    {
    }

    public virtual void BeginRename()
    {
    }

    public virtual void OnDeactivated()
    {
    }
}
