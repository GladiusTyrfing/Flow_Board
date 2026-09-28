using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowBoard.Services;

namespace FlowBoard.Models;

/// <summary>What the main area shows.</summary>
public enum ActiveView
{
    Board,
    Storyboard,
    Canvas,
    Note,
    Dashboard,
}

// =====================================================================
// Storyboards
// =====================================================================

public enum StoryboardMode
{
    Film,
    Animation,
}

public partial class Storyboard : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _name = "Untitled storyboard";
    [ObservableProperty] private StoryboardMode _mode = StoryboardMode.Film;
    [ObservableProperty] private bool _isStarred;
    [ObservableProperty] private DateTime _createdAt = DateTime.Now;
    [ObservableProperty] private string _accentColor = "#8B5CF6";
    [ObservableProperty] private ObservableCollection<Shot> _shots = [];

    [JsonIgnore] public TimeSpan TotalDuration => TimeSpan.FromSeconds(Shots.Sum(s => s.DurationSeconds));
}

/// <summary>A small editable text row (equipment item, tag...).</summary>
public partial class TextItem : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _text = string.Empty;
}

public partial class Shot : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _scene = string.Empty;
    /// <summary>Frame image (photo, reference or saved sketch), relative to the data folder.</summary>
    [ObservableProperty] private string? _imagePath;
    /// <summary>Ink strokes of the sketch (so it can be edited again), relative path.</summary>
    [ObservableProperty] private string? _sketchPath;
    /// <summary>Photo the sketch was drawn over (kept so the sketch stays editable).</summary>
    [ObservableProperty] private string? _sketchBackgroundPath;
    [ObservableProperty] private DateTime? _date;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _sceneNotes = string.Empty;

    // Film fields
    [ObservableProperty] private string _shotType = "Wide";
    [ObservableProperty] private string _angle = "Eye level";
    [ObservableProperty] private string _movement = "Static";
    [ObservableProperty] private string _lens = string.Empty;
    [ObservableProperty] private string _location = string.Empty;
    [ObservableProperty] private ObservableCollection<TextItem> _tags = [];
    [ObservableProperty] private ObservableCollection<ChecklistItem> _shotList = [];
    [ObservableProperty] private ObservableCollection<TextItem> _equipment = [];

    // Animation fields
    [ObservableProperty] private string _action = string.Empty;
    [ObservableProperty] private string _dialogue = string.Empty;
    [ObservableProperty] private double _durationSeconds = 3;
    [ObservableProperty] private string _transition = "Cut";
    [ObservableProperty] private Attachment? _voiceNote;

    // runtime-only UI state
    [ObservableProperty][property: JsonIgnore] private string _newTag = string.Empty;
    [ObservableProperty][property: JsonIgnore] private string _newShotItem = string.Empty;
    [ObservableProperty][property: JsonIgnore] private string _newEquipment = string.Empty;
    [ObservableProperty][property: JsonIgnore] private int _number;

    [JsonIgnore] public string? ImageFullPath => ImagePath == null ? null : Path.Combine(AppPaths.DataDir, ImagePath);
    [JsonIgnore] public bool HasImage => ImagePath != null;
    [JsonIgnore] public bool HasVoice => VoiceNote != null;

    partial void OnVoiceNoteChanged(Attachment? value) => OnPropertyChanged(nameof(HasVoice));

    partial void OnImagePathChanged(string? value)
    {
        OnPropertyChanged(nameof(ImageFullPath));
        OnPropertyChanged(nameof(HasImage));
    }

    public static readonly string[] ShotTypes = ["Extreme wide", "Wide", "Full", "Medium", "Medium close-up", "Close-up", "Extreme close-up", "Over the shoulder", "POV", "Insert", "Aerial", "Two shot"];
    public static readonly string[] Angles = ["Eye level", "High angle", "Low angle", "Bird's eye", "Worm's eye", "Dutch tilt", "Overhead"];
    public static readonly string[] Movements = ["Static", "Pan", "Tilt", "Dolly in", "Dolly out", "Tracking", "Handheld", "Crane", "Zoom", "Whip pan", "Orbit"];
    public static readonly string[] Transitions = ["Cut", "Fade", "Dissolve", "Wipe", "Match cut", "Smash cut"];
}

// =====================================================================
// Canvas & flowcharts
// =====================================================================

public enum NodeShape
{
    Rectangle,
    Rounded,
    Ellipse,
    Diamond,
    Sticky,
    Text,
    Image,
    Card,
}

public enum EdgeStyle
{
    Curved,
    Straight,
    Elbow,
}

