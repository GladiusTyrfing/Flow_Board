using CommunityToolkit.Mvvm.ComponentModel;

namespace FlowBoard.Models;

/// <summary>Per-user preferences, persisted to settings.json.</summary>
public partial class AppSettings : ObservableObject
{
    [ObservableProperty] private ThemeMode _theme = ThemeMode.Dark;
    [ObservableProperty] private BackdropMode _backdrop = BackdropMode.Mica;
    /// <summary>Hex accent color, or null to follow the Windows accent color.</summary>
    [ObservableProperty] private string? _accentColor;
    [ObservableProperty] private bool _animations = true;
    /// <summary>Small overview map in the corner of boards, storyboards, canvases, timelines and pages.</summary>
    [ObservableProperty] private bool _showMinimap = true;
    /// <summary>Accent color pair used for buttons, highlights and the background glow.</summary>
    [ObservableProperty] private string _accentPreset = "Aurora";
    /// <summary>Soft colored glow behind the whole app.</summary>
    [ObservableProperty] private bool _auroraBackground = true;
    /// <summary>Corner roundness for the whole app, in pixels (0 = sharp).</summary>
    [ObservableProperty] private double _cornerRadius = 8;
    [ObservableProperty] private string _displayName = Environment.UserName;
    [ObservableProperty] private bool _minimizeToTray = true;
    /// <summary>Completing a card moves it to the board's Done list; un-completing moves it back.</summary>
    [ObservableProperty] private bool _moveCompletedToDone = true;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _remindersEnabled = true;
    [ObservableProperty] private bool _playSounds = true;
    [ObservableProperty] private int _defaultReminderMinutes = 15;
    [ObservableProperty] private int _pomodoroFocusMinutes = 25;
    [ObservableProperty] private int _pomodoroShortBreakMinutes = 5;
    [ObservableProperty] private int _pomodoroLongBreakMinutes = 15;
    [ObservableProperty] private int _pomodoroSessionsBeforeLongBreak = 4;
    [ObservableProperty] private bool _pomodoroAutoStartBreaks = true;
    [ObservableProperty] private bool _pomodoroAutoStartFocus;
    [ObservableProperty] private bool _sidebarVisible = true;
    [ObservableProperty] private bool _showCompletedInCalendar = true;
    [ObservableProperty] private bool _compactCards;
    [ObservableProperty] private Guid? _lastBoardId;
    [ObservableProperty] private double _windowLeft = double.NaN;
    [ObservableProperty] private double _windowTop = double.NaN;
    [ObservableProperty] private double _windowWidth = 1400;
    [ObservableProperty] private double _windowHeight = 860;
    [ObservableProperty] private bool _windowMaximized;
    [ObservableProperty] private DateTime _lastBackup = DateTime.MinValue;
    [ObservableProperty] private bool _hasSeenWelcome;
    [ObservableProperty] private bool _globalHotkeysEnabled = true;
    /// <summary>System-wide hotkey that opens the quick-add box from any app.</summary>
    [ObservableProperty] private string _quickAddHotkey = "Ctrl+Alt+Space";
    /// <summary>System-wide hotkey that shows or hides the FlowBoard window.</summary>
    [ObservableProperty] private string _showHideHotkey = "Ctrl+Alt+F";
    [ObservableProperty] private bool _cardHoverHotkeys = true;
}
