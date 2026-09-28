using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowBoard.Models;

namespace FlowBoard.Services;

/// <summary>A single global stopwatch that logs time entries to cards.</summary>
public sealed partial class TimeTracker : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private Card? _card;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private DateTime _startedAt;
    [ObservableProperty] private string _elapsedText = "0:00";
    [ObservableProperty] private string? _cardTitle;

    public TimeTracker()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateElapsed();
    }

    public Card? Card => _card;

    public void Start(Card card)
    {
        if (_card == card && IsRunning) return;
        Stop();
        _card = card;
        card.IsTimerRunning = true;
        CardTitle = card.Title;
        StartedAt = DateTime.Now;
        IsRunning = true;
        UpdateElapsed();
        _timer.Start();
    }

    /// <summary>Stops the timer and logs the entry (if at least a few seconds long).</summary>
    public void Stop()
    {
        if (!IsRunning || _card == null) return;
        _timer.Stop();
        var end = DateTime.Now;
        if (end - StartedAt >= TimeSpan.FromSeconds(5))
            _card.TimeEntries.Add(new TimeEntry { Start = StartedAt, End = end, Source = "Timer" });
        _card.IsTimerRunning = false;
        _card = null;
        CardTitle = null;
        IsRunning = false;
        ElapsedText = "0:00";
    }

    /// <summary>After undo replaces card objects, re-point the running timer at the new instance.</summary>
    public void Rebind(Workspace ws)
    {
        if (_card == null) return;
        var fresh = ws.FindCard(_card.Id, out _, out _);
        if (fresh == null)
        {
            _timer.Stop();
            _card = null;
            IsRunning = false;
            CardTitle = null;
            return;
        }

        _card = fresh;
        fresh.IsTimerRunning = true;
    }

    private void UpdateElapsed()
    {
        var e = DateTime.Now - StartedAt;
        ElapsedText = e.TotalHours >= 1 ? e.ToString(@"h\:mm\:ss") : e.ToString(@"m\:ss");
    }
}
