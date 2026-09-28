using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;
using Microsoft.Win32;

namespace FlowBoard.ViewModels;

public partial class LabelOption : ObservableObject
{
    private readonly Card _card;

    public LabelOption(Label label, Card card)
    {
        Label = label;
        _card = card;
    }

    public Label Label { get; }

    public bool IsSelected
    {
        get => _card.LabelIds.Contains(Label.Id);
        set
        {
            if (value == IsSelected) return;
            if (value) _card.LabelIds.Add(Label.Id);
            else _card.LabelIds.Remove(Label.Id);
            OnPropertyChanged();
        }
    }
}

public sealed record ReminderChoice(int Minutes, string Text);

/// <summary>A related card shown in the card editor. Relation: "Linked", "Blocked by" or "Blocks".</summary>
public sealed record CardRelation(Guid Id, Card? Card, string Relation, string Title, string Where, bool IsDone)
{
    /// <summary>What the row points at (a card, or an item in another section).</summary>
    public LinkTarget Kind { get; init; } = LinkTarget.Card;
    public string Icon { get; init; } = "TaskListLtr24";
}

/// <summary>The big card editor (Trello's "card back").</summary>
public sealed partial class CardDetailViewModel : DialogViewModel
{
    private readonly MainViewModel _main;
    private readonly string _snapshot;
    private readonly int _boardIndex;

    public CardDetailViewModel(MainViewModel main, Card card, Board board)
    {
        _main = main;
        Card = card;
        Board = board;
        _snapshot = Json.SerializeBoard(board);
        _boardIndex = main.Workspace.Boards.IndexOf(board);
        LabelOptions = [];
        RebuildLabelOptions();
        LoadDateEditor();
        Card.Comments.CollectionChanged += OnCommentsChanged;
        Board.Labels.CollectionChanged += OnBoardLabelsChanged;
        RefreshRelations();
    }

    // ---------- linked cards & dependencies ----------

    public ObservableCollection<CardRelation> Relations { get; } = [];
    public bool HasRelations => Relations.Count > 0;

    public string BlockedText
    {
        get
        {
            var open = Relations.Count(r => r.Relation == "Blocked by" && !r.IsDone);
            return open == 0 ? string.Empty : open == 1 ? "Waiting on 1 unfinished card" : $"Waiting on {open} unfinished cards";
        }
    }

    private void RefreshRelations()
    {
        Relations.Clear();
        CardRelation Make(Guid id, string rel)
        {
            var c = _main.Workspace.FindCard(id, out var b, out var l);
            var where = c == null ? "Deleted card" : l == null ? $"{b?.Name} › archived" : $"{b?.Name} › {l.Name}";
            return new CardRelation(id, c, rel, c?.Title ?? "(missing card)", where, c?.IsCompleted ?? true);
        }

        foreach (var id in Card.BlockedByIds) Relations.Add(Make(id, "Blocked by"));
        foreach (var (_, _, other) in _main.Workspace.EnumerateActiveCards())
            if (other.BlockedByIds.Contains(Card.Id)) Relations.Add(Make(other.Id, "Blocks"));
        foreach (var id in Card.LinkedCardIds) Relations.Add(Make(id, "Linked"));
        foreach (var link in Card.Links)
        {
            var preview = LinkResolver.Describe(_main.Workspace, link.Kind, link.Id);
            var shot = link.Kind == LinkTarget.Shot ? LinkResolver.FindShot(_main.Workspace, link.Id, out _) : null;
            Relations.Add(new CardRelation(link.Id, null, KindName(link.Kind),
                preview.IsMissing ? "(deleted)" : preview.Title,
                shot != null ? $"{preview.Subtitle}{(string.IsNullOrEmpty(shot.Status) ? "" : " · " + shot.Status)}" : preview.Subtitle,
                shot != null && LinkSync.IsDoneStatus(shot.Status))
            {
                Kind = link.Kind,
                Icon = preview.Icon,
            });
        }

        OnPropertyChanged(nameof(HasRelations));
        OnPropertyChanged(nameof(BlockedText));
        Card.NotifyRelationsChanged();
    }

