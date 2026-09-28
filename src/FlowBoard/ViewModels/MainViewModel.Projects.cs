using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;
using Microsoft.Win32;

namespace FlowBoard.ViewModels;

/// <summary>A project in the recent list on the home screen.</summary>
public sealed record RecentProject(string Path, string Name, string Folder, string Edited, bool Exists, bool IsOpen);

/// <summary>Project files: new, open, recent, close, save a copy. The app always starts with no project open.</summary>
public sealed partial class MainViewModel
{
    public bool HasProject => _store.HasProject;
    public string ProjectName => _store.ProjectName;
    public string? ProjectPath => _store.ProjectFile;

    public IReadOnlyList<RecentProject> RecentProjects =>
        Settings.RecentProjects.Select(p =>
        {
            var exists = File.Exists(p);
            var edited = exists ? Ago(File.GetLastWriteTime(p)) : "missing";
            return new RecentProject(p, AppPaths.ProjectNameOf(p), Path.GetDirectoryName(p) ?? string.Empty, edited, exists,
                string.Equals(p, _store.ProjectFile, StringComparison.OrdinalIgnoreCase));
        }).ToList();

    public bool HasRecentProjects => Settings.RecentProjects.Count > 0;

    public string WindowTitle => HasProject ? $"{ProjectName} — FlowBoard" : "FlowBoard";

    private static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h ago";
        if (d.TotalDays < 2) return "yesterday";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays} days ago";
        return t.ToString("d MMM yyyy");
    }

    /// <summary>Guard for anything that creates content. Returns false (and says why) with no project open.</summary>
    public bool EnsureProject()
    {
        if (HasProject) return true;
        ShowToast("Create or open a project first", "New project", () => NewProjectCommand.Execute(null));
        OpenDashboard();
        return false;
    }

    /// <summary>Finishes everything that belongs to the current project before its contents are swapped out.</summary>
    private void BeforeProjectSwitch()
    {
        CloseAllDialogs();
        Timer.Stop();
        Player.Stop();
        if (Recorder.IsRecording) _ = Recorder.CancelAsync();
        if (Pomodoro.LinkedCardId != null) Pomodoro.LinkCard(null);
        IsFilterOpen = false;
        ActiveDocument = null;
        ActiveView = ActiveView.Board;
        _store.SaveIfChanged();
    }

    private void AfterProjectSwitch()
    {
        Undo.Clear();
        Filter.Clear();
        CurrentBoard = null;
        Filter.SetBoard(null);
        RefreshViews();
        RefreshSidebar();
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProjectPath));
        OnPropertyChanged(nameof(RecentProjects));
        OnPropertyChanged(nameof(HasRecentProjects));
        OnPropertyChanged(nameof(WindowTitle));
        OpenDashboard();
        ShowStartupWarning();
    }

    private void RunProjectSwitch(Func<string?> action, string? toast)
    {
        BeforeProjectSwitch();
        string? error;
        try
        {
            error = action();
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        AfterProjectSwitch();
        if (error != null) ShowToast(error, isError: true);
        else if (toast != null) ShowToast(toast);
    }

    // ---------- commands ----------

    [RelayCommand]
    private async Task NewProject() => await CreateProjectIn(AppPaths.DefaultProjectsDir, sample: false);

    [RelayCommand]
    private async Task NewSampleProject() => await CreateProjectIn(AppPaths.DefaultProjectsDir, sample: true);

    [RelayCommand]
    private async Task NewProjectIn()
    {
        var dlg = new OpenFolderDialog { Title = "Choose where the new project folder goes", InitialDirectory = AppPaths.DefaultProjectsDir };
        Directory.CreateDirectory(AppPaths.DefaultProjectsDir);
        if (dlg.ShowDialog() != true) return;
        await CreateProjectIn(dlg.FolderName, sample: false);
    }

    private async Task CreateProjectIn(string parent, bool sample)
    {
        var name = await PromptAsync(
            sample ? "New sample project" : "New project",
            $"A folder with this name is created in {parent}. Boards, storyboards, canvases, pages and their files all live inside it.",
            "Project name", "Create project", sample ? "Sample project" : "My project");
        if (name == null) return;
        var ws = new Workspace();
        if (sample) ws.Boards.Add(TemplateService.CreateWelcomeBoard(Settings.DisplayName));
        RunProjectSwitch(() =>
        {
            Directory.CreateDirectory(parent);
            _store.CreateProject(parent, name, ws);
            return null;
        }, $"Project \"{_store.ProjectName}\" created");
        if (sample && Workspace.Boards.FirstOrDefault() is { } b) SelectBoard(b);
    }

    [RelayCommand]
    private void OpenProject()
    {
        Directory.CreateDirectory(AppPaths.DefaultProjectsDir);
        var dlg = new OpenFileDialog
        {
            Title = "Open project",
            Filter = $"FlowBoard project|*{AppPaths.ProjectExtension};data.json|All files|*.*",
            InitialDirectory = ProjectPath != null ? Path.GetDirectoryName(Path.GetDirectoryName(ProjectPath)) : AppPaths.DefaultProjectsDir,
        };
        if (dlg.ShowDialog() != true) return;
        OpenProjectFile(dlg.FileName);
    }

    public void OpenProjectFile(string path)
    {
        if (string.Equals(path, ProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            OpenDashboard();
            return;
        }

        RunProjectSwitch(() => _store.OpenProject(path), null);
    }

    [RelayCommand]
    private void OpenRecentProject(RecentProject? p)
    {
        if (p == null) return;
        if (!p.Exists)
        {
            ShowToast($"\"{p.Name}\" was moved or deleted", "Remove from list", () => RemoveRecentProject(p));
            return;
        }

        OpenProjectFile(p.Path);
    }

    [RelayCommand]
    private void RemoveRecentProject(RecentProject? p)
    {
        if (p == null) return;
        _store.RemoveRecent(p.Path);
        OnPropertyChanged(nameof(RecentProjects));
        OnPropertyChanged(nameof(HasRecentProjects));
        (ActiveDocument as DashboardViewModel)?.Refresh();
    }

    [RelayCommand]
    private void CloseProject()
    {
        if (!HasProject) return;
        RunProjectSwitch(() =>
        {
            _store.CloseProject();
            return null;
        }, null);
    }

    [RelayCommand]
    private async Task SaveProjectCopy()
    {
        if (!HasProject) return;
        var name = await PromptAsync("Save a copy as", $"The project and all its files are copied to a new folder in {AppPaths.DefaultProjectsDir}, and the copy is opened.",
            "Project name", "Save copy", ProjectName + " copy");
        if (name == null) return;
        RunProjectSwitch(() =>
        {
            Directory.CreateDirectory(AppPaths.DefaultProjectsDir);
            _store.SaveCopyAs(AppPaths.DefaultProjectsDir, name);
            return null;
        }, "Copy saved and opened");
    }

    [RelayCommand]
    private void ShowProjectInExplorer(RecentProject? p)
    {
        var path = p?.Path ?? ProjectPath;
        if (path == null) return;
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir)) Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, isError: true);
        }
    }
}