public partial class CanvasDoc : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _name = "Untitled canvas";
    [ObservableProperty] private bool _isStarred;
    [ObservableProperty] private DateTime _createdAt = DateTime.Now;
    [ObservableProperty] private double _zoom = 1;
    [ObservableProperty] private double _offsetX = 80;
    [ObservableProperty] private double _offsetY = 60;
    [ObservableProperty] private bool _snapToGrid = true;
    [ObservableProperty] private ObservableCollection<CanvasNode> _nodes = [];
    [ObservableProperty] private ObservableCollection<CanvasEdge> _edges = [];
}

public partial class CanvasNode : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private NodeShape _shape = NodeShape.Rounded;
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width = 160;
    [ObservableProperty] private double _height = 72;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private string _fill = "#8B5CF6";
    [ObservableProperty] private double _fontSize = 14;
    [ObservableProperty] private string? _imagePath;
    [ObservableProperty] private Guid? _cardId;
    [ObservableProperty] private int _z;

    [ObservableProperty][property: JsonIgnore] private bool _isSelected;
    [ObservableProperty][property: JsonIgnore] private bool _isEditing;
    [ObservableProperty][property: JsonIgnore] private Card? _card;

    [JsonIgnore] public string? ImageFullPath => ImagePath == null ? null : Path.Combine(AppPaths.DataDir, ImagePath);
    [JsonIgnore] public double CenterX => X + Width / 2;
    [JsonIgnore] public double CenterY => Y + Height / 2;
    [JsonIgnore] public Services.Box Bounds => new(X, Y, Width, Height);
    [JsonIgnore] public bool IsCardMissing => Shape == NodeShape.Card && Card == null;
    partial void OnCardChanged(Card? value) => OnPropertyChanged(nameof(IsCardMissing));

    partial void OnImagePathChanged(string? value) => OnPropertyChanged(nameof(ImageFullPath));
}

public partial class CanvasEdge : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private Guid _fromId;
    [ObservableProperty] private Guid _toId;
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private EdgeStyle _style = EdgeStyle.Curved;
    [ObservableProperty] private string _color = "#98A2B3";
    [ObservableProperty] private bool _dashed;
    [ObservableProperty] private bool _arrow = true;

    [ObservableProperty][property: JsonIgnore] private bool _isSelected;

    // Runtime geometry (WPF path mini-language), recomputed whenever either end moves.
    [ObservableProperty][property: JsonIgnore] private string _pathData = string.Empty;
    [ObservableProperty][property: JsonIgnore] private string _arrowData = string.Empty;
    [ObservableProperty][property: JsonIgnore] private double _labelX;
    [ObservableProperty][property: JsonIgnore] private double _labelY;

    /// <summary>Label box is 240 wide and 40 high, centered on the label point.</summary>
    [JsonIgnore] public double LabelLeft => LabelX - 120;
    [JsonIgnore] public double LabelTop => LabelY - 20;
    [JsonIgnore] public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    partial void OnLabelXChanged(double value) => OnPropertyChanged(nameof(LabelLeft));
    partial void OnLabelYChanged(double value) => OnPropertyChanged(nameof(LabelTop));
    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(HasLabel));
}

// =====================================================================
// Notes / docs (block editor)
// =====================================================================

public enum BlockType
{
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    Bullet,
    Numbered,
    Todo,
    Quote,
    Code,
    Callout,
    Divider,
    Image,
    Link,
}

public enum LinkTarget
{
    Card,
    Board,
    Storyboard,
    Canvas,
    Note,
}

public partial class NotePage : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _title = "Untitled page";
    [ObservableProperty] private string _icon = "DocumentText24";
    [ObservableProperty] private bool _isStarred;
    [ObservableProperty] private DateTime _createdAt = DateTime.Now;
    [ObservableProperty] private DateTime _updatedAt = DateTime.Now;
    [ObservableProperty] private ObservableCollection<NoteBlock> _blocks = [];
}

public partial class NoteBlock : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private BlockType _type = BlockType.Paragraph;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private int _indent;
    [ObservableProperty] private string? _imagePath;
    [ObservableProperty] private LinkTarget _linkKind;
    [ObservableProperty] private Guid? _linkId;

    [ObservableProperty][property: JsonIgnore] private int _number = 1;
    [ObservableProperty][property: JsonIgnore] private bool _focusRequested;

    [JsonIgnore] public string? ImageFullPath => ImagePath == null ? null : Path.Combine(AppPaths.DataDir, ImagePath);

    partial void OnImagePathChanged(string? value) => OnPropertyChanged(nameof(ImageFullPath));
}
