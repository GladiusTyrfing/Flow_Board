using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;

namespace FlowBoard.ViewModels;

/// <summary>A storyboard: a row of shots (film) or frames (animation) with notes, sketches and an animatic player.</summary>
public sealed partial class StoryboardViewModel : DocumentViewModel
{
    public StoryboardViewModel(MainViewModel main, Storyboard board) : base(main)
    {
        Board = board;
        Board.Shots.CollectionChanged += OnShotsChanged;
        Renumber();
    }

    public Storyboard Board { get; }
    public override object Model => Board;
    public override ActiveView Kind => ActiveView.Storyboard;

    public string[] ShotTypes => Shot.ShotTypes;
    public string[] Angles => Shot.Angles;
    public string[] Movements => Shot.Movements;
    public string[] Transitions => Shot.Transitions;
    public AudioRecorder Recorder => Main.Recorder;

    /// <summary>Shot currently being voice-recorded (animation mode).</summary>
    [ObservableProperty] private Shot? _recordingShot;
    [ObservableProperty] private bool _isEditingName;

    public bool IsAnimation
    {
        get => Board.Mode == StoryboardMode.Animation;
        set
        {
            var mode = value ? StoryboardMode.Animation : StoryboardMode.Film;
            if (Board.Mode == mode) return;
            Checkpoint();
            Board.Mode = mode;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShotWord));
            OnPropertyChanged(nameof(Summary));
        }
    }

    public string ShotWord => IsAnimation ? "Frame" : "Shot";

    public string Summary
    {
        get
        {
            var n = Board.Shots.Count;
            var word = IsAnimation ? (n == 1 ? "frame" : "frames") : (n == 1 ? "shot" : "shots");
            var secs = Board.Shots.Sum(s => s.DurationSeconds);
            var time = TimeSpan.FromSeconds(secs);
            return $"{n} {word} · {(time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss"))} runtime";
        }
    }

    private void OnShotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Renumber();
        OnPropertyChanged(nameof(Summary));
    }

    private void Renumber()
    {
        for (int i = 0; i < Board.Shots.Count; i++) Board.Shots[i].Number = i + 1;
    }

    public override void OnDeactivated()
    {
        Board.Shots.CollectionChanged -= OnShotsChanged;
        if (Recorder.IsRecording && RecordingShot != null) _ = Recorder.CancelAsync();
        Main.Player.Stop();
    }

    protected override void Restore(string json)
    {
        var snap = JsonSerializer.Deserialize<Storyboard>(json, Json.Options);
        if (snap == null) return;
        Board.Name = snap.Name;
        Board.Mode = snap.Mode;
        Board.Shots.Clear();
        foreach (var s in snap.Shots) Board.Shots.Add(s);
        OnPropertyChanged(nameof(IsAnimation));
        OnPropertyChanged(nameof(ShotWord));
    }

    // ---------- name ----------

    [RelayCommand] private void StartRename() => IsEditingName = true;

    public override void BeginRename() => IsEditingName = true;

    [RelayCommand]
    private void CommitName()
    {
        IsEditingName = false;
        if (string.IsNullOrWhiteSpace(Board.Name)) Board.Name = "Untitled storyboard";
        Main.RefreshDocSidebar();
    }

    // ---------- shots ----------

    [RelayCommand]
    private void AddShot(Shot? after)
    {
        Checkpoint();
        var index = after == null ? Board.Shots.Count : Board.Shots.IndexOf(after) + 1;
        var shot = new Shot { Title = string.Empty, Scene = after?.Scene ?? string.Empty };
        if (after != null)
        {
            shot.ShotType = after.ShotType;
            shot.Location = after.Location;
            shot.DurationSeconds = after.DurationSeconds;
        }

        Board.Shots.Insert(index, shot);
        OnPropertyChanged(nameof(Summary));
    }

    [RelayCommand]
    private void DuplicateShot(Shot shot)
    {
        Checkpoint();
        var copy = Json.CloneDocument(shot);
        copy.Id = Guid.NewGuid();
        copy.VoiceNote = null;
        Board.Shots.Insert(Board.Shots.IndexOf(shot) + 1, copy);
    }

    [RelayCommand]
    private void DeleteShot(Shot shot)
    {
        Checkpoint();
        var index = Board.Shots.IndexOf(shot);
        Board.Shots.Remove(shot);
        Main.ShowToast($"Deleted {ShotWord.ToLowerInvariant()} {index + 1}", "Undo", () => Undo());
    }

    [RelayCommand]
    private void MoveShotLeft(Shot shot)
    {
        var i = Board.Shots.IndexOf(shot);
        if (i <= 0) return;
        Checkpoint();
        Board.Shots.Move(i, i - 1);
    }

    [RelayCommand]
    private void MoveShotRight(Shot shot)
    {
        var i = Board.Shots.IndexOf(shot);
        if (i < 0 || i >= Board.Shots.Count - 1) return;
        Checkpoint();
        Board.Shots.Move(i, i + 1);
    }

    /// <summary>Called by the drag &amp; drop handler before it reorders.</summary>
    public void BeforeReorder() => Checkpoint();

    // ---------- frame image ----------

    [RelayCommand]
    private void PickImage(Shot shot)
    {
        try
        {
            var rel = MediaStore.PickImage(Board.Id);
            if (rel == null) return;
            Checkpoint();
            shot.ImagePath = rel;
            shot.SketchPath = null;
            shot.SketchBackgroundPath = null;
        }
        catch (Exception ex)
        {
            Main.ShowToast($"Couldn't add the image: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void PasteImage(Shot shot)
    {
        var rel = MediaStore.FromClipboard(Board.Id);
        if (rel == null)
        {
            Main.ShowToast("There's no image on the clipboard.", isError: true);
            return;
        }

        Checkpoint();
        shot.ImagePath = rel;
        shot.SketchPath = null;
        shot.SketchBackgroundPath = null;
    }

    public void DropImage(Shot shot, string file)
    {
        if (!MediaStore.IsImageFile(file)) return;
        try
        {
            Checkpoint();
            shot.ImagePath = MediaStore.ImportFile(file, Board.Id);
            shot.SketchPath = null;
            shot.SketchBackgroundPath = null;
        }
        catch (Exception ex)
        {
            Main.ShowToast($"Couldn't add the image: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void ClearImage(Shot shot)
    {
        Checkpoint();
        shot.ImagePath = null;
        shot.SketchPath = null;
        shot.SketchBackgroundPath = null;
    }

    [RelayCommand]
    private void Sketch(Shot shot) => Main.ShowDialog(new SketchPadViewModel(this, shot));

    [RelayCommand]
    private void ViewImage(Shot shot)
    {
        if (shot.ImageFullPath is { } p && File.Exists(p))
            Main.ShowDialog(new ImagePreviewViewModel(new Attachment { Kind = AttachmentKind.Image, Name = shot.Title, RelativePath = shot.ImagePath }));
    }

    public void ApplySketch(Shot shot, string pngRelative, string inkRelative, string? backgroundRelative)
    {
        Checkpoint();
        shot.ImagePath = pngRelative;
        shot.SketchPath = inkRelative;
        shot.SketchBackgroundPath = backgroundRelative;
    }

    // ---------- tags / shot list / equipment ----------

    [RelayCommand]
    private void AddTag(Shot shot)
    {
        var t = shot.NewTag.Trim().TrimStart('#');
        if (t.Length == 0) return;
        if (!shot.Tags.Any(x => x.Text.Equals(t, StringComparison.OrdinalIgnoreCase))) shot.Tags.Add(new TextItem { Text = t });
        shot.NewTag = string.Empty;
    }

    [RelayCommand]
    private void AddShotItem(Shot shot)
    {
        var t = shot.NewShotItem.Trim();
        if (t.Length == 0) return;
        shot.ShotList.Add(new ChecklistItem { Text = t });
        shot.NewShotItem = string.Empty;
    }

    [RelayCommand]
    private void AddEquipment(Shot shot)
    {
        var t = shot.NewEquipment.Trim();
        if (t.Length == 0) return;
        shot.Equipment.Add(new TextItem { Text = t });
        shot.NewEquipment = string.Empty;
    }

    /// <summary>Removes a tag, equipment row or shot-list item from whichever shot holds it.</summary>
    [RelayCommand]
    private void RemoveItem(object item)
    {
        foreach (var s in Board.Shots)
        {
            if (item is TextItem t && (s.Tags.Remove(t) || s.Equipment.Remove(t))) return;
            if (item is ChecklistItem c && s.ShotList.Remove(c)) return;
        }
    }

    [RelayCommand] private void SetShotType(object[] args) => SetField(args, (s, v) => s.ShotType = v);
    [RelayCommand] private void SetAngle(object[] args) => SetField(args, (s, v) => s.Angle = v);
    [RelayCommand] private void SetMovement(object[] args) => SetField(args, (s, v) => s.Movement = v);
    [RelayCommand] private void SetTransition(object[] args) => SetField(args, (s, v) => s.Transition = v);

    private static void SetField(object[] args, Action<Shot, string> set)
    {
        if (args is [Shot s, string v]) set(s, v);
    }

    // ---------- voice (animation) ----------

    [RelayCommand]
    private async Task ToggleRecord(Shot shot)
    {
        if (Recorder.IsRecording)
        {
            var target = RecordingShot;
            RecordingShot = null;
            var result = await Recorder.StopAsync();
            if (result is not { } r || target == null) return;
            if (r.Duration < TimeSpan.FromSeconds(0.5))
            {
                try { File.Delete(r.Path); } catch { }
                return;
            }

            var dest = AppPaths.UniquePath(MediaStore.DocFolder(Board.Id), $"Voice {target.Number} {DateTime.Now:HHmmss}.wav");
            File.Move(r.Path, dest);
            double seconds = r.Duration.TotalSeconds;
            try
            {
                using var reader = new NAudio.Wave.WaveFileReader(dest);
                seconds = reader.TotalTime.TotalSeconds;
            }
            catch
            {
                // keep wall-clock duration
            }

            Checkpoint();
            target.VoiceNote = new Attachment
            {
                Kind = AttachmentKind.Voice, Name = Path.GetFileName(dest), RelativePath = AppPaths.ToRelative(dest),
                Size = new FileInfo(dest).Length, DurationSeconds = seconds,
            };
            if (target.DurationSeconds < seconds) target.DurationSeconds = Math.Ceiling(seconds * 2) / 2;
            OnPropertyChanged(nameof(Summary));
            return;
        }

        if (!AudioRecorder.HasMicrophone)
        {
            Main.ShowToast("No microphone was found.", isError: true);
            return;
        }

        try
        {
            Main.Player.Stop();
            Recorder.Start();
            RecordingShot = shot;
        }
        catch (Exception ex)
        {
            Main.ShowToast($"Couldn't start recording: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void PlayVoice(Shot shot)
    {
        if (shot.VoiceNote == null) return;
        try { Main.Player.Toggle(shot.VoiceNote); }
        catch (Exception ex) { Main.ShowToast($"Couldn't play: {ex.Message}", isError: true); }
    }

    [RelayCommand]
    private void RemoveVoice(Shot shot)
    {
        if (shot.VoiceNote == null) return;
        Main.Player.Stop();
        Checkpoint();
        shot.VoiceNote = null;
    }

    [RelayCommand]
    private void ChangeDuration(object[] args)
    {
        if (args is [Shot s, string delta] && double.TryParse(delta, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            s.DurationSeconds = Math.Clamp(Math.Round((s.DurationSeconds + d) * 2) / 2, 0.5, 600);
            OnPropertyChanged(nameof(Summary));
        }
    }

    // ---------- play / export ----------

    [RelayCommand]
    private void PlayAnimatic(Shot? from)
    {
        if (Board.Shots.Count == 0) return;
        Main.Player.Stop();
        Main.ShowDialog(new AnimaticViewModel(this, from == null ? 0 : Math.Max(0, Board.Shots.IndexOf(from))));
    }

    /// <summary>Raised when the view should render itself to PNG ("export") or print ("print").</summary>
    public event EventHandler<string>? RenderRequested;

    [RelayCommand] private void ExportImage() => RenderRequested?.Invoke(this, "export");
    [RelayCommand] private void Print() => RenderRequested?.Invoke(this, "print");

    [RelayCommand]
    private void CreateCards()
    {
        var board = Main.CurrentBoard;
        var list = board?.Lists.FirstOrDefault(l => !l.IsDoneList);
        if (board == null || list == null)
        {
            Main.ShowToast("Open a board with a list first.", isError: true);
            return;
        }

        foreach (var s in Board.Shots)
        {
            var title = string.IsNullOrWhiteSpace(s.Title) ? $"{Board.Name} — {ShotWord} {s.Number}" : $"{ShotWord} {s.Number}: {s.Title}";
            var card = new Card { Title = title, Description = s.Description, DueDate = s.Date };
            if (s.ShotList.Count > 0)
            {
                var cl = new Checklist { Title = "Shots required" };
                foreach (var i in s.ShotList) cl.Items.Add(new ChecklistItem { Text = i.Text, IsDone = i.IsDone });
                card.Checklists.Add(cl);
            }

            list.Cards.Add(card);
        }

        board.Hydrate();
        Main.ShowToast($"Added {Board.Shots.Count} cards to \"{list.Name}\" on {board.Name}");
    }
}
