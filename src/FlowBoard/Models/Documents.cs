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
    /// <summary>Frame aspect ratio ("16:9", "4:3", "2.39:1", "1:1", "9:16"...).</summary>
    [ObservableProperty] private string _aspect = "16:9";
    /// <summary>Hide the notes and show only the frames (quick overview).</summary>
    [ObservableProperty] private bool _framesOnly;
    [ObservableProperty] private ObservableCollection<Shot> _shots = [];

    public static readonly string[] AspectChoices = ["16:9", "4:3", "1.85:1", "2.39:1", "1:1", "4:5", "9:16"];

    public static double ParseAspect(string? aspect)
    {
        if (string.IsNullOrWhiteSpace(aspect)) return 16.0 / 9;
        var parts = aspect.Split(':');
        return parts.Length == 2
               && double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var a)
               && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var b)
               && a > 0 && b > 0
            ? a / b
            : 16.0 / 9;
    }

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
    /// <summary>Uncropped image (so the crop can be changed later).</summary>
    [ObservableProperty] private string? _originalImagePath;
    [ObservableProperty] private double _cropX;
    [ObservableProperty] private double _cropY;
    [ObservableProperty] private double _cropW;
    [ObservableProperty] private double _cropH;
    /// <summary>Show the whole image inside the frame (letterboxed) instead of filling it.</summary>
    [ObservableProperty] private bool _imageFit;
    /// <summary>Production status ("", "Idea", "Planned", "In progress", "Done", "Approved").</summary>
    [ObservableProperty] private string _status = string.Empty;
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
    public static readonly string[] Statuses = ["Idea", "Planned", "In progress", "Needs changes", "Done", "Approved"];
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
    /// <summary>Freehand drawing (pen tool). The stroke is in <see cref="CanvasNode.PathData"/>.</summary>
    Ink,
    /// <summary>Labelled area that groups the shapes inside it.</summary>
    Frame,
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
    /// <summary>Ink stroke geometry (path mini-language, any coordinates; it is stretched to the node box).</summary>
    [ObservableProperty] private string? _pathData;
    [ObservableProperty] private double _strokeWidth = 3;
    /// <summary>Size of the box the ink was drawn in (the drawing scales from it when resized).</summary>
    [ObservableProperty] private double _inkWidth;
    [ObservableProperty] private double _inkHeight;

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
    [ObservableProperty] private bool _startArrow;
    [ObservableProperty] private double _thickness = 2;
    /// <summary>Free end points, used when <see cref="FromId"/> / <see cref="ToId"/> is empty (lines and arrows drawn on their own).</summary>
    [ObservableProperty] private double _fromX;
    [ObservableProperty] private double _fromY;
    [ObservableProperty] private double _toX;
    [ObservableProperty] private double _toY;

    // Runtime: where the line actually starts and ends (for the drag handles).
    [ObservableProperty][property: JsonIgnore] private double _startX;
    [ObservableProperty][property: JsonIgnore] private double _startY;
    [ObservableProperty][property: JsonIgnore] private double _endX;
    [ObservableProperty][property: JsonIgnore] private double _endY;

    [JsonIgnore] public bool IsFreeFrom => FromId == Guid.Empty;
    [JsonIgnore] public bool IsFreeTo => ToId == Guid.Empty;
    partial void OnFromIdChanged(Guid value) => OnPropertyChanged(nameof(IsFreeFrom));
    partial void OnToIdChanged(Guid value) => OnPropertyChanged(nameof(IsFreeTo));

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
