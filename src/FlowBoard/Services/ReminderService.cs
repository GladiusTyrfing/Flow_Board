using System.Media;
using System.Windows.Threading;
using FlowBoard.Models;

namespace FlowBoard.Services;

public sealed class ReminderService
{
    private readonly DataStore _store;
    private readonly DispatcherTimer _timer;

    public event Action<Card, Board>? ReminderDue;

    public ReminderService(DataStore store)
    {
        _store = store;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => Check();
    }

    public void Start()
    {
        _timer.Start();
        Check();
    }

    public void Check()
    {
        var now = DateTime.Now;
        foreach (var (board, _, card) in _store.Workspace.EnumerateActiveCards().ToList())
        {
            card.RefreshTimeState();
            if (!_store.Settings.RemindersEnabled) continue;
            if (card.IsCompleted || card.ReminderSent || card.DueDate is not { } due || card.ReminderMinutes < 0) continue;

            var at = due.TimeOfDay == TimeSpan.Zero ? due.Date.AddHours(9) : due;
            if (now >= at.AddMinutes(-card.ReminderMinutes))
            {
                card.ReminderSent = true;
                if (now - at < TimeSpan.FromDays(1))
                {
                    if (_store.Settings.PlaySounds) SystemSounds.Asterisk.Play();
                    ReminderDue?.Invoke(card, board);
                }
            }
        }
    }
}
