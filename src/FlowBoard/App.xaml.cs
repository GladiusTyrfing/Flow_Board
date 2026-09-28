using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FlowBoard.Helpers;
using FlowBoard.Services;
using FlowBoard.ViewModels;

namespace FlowBoard;

public partial class App : Application
{
    private const string MutexName = "FlowBoard.SingleInstance.v2";
    private const string ShowEventName = "FlowBoard.ShowWindow.v2";

    private static Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private DataStore? _store;
    private TrayService? _tray;
    private ReminderService? _reminders;
    private MainWindow? _window;
    private MainViewModel? _vm;
    private GlobalHotkeyService? _hotkeys;
    private const int QuickAddHotkeyId = 0xB001;
    private const int ShowHideHotkeyId = 0xB002;
    private bool _trayHintShown;

    /// <summary>True while the app is really shutting down (vs. hiding to the tray).</summary>
    public static bool IsExiting { get; private set; }

    /// <summary>Set when a backup was restored: don't overwrite the restored files on exit.</summary>
    private static bool _skipSave;

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst && !e.Args.Contains("--restarted"))
        {
            // Another FlowBoard is running: ask it to come to the front and quit.
            try
            {
                using var ev = EventWaitHandle.OpenExisting(ShowEventName);
                ev.Set();
            }
            catch
            {
                // The other instance may be starting up; nothing else to do.
            }

            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;

        try
        {
            Start(e);
        }
        catch (Exception ex)
        {
            // Never leave an invisible process behind: report the problem and quit.
            ReportFatal(ex);
        }
    }

    private void Start(StartupEventArgs e)
    {
        AppPaths.Initialize();
        _store = new DataStore();
        _store.Load();

        ThemeService.Initialize(_store.Settings);
        Behaviors.AnimationsEnabled = _store.Settings.Animations;

        _vm = new MainViewModel(_store);
        _window = new MainWindow(_vm);
        MainWindow = _window;
        ThemeService.Attach(_window);

        SetUpTray();
        SetUpReminders();
        SetUpHotkeys();
        ListenForSecondInstance();

        _window.ContentRendered += (_, _) => _windowShownOnce = true;
        var startHidden = e.Args.Contains("--minimized") || _store.Settings.StartMinimized;
        if (startHidden) _windowShownOnce = true;
        if (!startHidden) _window.Show();
        else if (!_store.Settings.MinimizeToTray)
        {
            _window.WindowState = WindowState.Minimized;
            _window.Show();
        }

        _store.StartAutoSave();

        // Double-clicking a .flowboard file in Explorer opens that project.
        var file = e.Args.FirstOrDefault(a => a.EndsWith(AppPaths.ProjectExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(a));
        if (file != null) _vm.OpenProjectFile(file);
        _vm.ShowStartupWarning();
    }

    private void SetUpTray()
    {
        _tray = new TrayService();
        _tray.OpenRequested += (_, _) => ShowMainWindow();
        _tray.ExitRequested += (_, _) => ExitApp();
        _tray.QuickAddRequested += (_, _) => QuickAddFromAnywhere();
        _tray.FocusToggleRequested += (_, _) => _vm?.Pomodoro.Toggle();
        _tray.CardRequested += (_, id) =>
        {
            ShowMainWindow();
            if (_vm?.Workspace.FindCard(id, out var board, out Models.BoardList? _) is { } card && board != null)
            {
                _vm.SelectBoard(board);
                _vm.OpenCard(card);
            }
        };

        _vm!.PomodoroStateChanged += (_, _) =>
            _tray.SetFocusMenuText(_vm.Pomodoro.IsRunning ? "Pause focus session" : "Start focus session");

        _vm.Pomodoro.PhaseCompleted += (_, phase) =>
        {
            if (_store!.Settings.PlaySounds) System.Media.SystemSounds.Exclamation.Play();
            var (title, msg) = phase == PomodoroPhase.Focus
                ? ("Focus session complete", "Time for a break.")
                : ("Break is over", "Ready for the next focus session?");
            _tray.Notify(title, msg);
            _vm.ShowToast($"{title} — {msg}");
        };
    }

    private void SetUpReminders()
    {
        _reminders = new ReminderService(_store!);
        _reminders.ReminderDue += (card, board) =>
        {
            var when = card.DueDate is { } d ? Models.Card.FormatDate(d) : string.Empty;
            _tray?.Notify($"Reminder: {card.Title}", $"Due {when} · {board.Name}", card.Id);
            _vm?.ShowToast($"Reminder: \"{card.Title}\" is due {when}", "Open", () =>
            {
                _vm.SelectBoard(board);
                _vm.OpenCard(card);
            });
        };
        _reminders.Start();
    }

    private void SetUpHotkeys()
    {
        _hotkeys = new GlobalHotkeyService();
        _hotkeys.Attach(_window!);
        RegisterHotkeys(announceFailures: false);
        _store!.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Models.AppSettings.GlobalHotkeysEnabled)
                or nameof(Models.AppSettings.QuickAddHotkey)
                or nameof(Models.AppSettings.ShowHideHotkey))
                RegisterHotkeys(announceFailures: true);
        };
        _vm!.HideWindowRequested += (_, _) => HideMainWindow();
    }

    private void RegisterHotkeys(bool announceFailures)
    {
        if (_hotkeys == null || _store == null) return;
        _hotkeys.UnregisterAll();
        var s = _store.Settings;
        if (!s.GlobalHotkeysEnabled) return;

        var failed = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.QuickAddHotkey) && !_hotkeys.Register(QuickAddHotkeyId, s.QuickAddHotkey, QuickAddFromAnywhere))
            failed.Add(s.QuickAddHotkey);
        if (!string.IsNullOrWhiteSpace(s.ShowHideHotkey) && !_hotkeys.Register(ShowHideHotkeyId, s.ShowHideHotkey, ToggleMainWindow))
            failed.Add(s.ShowHideHotkey);

        if (failed.Count > 0)
        {
            var msg = $"Couldn't register {string.Join(" and ", failed)} — another app may be using it. Pick a different hotkey in Settings.";
            if (announceFailures) _vm?.ShowToast(msg, isError: true);
            else Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _vm?.ShowToast(msg, isError: true));
        }
    }

    private bool IsWindowInForeground => _window is { IsVisible: true, IsActive: true } && _window.WindowState != WindowState.Minimized;

    /// <summary>Global hotkey: pop the quick-add box over whatever you're doing; hide again afterwards if we were hidden.</summary>
    private void QuickAddFromAnywhere()
    {
        var wasHidden = !IsWindowInForeground;
        ShowMainWindow();
        _vm?.ShowQuickAdd(hideAfter: wasHidden);
    }

    private void ToggleMainWindow()
    {
        if (IsWindowInForeground) HideMainWindow();
        else ShowMainWindow();
    }

    private void HideMainWindow()
    {
        if (_window == null) return;
        if (_store!.Settings.MinimizeToTray) _window.Hide();
        else _window.WindowState = WindowState.Minimized;
    }

    private void ListenForSecondInstance()
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                if (IsExiting) return;
                Dispatcher.BeginInvoke(ShowMainWindow);
            }
        })
        { IsBackground = true, Name = "FlowBoard single-instance listener" };
        thread.Start();
    }

    public void ShowMainWindow()
    {
        if (_window == null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    /// <summary>Called when the window is closed with "minimize to tray" enabled.</summary>
    public void OnHiddenToTray()
    {
        if (_trayHintShown) return;
        _trayHintShown = true;
        _tray?.Notify("FlowBoard is still running", "Reminders and the focus timer keep working. Right-click the tray icon to exit.");
    }

    /// <summary>The window is closing for real (tray disabled): shut down once the close completes.</summary>
    public void ExitFromWindowClosing()
    {
        IsExiting = true;
        Dispatcher.BeginInvoke(Shutdown);
    }

    public void ExitApp()
    {
        IsExiting = true;
        _window?.Close();
        Shutdown();
    }

    /// <summary>Restart after restoring a backup.</summary>
    public static void Restart()
    {
        _skipSave = true;
        IsExiting = true;
        try
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            _mutex = null;
        }
        catch
        {
            // Not owned; fine.
        }

        if (Environment.ProcessPath is { } exe) Process.Start(exe, "--restarted");
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true;
        try
        {
            _vm?.Timer.Stop();
            _vm?.Pomodoro.Pause();
            if (!_skipSave) _store?.SaveIfChanged();
            _vm?.PurgeDeletedAttachments();
            _vm?.Player.Dispose();
            _vm?.Recorder.Dispose();
        }
        finally
        {
            _hotkeys?.Dispose();
            _tray?.Dispose();
            _showEvent?.Set();
            base.OnExit(e);
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        LogError(e.Exception);

        // Before the window is up there is nothing to recover to.
        if (_window == null || !_windowShownOnce)
        {
            ReportFatal(e.Exception);
            return;
        }

        _vm?.ShowToast($"Something went wrong: {e.Exception.Message}", isError: true);
    }

    private bool _windowShownOnce;

    private static string LogError(Exception ex)
    {
        var path = Path.Combine(AppPaths.AppDir, "error.log");
        try
        {
            Directory.CreateDirectory(AppPaths.AppDir);
            File.AppendAllText(path, $"[{DateTime.Now:u}] {ex}\n\n");
        }
        catch
        {
            // Ignore logging failures.
        }

        return path;
    }

    private void ReportFatal(Exception ex)
    {
        var log = LogError(ex);
        MessageBox.Show(
            $"FlowBoard couldn't start:\n\n{ex.GetType().Name}: {ex.Message}\n\nDetails were written to:\n{log}",
            "FlowBoard", MessageBoxButton.OK, MessageBoxImage.Error);
        IsExiting = true;
        try { _tray?.Dispose(); } catch { }
        Shutdown(1);
    }
}
