using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;

namespace FlowBoard.ViewModels;

/// <summary>Theme and wallpaper for a storyboard, canvas or page (a drawer; the document previews live behind it).</summary>
public sealed partial class DocStyleViewModel : DialogViewModel
{
    private readonly MainViewModel _main;

    public DocStyleViewModel(MainViewModel main, StyledDocument doc, string name)
    {
        _main = main;
        Doc = doc;
        Name = name;
    }

    public override double BackdropOpacity => 0.12;
    public override string EnterAnimation => "slide";

    public StyledDocument Doc { get; }
    public string Name { get; }
    public IReadOnlyList<BoardThemePreset> Presets => BoardThemeService.Presets;
    public IReadOnlyList<string> Gradients => Board.GradientPresets;
    public IReadOnlyList<string> Solids => Board.SolidPresets;

    public IEnumerable<string> RecentImages =>
        System.IO.Directory.Exists(AppPaths.BackgroundsDir)
            ? System.IO.Directory.GetFiles(AppPaths.BackgroundsDir)
                .OrderByDescending(System.IO.File.GetCreationTime)
                .Take(12)
                .Select(f => "image:" + AppPaths.ToRelative(f))
            : [];

    [RelayCommand] private void SetTheme(string name) => Doc.Theme = name;
    [RelayCommand] private void SetBackground(string spec) => Doc.Background = spec;

    [RelayCommand]
    private void ChooseImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Choose a wallpaper", Filter = MediaStore.ImageFilter };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var dest = AppPaths.UniquePath(AppPaths.BackgroundsDir, System.IO.Path.GetFileName(dlg.FileName));
            System.IO.File.Copy(dlg.FileName, dest);
            Doc.Background = "image:" + AppPaths.ToRelative(dest);
            OnPropertyChanged(nameof(RecentImages));
        }
        catch (Exception ex)
        {
            _main.ShowToast($"Couldn't use that image: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void Reset()
    {
        Doc.Theme = "Auto";
        Doc.Background = "none";
        Doc.BackgroundDim = 0.2;
        Doc.BackgroundBlur = 0;
        Doc.PanelOpacity = 0.9;
    }

    /// <summary>Use this look for every storyboard, canvas and page.</summary>
    [RelayCommand]
    private void ApplyToAll()
    {
        foreach (var d in _main.Workspace.Storyboards.Cast<StyledDocument>().Concat(_main.Workspace.Canvases).Concat(_main.Workspace.Notes))
        {
            if (d == Doc) continue;
            d.Theme = Doc.Theme;
            d.Background = Doc.Background;
            d.BackgroundDim = Doc.BackgroundDim;
            d.BackgroundBlur = Doc.BackgroundBlur;
            d.PanelOpacity = Doc.PanelOpacity;
        }

        _main.ShowToast("Style applied to every storyboard, canvas and page");
    }
}
