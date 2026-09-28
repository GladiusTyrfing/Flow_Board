using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FlowBoard.Models;

/// <summary>A column on a board ("To Do", "Doing", ...).</summary>
public partial class BoardList : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isCollapsed;
    /// <summary>Work-in-progress limit. 0 = unlimited.</summary>
    [ObservableProperty] private int _wipLimit;
    [ObservableProperty] private string? _color;
    /// <summary>When true, cards dropped into this list are marked complete.</summary>
    [ObservableProperty] private bool _isDoneList;
    [ObservableProperty] private DateTime? _archivedAt;
    [ObservableProperty] private ObservableCollection<Card> _cards = [];

    // ---- runtime-only UI state ----
    [ObservableProperty][property: JsonIgnore] private bool _isAddingCard;
    [ObservableProperty][property: JsonIgnore] private bool _isEditingName;
    [ObservableProperty][property: JsonIgnore] private string _newCardTitle = string.Empty;

    public BoardList()
    {
        Cards.CollectionChanged += OnCardsCollectionChanged;
    }

    [JsonIgnore] public int VisibleCount => Cards.Count(c => !c.IsFilteredOut);
    [JsonIgnore] public bool IsOverWip => WipLimit > 0 && Cards.Count > WipLimit;
    [JsonIgnore] public string CountText => WipLimit > 0 ? $"{Cards.Count}/{WipLimit}" : Cards.Count.ToString();
    [JsonIgnore] public bool HasColor => !string.IsNullOrEmpty(Color);

    partial void OnCardsChanged(ObservableCollection<Card>? oldValue, ObservableCollection<Card> newValue)
    {
        if (oldValue != null) oldValue.CollectionChanged -= OnCardsCollectionChanged;
        newValue.CollectionChanged += OnCardsCollectionChanged;
        RaiseCounts();
    }

    partial void OnWipLimitChanged(int value) => RaiseCounts();
    partial void OnColorChanged(string? value) => OnPropertyChanged(nameof(HasColor));

    private void OnCardsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RaiseCounts();

    public void RaiseCounts()
    {
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(IsOverWip));
        OnPropertyChanged(nameof(CountText));
    }
}
