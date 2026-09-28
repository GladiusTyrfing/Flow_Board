using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FlowBoard.Models;

public partial class Board : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _name = "Untitled board";
    [ObservableProperty] private bool _isStarred;
    /// <summary>"gradient:#A,#B", "color:#A" or "image:relative/path.jpg".</summary>
    [ObservableProperty] private string _background = "gradient:#0C66E4,#9F6FEF";
    [ObservableProperty] private double _backgroundDim = 0.15;
    /// <summary>Board style preset (see BoardThemeService): "Auto" follows the app theme.</summary>
    [ObservableProperty] private string _theme = "Auto";
    /// <summary>0 = fully transparent lists, 1 = solid.</summary>
    [ObservableProperty] private double _listOpacity = 0.82;
    [ObservableProperty] private double _cardOpacity = 0.96;
    /// <summary>Blur radius applied to the board wallpaper (0 = sharp).</summary>
    [ObservableProperty] private double _backgroundBlur;
    /// <summary>Corner roundness for this board's lists and cards; negative = use the app setting.</summary>
    [ObservableProperty] private double _cornerRadius = -1;
    /// <summary>Draw curved lines between linked cards on the board.</summary>
    [ObservableProperty] private bool _showLinks = true;
    /// <summary>Timeline zoom: pixels per day.</summary>
    [ObservableProperty] private double _timelineDayWidth = 36;
    [ObservableProperty] private DateTime _createdAt = DateTime.Now;
    [ObservableProperty] private DateTime _lastOpened = DateTime.Now;
    [ObservableProperty] private BoardViewMode _viewMode = BoardViewMode.Board;
    [ObservableProperty] private ObservableCollection<Label> _labels = [];
    [ObservableProperty] private ObservableCollection<BoardList> _lists = [];
    [ObservableProperty] private ObservableCollection<BoardList> _archivedLists = [];
    [ObservableProperty] private ObservableCollection<Card> _archivedCards = [];

    [ObservableProperty][property: JsonIgnore] private bool _isEditingName;

    public Board()
    {
        Lists.CollectionChanged += OnListsCollectionChanged;
        ArchivedCards.CollectionChanged += OnArchiveChanged;
        ArchivedLists.CollectionChanged += OnArchiveChanged;
    }

    [JsonIgnore] public IEnumerable<Card> AllActiveCards => Lists.SelectMany(l => l.Cards);
    [JsonIgnore] public int ActiveCardCount => Lists.Sum(l => l.Cards.Count);
    [JsonIgnore] public int ArchivedCount => ArchivedCards.Count + ArchivedLists.Count;

    /// <summary>Sets runtime back-references after loading or cloning.</summary>
    public void Hydrate()
    {
        foreach (var list in Lists)
            foreach (var c in list.Cards) c.Board = this;
        foreach (var list in ArchivedLists)
            foreach (var c in list.Cards) c.Board = this;
        foreach (var c in ArchivedCards) c.Board = this;
    }

    public BoardList? FindListOf(Card card) => Lists.FirstOrDefault(l => l.Cards.Contains(card));

    public void NotifyLabelsChanged()
    {
        foreach (var l in Lists) foreach (var c in l.Cards) c.NotifyLabelsChanged();
        foreach (var c in ArchivedCards) c.NotifyLabelsChanged();
    }

    partial void OnListsChanged(ObservableCollection<BoardList>? oldValue, ObservableCollection<BoardList> newValue)
    {
        if (oldValue != null) oldValue.CollectionChanged -= OnListsCollectionChanged;
        newValue.CollectionChanged += OnListsCollectionChanged;
        OnPropertyChanged(nameof(ActiveCardCount));
    }

    partial void OnArchivedCardsChanged(ObservableCollection<Card>? oldValue, ObservableCollection<Card> newValue)
    {
        if (oldValue != null) oldValue.CollectionChanged -= OnArchiveChanged;
        newValue.CollectionChanged += OnArchiveChanged;
        OnPropertyChanged(nameof(ArchivedCount));
    }

    partial void OnArchivedListsChanged(ObservableCollection<BoardList>? oldValue, ObservableCollection<BoardList> newValue)
    {
        if (oldValue != null) oldValue.CollectionChanged -= OnArchiveChanged;
        newValue.CollectionChanged += OnArchiveChanged;
        OnPropertyChanged(nameof(ArchivedCount));
    }

    private void OnListsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(ActiveCardCount));
    private void OnArchiveChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(ArchivedCount));

    public static List<Label> DefaultLabels() =>
    [
        new() { Name = "", Color = "#4BCE97" },
        new() { Name = "", Color = "#F5CD47" },
        new() { Name = "", Color = "#FEA362" },
        new() { Name = "", Color = "#F87168" },
        new() { Name = "", Color = "#9F8FEF" },
        new() { Name = "", Color = "#579DFF" },
    ];

    public static readonly string[] GradientPresets =
    [
        "none",
        "gradient:#0C66E4,#9F6FEF",
        "gradient:#0B3D91,#37B4C3",
        "gradient:#1F845A,#94C748",
        "gradient:#E34935,#FCA700",
        "gradient:#6E5DC6,#E774BB",
        "gradient:#0C1D3B,#3D2D6B",
        "gradient:#172B4D,#44546F",
        "gradient:#09326C,#1F845A",
        "gradient:#AE2E24,#6E5DC6",
        "gradient:#1D2125,#454F59",
        "gradient:#227D9B,#6CC3E0",
        "gradient:#943D73,#FEA362",
    ];

    public static readonly string[] SolidPresets =
    [
        "color:#0079BF", "color:#D29034", "color:#519839", "color:#B04632",
        "color:#89609E", "color:#CD5A91", "color:#4BBF6B", "color:#00AECC",
        "color:#838C91", "color:#1D2125",
    ];
}
