using CommunityToolkit.Mvvm.ComponentModel;

namespace FlowBoard.Models;

public partial class AppSettings : ObservableObject
{
    [ObservableProperty] private ThemeMode _theme = ThemeMode.Dark;
    [ObservableProperty] private BackdropMode _backdrop = BackdropMode.Mica;
    [ObservableProperty] private string? _accentColor;
    [ObservableProperty] private bool _animations = true;
    [ObservableProperty] private bool _showMinimap = true;
    [ObservableProperty] private string _accentPreset = "Aurora";
    [ObservableProperty] private bool _auroraBackground = true;
    [ObservableProperty] private double _cornerRadius = 8;
    [ObservableProperty] private string _displayName = Environment.UserName;
    [ObservableProperty] private bool _minimizeToTray = true;
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
    [ObservableProperty] private string _quickAddHotkey = "Ctrl+Alt+Space";
    [ObservableProperty] private string _showHideHotkey = "Ctrl+Alt+F";
    [ObservableProperty] private bool _cardHoverHotkeys = true;
    public string? LastSystemUserName { get; set; }
    [ObservableProperty] private string? _projectsFolder;
    public List<string> RecentProjects { get; set; } = [];
    public bool LegacyWorkspaceListed { get; set; }

    public void SyncDisplayName(string systemName)
    {
        var followsWindows = string.IsNullOrWhiteSpace(DisplayName) || LastSystemUserName == null || DisplayName == LastSystemUserName;
        if (followsWindows) DisplayName = systemName;
        if (followsWindows) LastSystemUserName = systemName;
    }
}