    private IEnumerable<Guid> Related() => Relations.Select(r => r.Id).Append(Card.Id);

    private static string KindName(LinkTarget kind) => kind switch
    {
        LinkTarget.Note => "Page",
        _ => kind.ToString(),
    };

    /// <summary>Links this card to a board, storyboard, single shot, canvas or page.</summary>
    [RelayCommand]
    private async Task LinkItem()
    {
        var pick = await _main.PickLinkAsync("Link this card to…", MainViewModel.CardLinkKinds, Related());
        if (pick == null) return;
        _main.AddCardLink(Card, pick.Value.Kind, pick.Value.Id);
        Card.AddActivity($"linked {KindName(pick.Value.Kind).ToLowerInvariant()} \"{pick.Value.Title}\"", Me);
        RefreshRelations();
        OnPropertyChanged(nameof(CurrentList));
        if (pick.Value.Kind == LinkTarget.Shot)
            _main.ShowToast("Linked — marking the shot Done completes this card, and completing the card marks the shot Done.");
    }

    [RelayCommand]
    private async Task LinkCard()
    {
        var pick = await _main.PickLinkAsync("Link a related card", [LinkTarget.Card], Related());
        if (pick == null) return;
        var other = _main.Workspace.FindCard(pick.Value.Id, out _, out _);
        if (!Card.LinkedCardIds.Contains(pick.Value.Id)) Card.LinkedCardIds.Add(pick.Value.Id);
        if (other != null && !other.LinkedCardIds.Contains(Card.Id))
        {
            other.LinkedCardIds.Add(Card.Id);
            other.NotifyRelationsChanged();
        }

        Card.AddActivity($"linked \"{pick.Value.Title}\"", Me);
        RefreshRelations();
    }

    [RelayCommand]
    private async Task AddBlocker()
    {
        var pick = await _main.PickLinkAsync("This card is blocked by…", [LinkTarget.Card], Related());
        if (pick == null) return;
        if (DependsOn(pick.Value.Id, Card.Id))
        {
            _main.ShowToast("That would create a loop: that card already waits on this one.", isError: true);
            return;
        }

        Card.BlockedByIds.Add(pick.Value.Id);
        Card.AddActivity($"is now blocked by \"{pick.Value.Title}\"", Me);
        RefreshRelations();
        _main.RefreshBlocked();
    }

    [RelayCommand]
    private async Task AddBlocks()
    {
        var pick = await _main.PickLinkAsync("This card blocks…", [LinkTarget.Card], Related());
        if (pick == null || _main.Workspace.FindCard(pick.Value.Id, out _, out _) is not { } other) return;
        if (DependsOn(Card.Id, other.Id))
        {
            _main.ShowToast("That would create a loop: this card already waits on that one.", isError: true);
            return;
        }

        other.BlockedByIds.Add(Card.Id);
        other.NotifyRelationsChanged();
        Card.AddActivity($"now blocks \"{pick.Value.Title}\"", Me);
        RefreshRelations();
        _main.RefreshBlocked();
    }

    /// <summary>True when <paramref name="card"/> (transitively) waits on <paramref name="blocker"/>.</summary>
    private bool DependsOn(Guid card, Guid blocker)
    {
        var seen = new HashSet<Guid>();
        var stack = new Stack<Guid>([card]);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (id == blocker) return true;
            if (!seen.Add(id) || _main.Workspace.FindCard(id, out _, out _) is not { } c) continue;
            foreach (var b in c.BlockedByIds) stack.Push(b);
        }

