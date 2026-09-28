using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowBoard.Models;

namespace FlowBoard.Services;

public enum PomodoroPhase
{
    Idle,
    Focus,
    ShortBreak,
    LongBreak,
}

/// <summary>Focus timer. Completed focus time can be logged to a linked card.</summary>
public sealed partial class PomodoroService : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer;
    private DateTime _lastTick;
    private DateTime? _focusStartedAt;
    private TimeSpan _focusAccumulated;

    [ObservableProperty] private PomodoroPhase _phase = PomodoroPhase.Idle;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private TimeSpan _remaining;
    [ObservableProperty] private TimeSpan _total;
    [ObservableProperty] private int _completedSessions;
    [ObservableProperty] private Guid? _linkedCardId;
    [ObservableProperty] private string? _linkedCardTitle;

    /// <summary>Raised when a phase finishes naturally.</summary>
    public event EventHandler<PomodoroPhase>? PhaseCompleted;

    /// <summary>Raised with (cardId, start, end) when focus time should be logged.</summary>
    public event Action<Guid, DateTime, DateTime>? FocusLogged;

    public PomodoroService(AppSettings settings)
    {
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTick;
        Remaining = Total = TimeSpan.FromMinutes(settings.PomodoroFocusMinutes);
    }

    public double Progress => Total.TotalSeconds <= 0 ? 0 : 1 - Remaining.TotalSeconds / Total.TotalSeconds;
    public string DisplayText => Remaining.ToString(Remaining.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
    public bool IsActive => Phase != PomodoroPhase.Idle;
    public bool IsBreak => Phase is PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak;

    public string PhaseText => Phase switch
    {
        PomodoroPhase.Focus => "Focus",
        PomodoroPhase.ShortBreak => "Short break",
        PomodoroPhase.LongBreak => "Long break",
        _ => "Ready",
    };

    partial void OnRemainingChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(DisplayText));
    }

    partial void OnPhaseChanged(PomodoroPhase value)
    {
        OnPropertyChanged(nameof(PhaseText));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsBreak));
    }

    public void LinkCard(Card? card)
    {
        LinkedCardId = card?.Id;
        LinkedCardTitle = card?.Title;
    }

    public void Toggle()
    {
        if (IsRunning) Pause();
        else Start();
    }

    public void Start()
    {
        if (Phase == PomodoroPhase.Idle) BeginPhase(PomodoroPhase.Focus);
        IsRunning = true;
        _lastTick = DateTime.Now;
        if (Phase == PomodoroPhase.Focus) _focusStartedAt ??= DateTime.Now;
        _timer.Start();
    }

    public void Pause()
    {
        IsRunning = false;
        _timer.Stop();
        AccumulateFocus();
    }

    /// <summary>Stops everything and logs any focus time already spent.</summary>
    public void Reset()
    {
        Pause();
        FlushFocusLog();
        Phase = PomodoroPhase.Idle;
        Remaining = Total = TimeSpan.FromMinutes(_settings.PomodoroFocusMinutes);
    }

    public void Skip()
    {
        var wasRunning = IsRunning;
        if (Phase == PomodoroPhase.Focus)
        {
            AccumulateFocus();
            FlushFocusLog();
        }

        MoveToNextPhase(countSession: false);
        if (wasRunning) Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        var delta = now - _lastTick;
        _lastTick = now;
        Remaining -= delta;
        if (Remaining > TimeSpan.Zero) return;

        Remaining = TimeSpan.Zero;
        var finished = Phase;
        if (finished == PomodoroPhase.Focus)
        {
            AccumulateFocus();
            FlushFocusLog();
        }

        MoveToNextPhase(countSession: finished == PomodoroPhase.Focus);
        PhaseCompleted?.Invoke(this, finished);

        var autoStart = Phase == PomodoroPhase.Focus ? _settings.PomodoroAutoStartFocus : _settings.PomodoroAutoStartBreaks;
        if (autoStart) Start();
        else Pause();
    }

    private void MoveToNextPhase(bool countSession)
    {
        if (Phase == PomodoroPhase.Focus)
        {
            if (countSession) CompletedSessions++;
            var longBreak = countSession && CompletedSessions % Math.Max(1, _settings.PomodoroSessionsBeforeLongBreak) == 0;
            BeginPhase(longBreak ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak);
        }
        else
        {
            BeginPhase(PomodoroPhase.Focus);
        }
    }

    private void BeginPhase(PomodoroPhase phase)
    {
        Phase = phase;
        var minutes = phase switch
        {
            PomodoroPhase.ShortBreak => _settings.PomodoroShortBreakMinutes,
            PomodoroPhase.LongBreak => _settings.PomodoroLongBreakMinutes,
            _ => _settings.PomodoroFocusMinutes,
        };
        Remaining = Total = TimeSpan.FromMinutes(Math.Max(1, minutes));
        _focusStartedAt = null;
    }

    private void AccumulateFocus()
    {
        if (Phase == PomodoroPhase.Focus && _focusStartedAt is { } started)
        {
            _focusAccumulated += DateTime.Now - started;
            _focusStartedAt = null;
        }
    }

    private void FlushFocusLog()
    {
        if (_focusAccumulated >= TimeSpan.FromMinutes(1) && LinkedCardId is { } id)
        {
            var end = DateTime.Now;
            FocusLogged?.Invoke(id, end - _focusAccumulated, end);
        }

        _focusAccumulated = TimeSpan.Zero;
    }
}
