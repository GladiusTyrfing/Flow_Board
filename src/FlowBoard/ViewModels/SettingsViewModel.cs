using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;

namespace FlowBoard.ViewModels;

public sealed partial class SettingsViewModel : DialogViewModel
{
    public override string EnterAnimation => "pop";

    public SettingsViewModel(MainViewModel main) => Main = main;

    public MainViewModel Main { get; }
    public AppSettings Settings => Main.Settings;
    public IReadOnlyList<ThemeMode> Themes { get; } = Enum.GetValues<ThemeMode>();
    public IReadOnlyList<BackdropMode> Backdrops { get; } = Enum.GetValues<BackdropMode>();
    public string DataDir => AppPaths.DataDir;
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
        _tab = tab;
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

    [RelayCommand] private void SetTab(string tab) => Tab = tab;

    [RelayCommand] private void SetTheme(string name) => Board.Theme = name;

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
