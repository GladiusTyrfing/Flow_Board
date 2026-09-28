using System.Diagnostics;
using System.IO;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;

namespace FlowBoard.ViewModels;

/// <summary>Draw a frame (optionally over the current image). Saves a PNG plus the ink strokes so it stays editable.</summary>
public sealed partial class SketchPadViewModel : DialogViewModel
{
    private readonly StoryboardViewModel _owner;

    public SketchPadViewModel(StoryboardViewModel owner, Shot shot)
    {
        _owner = owner;
        Shot = shot;
        // Re-editing a sketch: draw over its original photo. Otherwise draw over the current image (if any).
        BackgroundPath = shot.SketchPath != null ? shot.SketchBackgroundPath : shot.ImagePath;
        if (shot.SketchPath is { } ink && File.Exists(AppPaths.ToFull(ink)))
        {
            try
            {
                using var fs = File.OpenRead(AppPaths.ToFull(ink));
                Strokes = new StrokeCollection(fs);
            }
            catch
            {
                Strokes = [];
            }
        }
    }

    public override bool CloseOnBackdropClick => false;
    public Shot Shot { get; }
    public StrokeCollection Strokes { get; } = [];
    public string? BackgroundPath { get; }
    public string? BackgroundFullPath => BackgroundPath == null ? null : AppPaths.ToFull(BackgroundPath);

    public static readonly string[] Palette =
        ["#111827", "#FFFFFF", "#EF4444", "#F97316", "#FACC15", "#22C55E", "#06B6D4", "#3B82F6", "#8B5CF6", "#EC4899", "#94A3B8", "#78350F"];

    public string[] Colors => Palette;

    [ObservableProperty] private string _penColor = "#111827";
    [ObservableProperty] private double _penSize = 4;
    [ObservableProperty] private bool _isEraser;
    [ObservableProperty] private bool _isHighlighter;
    [ObservableProperty] private bool _showBackground = true;
    [ObservableProperty] private bool _showGuides = true;

    [RelayCommand]
    private void SetColor(string hex)
    {
        PenColor = hex;
        IsEraser = false;
    }

    [RelayCommand]
    private void SetSize(string size)
    {
        if (double.TryParse(size, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v)) PenSize = v;
        IsEraser = false;
    }

    [RelayCommand]
    private void UndoStroke()
    {
        if (Strokes.Count > 0) Strokes.RemoveAt(Strokes.Count - 1);
    }

    [RelayCommand]
    private void ClearAll() => Strokes.Clear();

    /// <summary>Called by the view with the rendered frame.</summary>
    public void Save(BitmapSource frame)
    {
        try
        {
            var folder = MediaStore.DocFolder(_owner.Board.Id);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var png = MediaStore.SavePng(frame, _owner.Board.Id, $"Sketch {Shot.Number} {stamp}.png");
            var inkPath = AppPaths.UniquePath(folder, $"Sketch {Shot.Number} {stamp}.isf");
            using (var fs = File.Create(inkPath)) Strokes.Save(fs);

            // Old files are kept on purpose: undo (and duplicated storyboards) may still point at them.
            _owner.ApplySketch(Shot, png, AppPaths.ToRelative(inkPath), ShowBackground ? BackgroundPath : null);
            Close();
        }
        catch (Exception ex)
        {
            MainViewModel.Instance.ShowToast($"Couldn't save the sketch: {ex.Message}", isError: true);
        }
    }
}

/// <summary>Plays the storyboard as a timed slideshow (with each frame's voice note) — an animatic.</summary>
public sealed partial class AnimaticViewModel : DialogViewModel
{
    private readonly StoryboardViewModel _owner;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private double _offset;

    public AnimaticViewModel(StoryboardViewModel owner, int start)
    {
        _owner = owner;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Tick();
        Index = Math.Clamp(start, 0, Math.Max(0, Shots.Count - 1));
        Enter();
        Play();
    }

    public override string EnterAnimation => "fade";
    public IList<Shot> Shots => _owner.Board.Shots;
    public string Title => _owner.Board.Name;
    public bool IsAnimation => _owner.IsAnimation;
    public string ShotWord => _owner.ShotWord;
    public double TotalSeconds => Math.Max(0.1, Shots.Sum(s => s.DurationSeconds));

    [ObservableProperty] private int _index;
    [ObservableProperty] private Shot? _current;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _loop;
    [ObservableProperty] private double _shotProgress;
    [ObservableProperty] private double _totalProgress;
    [ObservableProperty] private string _timeText = "0:00";

