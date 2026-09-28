using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;

namespace FlowBoard.ViewModels;

public sealed partial class SettingsViewModel : DialogViewModel
{
    public override string EnterAnimation => "pop";

    // Barely dim the app on the Board style page so changes are visible behind the dialog.
    public override double BackdropOpacity => Section == "boardstyle" ? 0.12 : 1;

    partial void OnSectionChanged(string value) => OnPropertyChanged(nameof(BackdropOpacity));

    public SettingsViewModel(MainViewModel main)
    {
        Main = main;
        if (main.CurrentBoard != null)
        {
            BoardStyle = new BoardSettingsViewModel(main, main.CurrentBoard, "style");
            main.CurrentBoard.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Models.Board.CornerRadius)) OnPropertyChanged(nameof(BoardOverridesRadius));
            };
        }
    }

    /// <summary>True when the open board has its own roundness, so the app slider doesn't change it.</summary>
    public bool BoardOverridesRadius => Main.CurrentBoard is { CornerRadius: >= 0 };

    [RelayCommand]
    private void FollowAppRadius()
    {
        if (Main.CurrentBoard != null) Main.CurrentBoard.CornerRadius = -1;
    }

    /// <summary>Style editor for the board that is open (null when no board is open).</summary>
    public BoardSettingsViewModel? BoardStyle { get; }
    public bool HasBoard => BoardStyle != null;

    public override void OnClosed() => BoardStyle?.OnClosed();

    public MainViewModel Main { get; }
    public AppSettings Settings => Main.Settings;
    public IReadOnlyList<ThemeMode> Themes { get; } = Enum.GetValues<ThemeMode>();
    public IReadOnlyList<BackdropMode> Backdrops { get; } = Enum.GetValues<BackdropMode>();
    public string AppDir => AppPaths.AppDir;
    public string ProjectsDir => AppPaths.DefaultProjectsDir;
    public bool HasCustomProjectsDir => !string.IsNullOrWhiteSpace(Settings.ProjectsFolder);
    public string WindowsUserName => Environment.UserName;
    public string Copyright => $"© {Math.Max(2026, DateTime.Now.Year)} Gladius_Tyrfing. All rights reserved.";

    [RelayCommand]
    private void BrowseProjectsFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where new projects are saved", InitialDirectory = ProjectsDir };
        System.IO.Directory.CreateDirectory(ProjectsDir);
        if (dlg.ShowDialog() != true) return;
        Settings.ProjectsFolder = dlg.FolderName;
        OnPropertyChanged(nameof(ProjectsDir));
        OnPropertyChanged(nameof(HasCustomProjectsDir));
    }

    [RelayCommand]
    private void ResetProjectsFolder()
    {
        Settings.ProjectsFolder = null;
        OnPropertyChanged(nameof(ProjectsDir));
        OnPropertyChanged(nameof(HasCustomProjectsDir));
    }

    [RelayCommand]
    private void OpenProjectsFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(ProjectsDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ProjectsDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Main.ShowToast(ex.Message, isError: true);
        }
    }

    /// <summary>Back to following the Windows user name.</summary>
    [RelayCommand]
    private void UseWindowsName()
    {
        Settings.LastSystemUserName = null;
        Settings.SyncDisplayName(Environment.UserName);
    }
    public IReadOnlyList<AccentPreset> Accents => ThemeService.AccentPresets;

    /// <summary>Which settings page is shown (appearance, general, hotkeys, reminders, focus, data).</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string _section = "appearance";

    [RelayCommand] private void SetAccentPreset(string name) => Settings.AccentPreset = name;
    public bool SupportsBackdrop => ThemeService.SupportsBackdrop;
    public bool IsPortable => AppPaths.IsPortable;
    public string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public IReadOnlyList<string> AccentColors { get; } =
    [
        "#0C66E4", "#579DFF", "#6E5DC6", "#9F6FEF", "#E774BB", "#C9372C",
        "#F87168", "#FEA362", "#F5CD47", "#94C748", "#1F845A", "#4BCE97", "#227D9B", "#6CC3E0",
    ];

    public IReadOnlyList<int> ReminderDefaults { get; } = [-1, 0, 5, 15, 30, 60, 1440];

    [RelayCommand]
    private void SetAccent(string? color) => Settings.AccentColor = color;
}

public sealed partial class BoardSettingsViewModel : DialogViewModel
{
    private readonly MainViewModel _main;

    public BoardSettingsViewModel(MainViewModel main, Board board, string tab)
    {
        _main = main;
        Board = board;
        _tab = tab == "background" ? "style" : tab; // background now lives on the Style tab
        _snapshot = Json.SerializeBoard(board);
        _index = main.Workspace.Boards.IndexOf(board);
    }

    private readonly string _snapshot;
    private readonly int _index;

