using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FlowBoard.Models;

public partial class ChecklistItem : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private bool _isDone;

    [ObservableProperty][property: JsonIgnore] private bool _isEditing;
}

public partial class Checklist : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _title = "Checklist";
    [ObservableProperty] private bool _hideCompleted;
    [ObservableProperty] private ObservableCollection<ChecklistItem> _items = [];

    [ObservableProperty][property: JsonIgnore] private string _newItemText = string.Empty;

    public event EventHandler? ProgressChanged;

    public Checklist()
    {
        Hook(Items);
    }

    [JsonIgnore] public int DoneCount => Items.Count(i => i.IsDone);
    [JsonIgnore] public int TotalCount => Items.Count;
    [JsonIgnore] public double Percent => TotalCount == 0 ? 0 : Math.Round(100.0 * DoneCount / TotalCount);
    [JsonIgnore] public bool IsComplete => TotalCount > 0 && DoneCount == TotalCount;

    partial void OnItemsChanged(ObservableCollection<ChecklistItem>? oldValue, ObservableCollection<ChecklistItem> newValue)
    {
        if (oldValue != null) oldValue.CollectionChanged -= OnItemsCollectionChanged;
        Hook(newValue);
        RaiseProgress();
    }

    private void Hook(ObservableCollection<ChecklistItem> items)
    {
        items.CollectionChanged += OnItemsCollectionChanged;
        foreach (var i in items) i.PropertyChanged += OnItemPropertyChanged;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (ChecklistItem i in e.OldItems) i.PropertyChanged -= OnItemPropertyChanged;
        if (e.NewItems != null) foreach (ChecklistItem i in e.NewItems) i.PropertyChanged += OnItemPropertyChanged;
        RaiseProgress();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChecklistItem.IsDone)) RaiseProgress();
    }

    private void RaiseProgress()
    {
        OnPropertyChanged(nameof(DoneCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(IsComplete));
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }
}