    /// <summary>Raised when a new shot is shown; the argument is its transition ("Cut", "Fade", ...).</summary>
    public event EventHandler<string>? ShotEntered;

    public string Caption
    {
        get
        {
            if (Current == null) return string.Empty;
            var parts = IsAnimation
                ? new[] { Current.Action, string.IsNullOrWhiteSpace(Current.Dialogue) ? string.Empty : $"“{Current.Dialogue}”" }
                : new[] { Current.Description };
            return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }

    public string Counter => $"{ShotWord} {Index + 1} / {Shots.Count}";

    private double Elapsed => _offset + _clock.Elapsed.TotalSeconds;

    private void Enter()
    {
        Current = Shots.Count == 0 ? null : Shots[Index];
        _offset = 0;
        if (_clock.IsRunning) _clock.Restart();
        else _clock.Reset();
        OnPropertyChanged(nameof(Caption));
        OnPropertyChanged(nameof(Counter));
        MainViewModel.Instance.Player.Stop();
        if (IsPlaying) StartVoice();
        ShotEntered?.Invoke(this, Current?.Transition ?? "Cut");
        UpdateProgress();
    }

    private void StartVoice()
    {
        if (Current?.VoiceNote is not { } v || !v.Exists) return;
        try
        {
            if (!v.IsPlaying) MainViewModel.Instance.Player.Toggle(v);
        }
        catch
        {
            // Playback problems shouldn't stop the animatic.
        }
    }

    private void Tick()
    {
        if (Current == null) return;
        if (Elapsed >= Current.DurationSeconds)
        {
            if (Index < Shots.Count - 1)
            {
                Index++;
                Enter();
            }
            else if (Loop)
            {
                Index = 0;
                Enter();
            }
            else
            {
                Pause();
                _offset = Current.DurationSeconds;
            }
        }

        UpdateProgress();
    }

    private void UpdateProgress()
    {
        if (Current == null) return;
        var e = Math.Min(Elapsed, Current.DurationSeconds);
        ShotProgress = e / Math.Max(0.1, Current.DurationSeconds);
        var before = Shots.Take(Index).Sum(s => s.DurationSeconds);
        TotalProgress = Math.Clamp((before + e) / TotalSeconds, 0, 1);
        TimeText = $"{TimeSpan.FromSeconds(before + e):m\\:ss} / {TimeSpan.FromSeconds(TotalSeconds):m\\:ss}";
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    private void Play()
    {
        if (Current == null) return;
        if (Index == Shots.Count - 1 && Elapsed >= Current.DurationSeconds)
        {
            Index = 0;
            IsPlaying = true;
            _clock.Restart();
            _timer.Start();
            Enter();
            return;
        }

        IsPlaying = true;
        _clock.Start();
        _timer.Start();
        if (Elapsed < 0.05) StartVoice();
    }

    private void Pause()
    {
        IsPlaying = false;
        _offset += _clock.Elapsed.TotalSeconds;
        _clock.Reset();
        _timer.Stop();
        MainViewModel.Instance.Player.Stop();
    }

    [RelayCommand]
    private void Next()
    {
        if (Index >= Shots.Count - 1) return;
        Index++;
        Enter();
    }

    [RelayCommand]
    private void Previous()
    {
        // First press restarts the shot, a second one goes back.
        if (Elapsed > 1 || Index == 0)
        {
            Enter();
            return;
        }

        Index--;
        Enter();
    }

    [RelayCommand]
    private void Restart()
    {
        Index = 0;
        Enter();
        if (!IsPlaying) Play();
    }

    public void SeekTotal(double fraction)
    {
        var target = Math.Clamp(fraction, 0, 1) * TotalSeconds;
        double acc = 0;
        for (int i = 0; i < Shots.Count; i++)
        {
            if (target < acc + Shots[i].DurationSeconds || i == Shots.Count - 1)
            {
                if (i != Index || Current == null)
                {
                    Index = i;
                    Enter();
                }

                _offset = Math.Max(0, target - acc);
                if (_clock.IsRunning) _clock.Restart();
                else _clock.Reset();
                UpdateProgress();
                return;
            }

            acc += Shots[i].DurationSeconds;
        }
    }

    public override void OnClosed()
    {
        _timer.Stop();
        _clock.Stop();
        MainViewModel.Instance.Player.Stop();
    }
}