    // Drawer on the right without dimming, so style changes preview live on the board.
    public override double BackdropOpacity => 0;
    public override string EnterAnimation => "slide";

    public Board Board { get; }
    public IReadOnlyList<string> Gradients => Board.GradientPresets;
    public IReadOnlyList<string> Solids => Board.SolidPresets;
    public IReadOnlyList<string> LabelPalette => Label.Palette;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string _tab;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private Label? _editingLabel;

    /// <summary>Background images previously added to any board.</summary>
    public IEnumerable<string> RecentImages =>
        System.IO.Directory.Exists(AppPaths.BackgroundsDir)
            ? System.IO.Directory.GetFiles(AppPaths.BackgroundsDir)
                .OrderByDescending(System.IO.File.GetCreationTime)
                .Take(12)
                .Select(f => "image:" + AppPaths.ToRelative(f))
            : [];

    public IReadOnlyList<BoardThemePreset> Presets => BoardThemeService.Presets;

    /// <summary>When on, this board uses the app-wide corner roundness.</summary>
    public bool UseAppRadius
    {
        get => Board.CornerRadius < 0;
        set
        {
            Board.CornerRadius = value ? -1 : _main.Settings.CornerRadius;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BoardRadius));
        }
    }

    public double BoardRadius
    {
        get => Board.CornerRadius < 0 ? _main.Settings.CornerRadius : Board.CornerRadius;
        set
        {
            Board.CornerRadius = Math.Round(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseAppRadius));
        }
    }

    [RelayCommand] private void SetTab(string tab) => Tab = tab;

    [RelayCommand] private void SetTheme(string name) => Board.Theme = name;

    /// <summary>Copies this board's look (theme, transparency, corners, blur, dim) to every board.</summary>
    [RelayCommand]
    private void ApplyStyleToAllBoards()
    {
        foreach (var b in _main.Workspace.Boards.Where(b => b != Board))
        {
            _main.Undo.Checkpoint(_main.Workspace, b, "Apply style to all boards");
            b.Theme = Board.Theme;
            b.ListOpacity = Board.ListOpacity;
            b.CardOpacity = Board.CardOpacity;
            b.CornerRadius = Board.CornerRadius;
            b.BackgroundBlur = Board.BackgroundBlur;
            b.BackgroundDim = Board.BackgroundDim;
        }

        _main.ShowToast("Style applied to all boards");
    }

    /// <summary>One-click looks combining transparency and blur.</summary>
    [RelayCommand]
    private void ApplyLook(string look)
    {
        (Board.ListOpacity, Board.CardOpacity, Board.BackgroundBlur) = look switch
        {
            "glass" => (0.55, 0.82, 14.0),
            "clear" => (0.0, 0.72, 0.0),
            "solid" => (1.0, 1.0, 0.0),
            _ => (0.82, 0.96, 0.0),
        };
    }

    [RelayCommand] private void SetBackground(string spec) => Board.Background = spec;

    [RelayCommand]
    private void ChooseImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a background image",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var dest = AppPaths.UniquePath(AppPaths.BackgroundsDir, System.IO.Path.GetFileName(dlg.FileName));
            System.IO.File.Copy(dlg.FileName, dest);
            Board.Background = "image:" + AppPaths.ToRelative(dest);
            OnPropertyChanged(nameof(RecentImages));
        }
        catch (Exception ex)
        {
            _main.ShowToast($"Couldn't use that image: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void AddLabel()
    {
        var used = Board.Labels.Select(l => l.Color).ToHashSet();
        var color = Label.Palette.FirstOrDefault(c => !used.Contains(c)) ?? Label.Palette[0];
        var label = new Label { Name = "New label", Color = color };
        Board.Labels.Add(label);
        EditingLabel = label;
        _main.Filter.SetBoard(Board);
    }

    [RelayCommand] private void EditLabel(Label l) => EditingLabel = EditingLabel == l ? null : l;

    [RelayCommand]
    private void SetLabelColor(string color)
    {
        if (EditingLabel == null) return;
        EditingLabel.Color = color;
        Board.NotifyLabelsChanged();
    }

    [RelayCommand]
    private void DeleteLabel(Label l)
    {
        Board.Labels.Remove(l);
        foreach (var c in Board.AllActiveCards.Concat(Board.ArchivedCards)) c.LabelIds.Remove(l.Id);
        if (EditingLabel == l) EditingLabel = null;
        _main.Filter.SetBoard(Board);
    }

    public override void OnClosed()
    {
        Board.NotifyLabelsChanged();
        _main.Filter.SetBoard(Board);
        _main.RefreshSidebar();
        if (_main.Workspace.Boards.Contains(Board) && Json.SerializeBoard(Board) != _snapshot)
            _main.Undo.PushSnapshot(Board.Id, _index, _snapshot, "Edit board settings");
    }
}