        return false;
    }

    [RelayCommand]
    private void RemoveRelation(CardRelation r)
    {
        var other = r.Card;
        switch (r.Relation)
        {
            case "Blocked by":
                Card.BlockedByIds.Remove(r.Id);
                break;
            case "Blocks":
                other?.BlockedByIds.Remove(Card.Id);
                break;
            case "Linked":
                Card.LinkedCardIds.Remove(r.Id);
                other?.LinkedCardIds.Remove(Card.Id);
                break;
            default:
                foreach (var l in Card.Links.Where(l => l.Kind == r.Kind && l.Id == r.Id).ToList()) Card.Links.Remove(l);
                break;
        }

        other?.NotifyRelationsChanged();
        RefreshRelations();
        _main.RefreshBlocked();
    }

    [RelayCommand]
    private void OpenRelation(CardRelation r)
    {
        if (r.Kind == LinkTarget.Card && r.Card == null) return;
        _main.CloseDialog(this);
        _main.OpenTarget(r.Kind, r.Id);
    }

    public Card Card { get; }
    public Board Board { get; }
    public MainViewModel Main => _main;
    public AudioRecorder Recorder => _main.Recorder;
    public TimeTracker Timer => _main.Timer;
    public ObservableCollection<BoardList> Lists => Board.Lists;
    public ObservableCollection<LabelOption> LabelOptions { get; }
    public IReadOnlyList<string> LabelPalette => Label.Palette;
    public IReadOnlyList<string> CoverPalette { get; } = Label.Palette.Take(12).ToList();
    public IReadOnlyList<Priority> Priorities { get; } = Enum.GetValues<Priority>();
    public bool IsArchived => Board.ArchivedCards.Contains(Card);
    public IEnumerable<Board> OtherBoards => _main.Workspace.Boards.Where(b => b != Board);

    public IReadOnlyList<ReminderChoice> ReminderChoices { get; } =
    [
        new(-1, "None"), new(0, "At time of due date"), new(5, "5 minutes before"), new(15, "15 minutes before"),
        new(60, "1 hour before"), new(120, "2 hours before"), new(1440, "1 day before"), new(2880, "2 days before"),
    ];

    public BoardList? CurrentList
    {
        get => Board.FindListOf(Card);
        set
        {
            var from = CurrentList;
            if (value == null || from == value || from == null) return;
            from.Cards.Remove(Card);
            value.Cards.Insert(0, Card);
            _main.AfterCardMoved(Card, from, value);
            OnPropertyChanged();
        }
    }

    public IEnumerable<Comment> VisibleComments =>
        Card.CommentsNewestFirst.Where(c => ShowActivity || !c.IsActivity);

    [ObservableProperty] private string _newComment = string.Empty;
    [ObservableProperty] private bool _showActivity = true;
    [ObservableProperty] private string _newLabelName = string.Empty;
    [ObservableProperty] private string _newLabelColor = Label.Palette[5];
    [ObservableProperty] private Label? _editingLabel;
    [ObservableProperty] private bool _isEditingDescription;
    [ObservableProperty] private bool _isDragOver;

    /// <summary>A popup to open as soon as the view appears ("dates", "labels"), set by hover hotkeys.</summary>
    public string? PendingPopup { get; set; }

    // ---- date editor state ----
    [ObservableProperty] private bool _hasStartDate;
    [ObservableProperty] private DateTime? _startDateValue;
    [ObservableProperty] private bool _hasDueDate = true;
    [ObservableProperty] private DateTime? _dueDateValue;
    [ObservableProperty] private string _dueTimeText = string.Empty;
    [ObservableProperty] private ReminderChoice? _selectedReminder;

    partial void OnShowActivityChanged(bool value) => OnPropertyChanged(nameof(VisibleComments));

    private void OnCommentsChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(VisibleComments));

    private void OnBoardLabelsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildLabelOptions();

    private string Me => _main.Settings.DisplayName;

    public override void OnClosed()
    {
        Card.Comments.CollectionChanged -= OnCommentsChanged;
        Board.Labels.CollectionChanged -= OnBoardLabelsChanged;
        if (Recorder.IsRecording) _ = Recorder.CancelAsync();
        _main.Player.Stop();

        // Clean up empty checklist items and record one undo step for everything edited here.
        foreach (var cl in Card.Checklists)
            foreach (var i in cl.Items.Where(i => string.IsNullOrWhiteSpace(i.Text)).ToList())
                cl.Items.Remove(i);
        if (string.IsNullOrWhiteSpace(Card.Title)) Card.Title = "Untitled card";

        if (_main.Workspace.Boards.Contains(Board) && Json.SerializeBoard(Board) != _snapshot)
            _main.Undo.PushSnapshot(Board.Id, _boardIndex, _snapshot, $"Edit \"{Card.Title}\"");
        _main.OnCardDialogClosed();
    }

    // ---------- title / completion / description ----------

    [RelayCommand]
    private void ToggleComplete()
    {
        _main.Undo.Checkpoint(_main.Workspace, Board, Card.IsCompleted ? "Mark incomplete" : "Mark complete");
        Card.IsCompleted = !Card.IsCompleted;
        Card.AddActivity(Card.IsCompleted ? "marked this card as complete" : "marked this card as incomplete", Me);
        _main.MoveForCompletion(Card);
        OnPropertyChanged(nameof(CurrentList));
    }

    [RelayCommand] private void EditDescription() => IsEditingDescription = true;
    [RelayCommand] private void DoneEditingDescription() => IsEditingDescription = false;

    // ---------- labels ----------

    private void RebuildLabelOptions()
    {
        LabelOptions.Clear();
        foreach (var l in Board.Labels) LabelOptions.Add(new LabelOption(l, Card));
    }

    [RelayCommand]
    private void CreateLabel()
    {
        var label = new Label { Name = NewLabelName.Trim(), Color = NewLabelColor };
        Board.Labels.Add(label);
        Card.LabelIds.Add(label.Id);
        NewLabelName = string.Empty;
        RebuildLabelOptions();
        _main.Filter.SetBoard(Board);
    }

    [RelayCommand] private void PickNewLabelColor(string color) => NewLabelColor = color;

    [RelayCommand] private void EditLabel(Label label) => EditingLabel = EditingLabel == label ? null : label;

    [RelayCommand]
    private void SetEditingLabelColor(string color)
    {
        if (EditingLabel == null) return;
        EditingLabel.Color = color;
        Board.NotifyLabelsChanged();
    }

    [RelayCommand]
    private void FinishEditingLabel()
    {
        EditingLabel = null;
        Board.NotifyLabelsChanged();
    }

    [RelayCommand]
    private void DeleteLabel(Label label)
    {
        Board.Labels.Remove(label);
        foreach (var c in Board.AllActiveCards.Concat(Board.ArchivedCards)) c.LabelIds.Remove(label.Id);
        EditingLabel = null;
        RebuildLabelOptions();
        _main.Filter.SetBoard(Board);
    }

    // ---------- dates ----------

    private void LoadDateEditor()
    {
        HasDueDate = Card.DueDate.HasValue || !Card.StartDate.HasValue;
        DueDateValue = Card.DueDate?.Date ?? DateTime.Today.AddDays(1);
        DueTimeText = Card.DueDate is { } d && d.TimeOfDay != TimeSpan.Zero ? d.ToString("HH:mm") : string.Empty;
        HasStartDate = Card.StartDate.HasValue;
        StartDateValue = Card.StartDate?.Date ?? DateTime.Today;
        var minutes = Card.DueDate.HasValue ? Card.ReminderMinutes : _main.Settings.DefaultReminderMinutes;
        SelectedReminder = ReminderChoices.FirstOrDefault(r => r.Minutes == minutes) ?? ReminderChoices[0];
    }

    [RelayCommand]
    private void QuickDue(string which)
    {
        HasDueDate = true;
        DueDateValue = which switch
        {
            "today" => DateTime.Today,
            "tomorrow" => DateTime.Today.AddDays(1),
            "weekend" => NextDay(DayOfWeek.Saturday),
            "nextweek" => NextDay(DayOfWeek.Monday),
            _ => DueDateValue,
        };
    }

    private static DateTime NextDay(DayOfWeek day)
    {
        var d = DateTime.Today.AddDays(1);
        while (d.DayOfWeek != day) d = d.AddDays(1);
        return d;
    }

    [RelayCommand]
    private void ApplyDates()
    {
        DateTime? due = null;
        if (HasDueDate && DueDateValue is { } dd)
        {
            due = dd.Date;
            if (TimeSpan.TryParse(DueTimeText.Trim(), out var t) && t > TimeSpan.Zero && t < TimeSpan.FromDays(1))
                due = dd.Date + t;
        }

        var start = HasStartDate ? StartDateValue?.Date : null;
        if (start != null && due != null && start > due) start = due.Value.Date;

        var changed = due != Card.DueDate;
        Card.StartDate = start;
        Card.DueDate = due;
        Card.ReminderMinutes = SelectedReminder?.Minutes ?? -1;
        if (changed)
            Card.AddActivity(due == null ? "removed the due date" : $"set the due date to {Card.FormatDate(due.Value)}", Me);
    }

    [RelayCommand]
    private void RemoveDates()
    {
        Card.StartDate = null;
        Card.DueDate = null;
        Card.ReminderMinutes = -1;
        LoadDateEditor();
        Card.AddActivity("removed the dates", Me);
    }

    // ---------- priority / cover ----------

    [RelayCommand]
    private void SetPriority(Priority p)
    {
        if (Card.Priority == p) return;
        Card.Priority = p;
        Card.AddActivity(p == Priority.None ? "cleared the priority" : $"set priority to {p}", Me);
    }

    [RelayCommand]
    private void SetCoverColor(string? color)
    {
        Card.CoverAttachmentId = null;
        Card.CoverColor = color;
    }

    [RelayCommand]
    private void SetCoverImage(Attachment a)
    {
        Card.CoverColor = null;
        Card.CoverAttachmentId = Card.CoverAttachmentId == a.Id ? null : a.Id;
    }

    [RelayCommand]
    private void RemoveCover()
    {
        Card.CoverColor = null;
        Card.CoverAttachmentId = null;
    }

    // ---------- checklists ----------

    [RelayCommand]
    private void AddChecklist()
    {
        var cl = new Checklist { Title = Card.Checklists.Count == 0 ? "Checklist" : $"Checklist {Card.Checklists.Count + 1}" };
        Card.Checklists.Add(cl);
    }

    [RelayCommand]
    private void DeleteChecklist(Checklist cl) => Card.Checklists.Remove(cl);

    [RelayCommand]
    private void AddChecklistItem(Checklist cl)
    {
        var text = cl.NewItemText.Trim();
        if (text.Length == 0) return;
        // Pasting several lines creates one item per line.
        foreach (var line in text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            cl.Items.Add(new ChecklistItem { Text = line });
        cl.NewItemText = string.Empty;
    }

    [RelayCommand]
    private void DeleteChecklistItem(ChecklistItem item)
    {
        foreach (var cl in Card.Checklists)
            if (cl.Items.Remove(item)) return;
    }

    [RelayCommand]
    private void ConvertItemToCard(ChecklistItem item)
    {
        var list = CurrentList;
        if (list == null) return;
        var card = new Card { Title = item.Text, Board = Board };
        card.AddActivity($"converted from a checklist item on \"{Card.Title}\"", Me);
        list.Cards.Insert(list.Cards.IndexOf(Card) + 1, card);
        DeleteChecklistItem(item);
        _main.ShowToast($"Created card \"{card.Title}\"");
    }

    // ---------- attachments ----------

    [RelayCommand]
    private void AddFiles()
    {
        var dlg = new OpenFileDialog { Multiselect = true, Title = "Attach files" };
        if (dlg.ShowDialog() == true) AddFilesFromPaths(dlg.FileNames);
    }

    public void AddFilesFromPaths(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var src in paths)
        {
            try
            {
                if (Directory.Exists(src)) continue;
                var dest = AppPaths.UniquePath(AppPaths.CardFolder(Card.Id), Path.GetFileName(src));
                File.Copy(src, dest);
                var ext = Path.GetExtension(src).ToLowerInvariant();
                var kind = Attachment.ImageExtensions.Contains(ext) ? AttachmentKind.Image
                    : MediaStore.IsAudioFile(dest) ? AttachmentKind.Voice
                    : AttachmentKind.File;
                var att = new Attachment
                {
                    Kind = kind, Name = Path.GetFileName(dest), RelativePath = AppPaths.ToRelative(dest), Size = new FileInfo(dest).Length,
                    DurationSeconds = kind == AttachmentKind.Voice ? MediaStore.AudioSeconds(dest) : 0,
                };
                Card.Attachments.Add(att);
                if (kind == AttachmentKind.Image && Card.CoverAttachmentId == null && string.IsNullOrEmpty(Card.CoverColor))
                    Card.CoverAttachmentId = att.Id;
                added++;
            }
            catch (Exception ex)
            {
                _main.ShowToast($"Couldn't attach {Path.GetFileName(src)}: {ex.Message}", isError: true);
            }
        }

        if (added > 0) Card.AddActivity(added == 1 ? "attached a file" : $"attached {added} files", Me);
    }

    /// <summary>Ctrl+V inside the card: images from the clipboard, copied files, or a URL.</summary>
    [RelayCommand]
    public void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                AddFilesFromPaths(Clipboard.GetFileDropList().Cast<string>());
                return;
            }

            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
            {
                var dest = AppPaths.UniquePath(AppPaths.CardFolder(Card.Id), $"Pasted image {DateTime.Now:yyyy-MM-dd HHmmss}.png");
                using (var fs = File.Create(dest))
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(image));
                    enc.Save(fs);
                }

                var att = new Attachment
                {
                    Kind = AttachmentKind.Image, Name = Path.GetFileName(dest), RelativePath = AppPaths.ToRelative(dest), Size = new FileInfo(dest).Length,
                };
                Card.Attachments.Add(att);
                if (Card.CoverAttachmentId == null && string.IsNullOrEmpty(Card.CoverColor)) Card.CoverAttachmentId = att.Id;
                Card.AddActivity("pasted an image", Me);
                return;
            }

            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText().Trim();
                if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
                    AddLinkInternal(text);
            }
        }
        catch (Exception ex)
        {
            _main.ShowToast($"Paste failed: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private async Task AddLink()
    {
        var url = await _main.PromptAsync("Attach a link", "Paste any web address.", "https://…", "Attach");
        if (url == null) return;
        if (!url.Contains("://")) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            _main.ShowToast("That doesn't look like a valid link.", isError: true);
            return;
        }

        AddLinkInternal(url);
    }

    private void AddLinkInternal(string url)
    {
        var name = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host + u.AbsolutePath.TrimEnd('/') : url;
        Card.Attachments.Add(new Attachment { Kind = AttachmentKind.Link, Name = name, Url = url });
        Card.AddActivity("attached a link", Me);
    }

    [RelayCommand]
    private void OpenAttachment(Attachment a)
    {
        try
        {
            if (a.Kind == AttachmentKind.Image)
            {
                _main.ShowDialog(new ImagePreviewViewModel(a));
                return;
            }

            var target = a.Kind == AttachmentKind.Link ? a.Url : a.FullPath;
            if (target == null) return;
            if (a.Kind != AttachmentKind.Link && !File.Exists(target))
            {
                _main.ShowToast("The file is missing from the data folder.", isError: true);
                return;
            }

            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _main.ShowToast($"Couldn't open: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void OpenExternally(Attachment a)
    {
        try
        {
            var target = a.Kind == AttachmentKind.Link ? a.Url : a.FullPath;
            if (target != null) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _main.ShowToast($"Couldn't open: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void ShowInFolder(Attachment a)
    {
        if (a.FullPath is { } p && File.Exists(p))
            Process.Start("explorer.exe", $"/select,\"{p}\"");
    }

    [RelayCommand]
    private void SaveAttachmentAs(Attachment a)
    {
        if (a.FullPath is not { } p || !File.Exists(p)) return;
        var dlg = new SaveFileDialog { FileName = a.Name, Filter = "All files|*.*" };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.Copy(p, dlg.FileName, true);
            }
            catch (Exception ex)
            {
                _main.ShowToast($"Couldn't save: {ex.Message}", isError: true);
            }
        }
    }

    [RelayCommand]
    private async Task RemoveAttachment(Attachment a)
    {
        var ok = await _main.ConfirmAsync("Delete attachment?", $"\"{a.Name}\" will be permanently deleted.", "Delete", danger: true);
        if (!ok) return;
        if (a.IsPlaying) _main.Player.Stop();
        Card.Attachments.Remove(a);
        if (Card.CoverAttachmentId == a.Id) Card.CoverAttachmentId = null;
        if (a.FullPath is { } p)
        {
            try { File.Delete(p); } catch { }
        }
    }

    public void HandleFileDrop(string[] files) => AddFilesFromPaths(files);

    // ---------- voice notes ----------

    [RelayCommand]
    private void StartRecording()
    {
        if (!AudioRecorder.HasMicrophone)
        {
            _main.ShowToast("No microphone was found.", isError: true);
            return;
        }

        try
        {
            _main.Player.Stop();
            Recorder.Start();
        }
        catch (Exception ex)
        {
            _main.ShowToast($"Couldn't start recording: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private async Task StopRecording()
    {
        var result = await Recorder.StopAsync();
        if (result is not { } r) return;
        if (r.Duration < TimeSpan.FromSeconds(0.5))
        {
            try { File.Delete(r.Path); } catch { }
            return;
        }

        var dest = AppPaths.UniquePath(AppPaths.CardFolder(Card.Id), $"Voice note {DateTime.Now:yyyy-MM-dd HHmmss}.wav");
        File.Move(r.Path, dest);
        var seconds = r.Duration.TotalSeconds;
        try
        {
            using var reader = new NAudio.Wave.WaveFileReader(dest);
            seconds = reader.TotalTime.TotalSeconds;
        }
        catch
        {
            // Use the wall-clock duration.
        }

        Card.Attachments.Add(new Attachment
        {
            Kind = AttachmentKind.Voice, Name = Path.GetFileName(dest), RelativePath = AppPaths.ToRelative(dest),
            Size = new FileInfo(dest).Length, DurationSeconds = seconds,
        });
        Card.AddActivity("recorded a voice note", Me);
    }

    [RelayCommand] private Task CancelRecording() => Recorder.CancelAsync();

    /// <summary>Adds audio files from the PC as voice notes.</summary>
    [RelayCommand]
    private void ImportVoice()
    {
        var files = MediaStore.PickAudio(multiple: true);
        if (files.Length > 0) AddFilesFromPaths(files);
    }

    [RelayCommand]
    private void TogglePlay(Attachment a)
    {
        try
        {
            _main.Player.Toggle(a);
        }
        catch (Exception ex)
        {
            _main.ShowToast($"Couldn't play: {ex.Message}", isError: true);
        }
    }

    public void Seek(Attachment a, double fraction) => _main.Player.Seek(a, fraction);

    [RelayCommand]
    private async Task RenameAttachment(Attachment a)
    {
        var name = await _main.PromptAsync("Rename", string.Empty, a.Name, "Rename");
        if (name != null) a.Name = name;
    }

    // ---------- time tracking / focus ----------

    [RelayCommand]
    private void ToggleTimer()
    {
        if (Card.IsTimerRunning) Timer.Stop();
        else Timer.Start(Card);
    }

    [RelayCommand]
    private void StartFocus()
    {
        _main.Pomodoro.LinkCard(Card);
        if (!_main.Pomodoro.IsRunning) _main.Pomodoro.Start();
        _main.ShowToast($"Focus session started for \"{Card.Title}\"");
    }

    [RelayCommand]
    private async Task LogTime()
    {
        var text = await _main.PromptAsync("Log time", "How much time did you spend? e.g. 45m, 1h 30m or 1.5h", "30m", "Log");
        if (text == null) return;
        if (!TryParseDuration(text, out var span) || span <= TimeSpan.Zero)
        {
            _main.ShowToast("Couldn't understand that duration.", isError: true);
            return;
        }

        var end = DateTime.Now;
        Card.TimeEntries.Add(new TimeEntry { Start = end - span, End = end, Source = "Manual" });
    }

    [RelayCommand]
    private void DeleteTimeEntry(TimeEntry e) => Card.TimeEntries.Remove(e);

    public static bool TryParseDuration(string text, out TimeSpan span)
    {
        span = TimeSpan.Zero;
        text = text.Trim().ToLowerInvariant().Replace(" ", string.Empty);
        if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var plain))
        {
            span = TimeSpan.FromMinutes(plain);
            return true;
        }

        if (TimeSpan.TryParse(text, out var ts))
        {
            span = ts;
            return true;
        }

        var m = System.Text.RegularExpressions.Regex.Match(text, @"^(?:(\d+(?:[.,]\d+)?)h)?(?:(\d+)m)?$");
        if (!m.Success || (m.Groups[1].Length == 0 && m.Groups[2].Length == 0)) return false;
        double hours = m.Groups[1].Length > 0 ? double.Parse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture) : 0;
        int minutes = m.Groups[2].Length > 0 ? int.Parse(m.Groups[2].Value) : 0;
        span = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
        return true;
    }

    // ---------- comments ----------

    [RelayCommand]
    private void AddComment()
    {
        var text = NewComment.Trim();
        if (text.Length == 0) return;
        Card.Comments.Add(new Comment { Author = Me, Text = text });
        NewComment = string.Empty;
    }

    [RelayCommand]
    private void DeleteComment(Comment c) => Card.Comments.Remove(c);

    // ---------- card actions ----------

    [RelayCommand]
    private void Archive()
    {
        _main.CloseDialog(this);
        _main.ArchiveCardCommand.Execute(Card);
    }

    [RelayCommand]
    private void Restore()
    {
        _main.RestoreCardCommand.Execute(Card);
        OnPropertyChanged(nameof(IsArchived));
        OnPropertyChanged(nameof(CurrentList));
    }

    [RelayCommand]
    private async Task Delete()
    {
        var ok = await _main.ConfirmAsync("Delete card?", $"\"{Card.Title}\" and all its attachments will be permanently deleted. You can undo this with Ctrl+Z (attachments excluded).", "Delete", danger: true);
        if (!ok) return;
        _main.CloseDialog(this);
        _main.DeleteCardPermanently(Card, Board);
    }

    [RelayCommand]
    private void Duplicate()
    {
        _main.DuplicateCardCommand.Execute(Card);
    }

    [RelayCommand]
    private void MoveToBoard(Board target)
    {
        _main.CloseDialog(this);
        _main.MoveCardToBoard(Card, Board, target);
    }

    [RelayCommand]
    private void CopyTitle()
    {
        try { Clipboard.SetText(Card.Title); } catch { }
        _main.ShowToast("Title copied");
    }
}
