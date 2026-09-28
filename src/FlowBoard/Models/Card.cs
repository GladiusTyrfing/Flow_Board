using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FlowBoard.Models;

/// <summary>A task card. Serialized as-is; runtime-only state is marked <see cref="JsonIgnoreAttribute"/>.</summary>
public partial class Card : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private DateTime? _completedAt;
    [ObservableProperty] private DateTime _createdAt = DateTime.Now;
    [ObservableProperty] private DateTime? _startDate;
    [ObservableProperty] private DateTime? _dueDate;
    /// <summary>Minutes before the due date to remind. Negative means no reminder.</summary>
    [ObservableProperty] private int _reminderMinutes = -1;
    [ObservableProperty] private bool _reminderSent;
    [ObservableProperty] private Priority _priority;
    [ObservableProperty] private string? _coverColor;
    [ObservableProperty] private Guid? _coverAttachmentId;
    [ObservableProperty] private Guid? _archivedFromListId;
    [ObservableProperty] private DateTime? _archivedAt;
    /// <summary>Shows details (description, checklists, files, voice notes…) right on the board.</summary>
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private ObservableCollection<Guid> _labelIds = [];
    [ObservableProperty] private ObservableCollection<Checklist> _checklists = [];
    [ObservableProperty] private ObservableCollection<Attachment> _attachments = [];
    [ObservableProperty] private ObservableCollection<Comment> _comments = [];
    [ObservableProperty] private ObservableCollection<TimeEntry> _timeEntries = [];

    // ---- runtime-only state ----
    [ObservableProperty][property: JsonIgnore] private bool _isFilteredOut;
    [ObservableProperty][property: JsonIgnore] private bool _isTimerRunning;
    [ObservableProperty][property: JsonIgnore] private bool _isHighlighted;

    private Board? _board;

    /// <summary>The board that owns this card. Set by <see cref="Board.Hydrate"/> and whenever a card is created or moved.</summary>
    [JsonIgnore]
    public Board? Board
    {
        get => _board;
        set
        {
            if (SetProperty(ref _board, value)) NotifyLabelsChanged();
        }
    }

    public Card()
    {
        HookAll();
    }

    // ---------- Derived display properties ----------

    [JsonIgnore]
    public IReadOnlyList<Label> ResolvedLabels =>
        Board == null ? [] : Board.Labels.Where(l => LabelIds.Contains(l.Id)).ToList();

    [JsonIgnore] public bool HasLabels => ResolvedLabels.Count > 0;
    [JsonIgnore] public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    [JsonIgnore] public int ChecklistDone => Checklists.Sum(c => c.DoneCount);
    [JsonIgnore] public int ChecklistTotal => Checklists.Sum(c => c.TotalCount);
    [JsonIgnore] public bool HasChecklist => ChecklistTotal > 0;
    [JsonIgnore] public bool IsChecklistComplete => ChecklistTotal > 0 && ChecklistDone == ChecklistTotal;
    [JsonIgnore] public string ChecklistText => $"{ChecklistDone}/{ChecklistTotal}";
    [JsonIgnore] public int FileCount => Attachments.Count(a => a.Kind != AttachmentKind.Voice);
    [JsonIgnore] public int VoiceCount => Attachments.Count(a => a.Kind == AttachmentKind.Voice);
    [JsonIgnore] public int CommentCount => Comments.Count(c => !c.IsActivity);
    [JsonIgnore] public IEnumerable<Attachment> FileAttachments => Attachments.Where(a => a.Kind != AttachmentKind.Voice);
    [JsonIgnore] public IEnumerable<Attachment> VoiceNotes => Attachments.Where(a => a.Kind == AttachmentKind.Voice);
    [JsonIgnore] public IEnumerable<Attachment> ImageAttachments => Attachments.Where(a => a.Kind == AttachmentKind.Image);
    [JsonIgnore] public IEnumerable<Attachment> OtherFiles => Attachments.Where(a => a.Kind is AttachmentKind.File or AttachmentKind.Link);
    [JsonIgnore] public Comment? LatestComment => Comments.Where(c => !c.IsActivity).OrderByDescending(c => c.CreatedAt).FirstOrDefault();
    [JsonIgnore] public IEnumerable<Comment> CommentsNewestFirst => Comments.OrderByDescending(c => c.CreatedAt);

    [JsonIgnore]
    public Attachment? CoverImage => CoverAttachmentId is { } id
        ? Attachments.FirstOrDefault(a => a.Id == id && a.Kind == AttachmentKind.Image)
        : null;

    [JsonIgnore] public string? CoverImagePath => CoverImage?.FullPath;
    [JsonIgnore] public bool HasCoverColor => CoverImage == null && !string.IsNullOrEmpty(CoverColor);

    [JsonIgnore]
    public TimeSpan TrackedTime => TimeSpan.FromTicks(TimeEntries.Sum(t => t.Duration.Ticks));

    [JsonIgnore] public bool HasTrackedTime => TrackedTime.TotalMinutes >= 1;

    [JsonIgnore] public string TrackedTimeText => FormatDuration(TrackedTime);

    [JsonIgnore] public bool HasDue => DueDate.HasValue;
    [JsonIgnore] public bool HasReminder => DueDate.HasValue && ReminderMinutes >= 0;
    [JsonIgnore] public bool HasDates => DueDate.HasValue || StartDate.HasValue;

    [JsonIgnore]
    public DueState DueState
    {
        get
        {
            if (DueDate is not { } due) return DueState.None;
            if (IsCompleted) return DueState.Done;
            var effective = EffectiveDue(due);
            var now = DateTime.Now;
            if (effective < now) return DueState.Overdue;
            if (effective - now < TimeSpan.FromHours(24)) return DueState.Soon;
            return DueState.Normal;
        }
    }

    [JsonIgnore]
    public string DueText
    {
        get
        {
            if (DueDate is not { } due) return string.Empty;
            var text = FormatDate(due);
            if (StartDate is { } start) text = $"{FormatDate(start, false)} – {text}";
            return text;
        }
    }

    /// <summary>A due date with no time component means "end of that day".</summary>
    public static DateTime EffectiveDue(DateTime due) => due.TimeOfDay == TimeSpan.Zero ? due.Date.AddDays(1).AddTicks(-1) : due;

    public static string FormatDate(DateTime d, bool includeTime = true)
    {
        var today = DateTime.Today;
        string day = d.Date == today ? "Today"
            : d.Date == today.AddDays(1) ? "Tomorrow"
            : d.Date == today.AddDays(-1) ? "Yesterday"
            : d.Year == today.Year ? d.ToString("MMM d") : d.ToString("MMM d, yyyy");
        if (includeTime && d.TimeOfDay != TimeSpan.Zero) day += d.ToString(" HH:mm");
        return day;
    }

    public static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m";
        return $"{t.Seconds}s";
    }

    public void NotifyLabelsChanged()
    {
        OnPropertyChanged(nameof(ResolvedLabels));
        OnPropertyChanged(nameof(HasLabels));
    }

    /// <summary>Re-evaluates time-dependent properties (called periodically).</summary>
    public void RefreshTimeState()
    {
        OnPropertyChanged(nameof(DueState));
        OnPropertyChanged(nameof(DueText));
    }

    public void AddActivity(string text, string author)
    {
        Comments.Add(new Comment { Author = author, Text = text, IsActivity = true });
    }

    // ---------- change hooks ----------

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(HasDescription));
    partial void OnDescriptionChanged(string value) => OnPropertyChanged(nameof(HasDescription));

    partial void OnIsCompletedChanged(bool value)
    {
        CompletedAt = value ? DateTime.Now : null;
        RefreshTimeState();
    }

    partial void OnDueDateChanged(DateTime? value)
    {
        ReminderSent = false;
        OnPropertyChanged(nameof(HasDue));
        OnPropertyChanged(nameof(HasReminder));
        OnPropertyChanged(nameof(HasDates));
        RefreshTimeState();
    }

    partial void OnStartDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(HasDates));
        RefreshTimeState();
    }

    partial void OnReminderMinutesChanged(int value)
    {
        ReminderSent = false;
        OnPropertyChanged(nameof(HasReminder));
    }

    partial void OnCoverColorChanged(string? value) => RaiseCover();
    partial void OnCoverAttachmentIdChanged(Guid? value) => RaiseCover();

    private void RaiseCover()
    {
        OnPropertyChanged(nameof(CoverImage));
        OnPropertyChanged(nameof(CoverImagePath));
        OnPropertyChanged(nameof(HasCoverColor));
    }

    partial void OnLabelIdsChanged(ObservableCollection<Guid>? oldValue, ObservableCollection<Guid> newValue)
    {
        if (oldValue != null) oldValue.CollectionChanged -= OnLabelIdsCollectionChanged;
        newValue.CollectionChanged += OnLabelIdsCollectionChanged;
        NotifyLabelsChanged();
    }

    partial void OnChecklistsChanged(ObservableCollection<Checklist>? oldValue, ObservableCollection<Checklist> newValue)
    {
        if (oldValue != null) oldValue.CollectionChanged -= OnChecklistsCollectionChanged;
        HookChecklists(newValue);
        RaiseChecklist();
    }

    partial void OnAttachmentsChanged(ObservableCollection<Attachment>? oldValue, ObservableCollection<Attachment> newValue)
    {
        if (oldValue != null) oldValue.CollectionChanged -= OnAttachmentsCollectionChanged;
        newValue.CollectionChanged += OnAttachmentsCollectionChanged;
        RaiseAttachments();
    }

    partial void OnCommentsChanged(ObservableCollection<Comment>? oldValue, ObservableCollection<Comment> newValue)
    {
        if (oldValue != null) oldValue.CollectionChanged -= OnCommentsCollectionChanged;
        newValue.CollectionChanged += OnCommentsCollectionChanged;
        RaiseComments();
    }

    partial void OnTimeEntriesChanged(ObservableCollection<TimeEntry>? oldValue, ObservableCollection<TimeEntry> newValue)
    {
        if (oldValue != null) oldValue.CollectionChanged -= OnTimeEntriesCollectionChanged;
        newValue.CollectionChanged += OnTimeEntriesCollectionChanged;
        RaiseTime();
    }

    private void HookAll()
    {
        LabelIds.CollectionChanged += OnLabelIdsCollectionChanged;
        HookChecklists(Checklists);
        Attachments.CollectionChanged += OnAttachmentsCollectionChanged;
        Comments.CollectionChanged += OnCommentsCollectionChanged;
        TimeEntries.CollectionChanged += OnTimeEntriesCollectionChanged;
    }

    private void HookChecklists(ObservableCollection<Checklist> lists)
    {
        lists.CollectionChanged += OnChecklistsCollectionChanged;
        foreach (var c in lists) c.ProgressChanged += OnChecklistProgress;
    }

    private void OnLabelIdsCollectionChanged(object? s, NotifyCollectionChangedEventArgs e) => NotifyLabelsChanged();

    private void OnChecklistsCollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (Checklist c in e.OldItems) c.ProgressChanged -= OnChecklistProgress;
        if (e.NewItems != null) foreach (Checklist c in e.NewItems) c.ProgressChanged += OnChecklistProgress;
        RaiseChecklist();
    }

    private void OnChecklistProgress(object? sender, EventArgs e) => RaiseChecklist();

    private void RaiseChecklist()
    {
        OnPropertyChanged(nameof(ChecklistDone));
        OnPropertyChanged(nameof(ChecklistTotal));
        OnPropertyChanged(nameof(HasChecklist));
        OnPropertyChanged(nameof(ChecklistText));
        OnPropertyChanged(nameof(IsChecklistComplete));
    }

    private void OnAttachmentsCollectionChanged(object? s, NotifyCollectionChangedEventArgs e) => RaiseAttachments();

    private void RaiseAttachments()
    {
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(VoiceCount));
        OnPropertyChanged(nameof(FileAttachments));
        OnPropertyChanged(nameof(VoiceNotes));
        OnPropertyChanged(nameof(ImageAttachments));
        OnPropertyChanged(nameof(OtherFiles));
        RaiseCover();
    }

    private void OnCommentsCollectionChanged(object? s, NotifyCollectionChangedEventArgs e) => RaiseComments();

    private void RaiseComments()
    {
        OnPropertyChanged(nameof(CommentCount));
        OnPropertyChanged(nameof(CommentsNewestFirst));
        OnPropertyChanged(nameof(LatestComment));
    }

    private void OnTimeEntriesCollectionChanged(object? s, NotifyCollectionChangedEventArgs e) => RaiseTime();

    public void RaiseTime()
    {
        OnPropertyChanged(nameof(TrackedTime));
        OnPropertyChanged(nameof(TrackedTimeText));
        OnPropertyChanged(nameof(HasTrackedTime));
    }
}
