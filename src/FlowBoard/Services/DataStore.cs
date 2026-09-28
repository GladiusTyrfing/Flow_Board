using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Threading;
using FlowBoard.Models;

namespace FlowBoard.Services;

/// <summary>
/// Loads and saves projects as local JSON. Autosave works by serializing periodically and
/// writing only when the content changed, so no change tracking is needed anywhere else.
/// The <see cref="Workspace"/> instance never changes: opening a project swaps its contents,
/// so everything bound to its collections keeps working.
/// </summary>
public sealed class DataStore
{
    private readonly DispatcherTimer _timer;
    private string _lastWorkspaceJson = string.Empty;
    private string _lastSettingsJson = string.Empty;

    public Workspace Workspace { get; } = new();
    public AppSettings Settings { get; private set; } = new();
    public string? LoadWarning { get; private set; }

    public bool HasProject => AppPaths.ProjectFile != null;
    public string? ProjectFile => AppPaths.ProjectFile;
    public string ProjectName => ProjectFile == null ? "No project" : AppPaths.ProjectNameOf(ProjectFile);

    public event EventHandler? Saved;
    public event EventHandler? ProjectChanged;

    public DataStore()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => SaveIfChanged();
    }

    /// <summary>Reads app settings. No project is opened: the app always starts on the home screen.</summary>
    public void Load()
    {
        Settings = TryRead<AppSettings>(AppPaths.SettingsFile, out _) ?? new AppSettings();
        Settings.SyncDisplayName(Environment.UserName);
        AppPaths.CustomProjectsDir = Settings.ProjectsFolder;
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.ProjectsFolder)) AppPaths.CustomProjectsDir = Settings.ProjectsFolder;
        };

        // Data from before projects existed shows up once in the recent list as "My workspace".
        if (!Settings.LegacyWorkspaceListed)
        {
            Settings.LegacyWorkspaceListed = true;
            if (File.Exists(AppPaths.LegacyDataFile) && !Settings.RecentProjects.Contains(AppPaths.LegacyDataFile, StringComparer.OrdinalIgnoreCase))
                Settings.RecentProjects.Insert(0, AppPaths.LegacyDataFile);
        }

        _lastWorkspaceJson = JsonSerializer.Serialize(Workspace, Json.Options);
        _lastSettingsJson = JsonSerializer.Serialize(Settings, Json.Options);
    }

    public void StartAutoSave() => _timer.Start();

    // ---------- projects ----------

    /// <summary>Opens a project file, saving the current one first. Returns an error message on failure.</summary>
    public string? OpenProject(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) return "The project file no longer exists.";
        Workspace? ws = null;
        string? error = null;
        try
        {
            ws = ProjectPackage.Read(path);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        string? warning = null;
        if (ws == null)
        {
            // Keep the damaged file and fall back to the newest backup.
            var backup = LoadNewestBackup(Path.Combine(ProjectPackage.MediaDirFor(path, null), "backups"))
                         ?? LoadNewestBackup(Path.Combine(Path.GetDirectoryName(path)!, "backups"));
            if (backup == null) return $"The project could not be read ({error}).";
            try { File.Copy(path, path + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}", true); } catch { }
            ws = backup;
            warning = "The project file could not be read, so its newest backup was opened. The damaged file was kept next to it.";
        }

        SaveIfChanged();
        AppPaths.SetProject(path, ProjectPackage.MediaDirFor(path, ws.MediaFolder));
        Replace(ws);
        LoadWarning = warning;
        AddRecent(path);
        CreateBackupIfDue();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>
    /// Creates a project at <paramref name="file"/> (any folder) with its "&lt;name&gt; files" folder beside it, and opens it.
    /// </summary>
    public void CreateProject(string file, Workspace initial)
    {
        file = WithExtension(file);
        var media = Path.Combine(Path.GetDirectoryName(file)!, ProjectPackage.MediaFolderName(file));
        Directory.CreateDirectory(media);
        initial.MediaFolder = Path.GetFileName(media);
        ProjectPackage.Write(file, JsonSerializer.Serialize(initial, Json.Options));
        var error = OpenProject(file);
        if (error != null) throw new IOException(error);
    }

    /// <summary>
    /// Saves the open project under a new name / place (with copies of its pictures, recordings and wallpapers)
    /// and continues working in the new file.
    /// </summary>
    public void SaveProjectAs(string file)
    {
        if (!HasProject) throw new InvalidOperationException("No project is open.");
        file = WithExtension(Path.GetFullPath(file));
        if (string.Equals(file, ProjectFile, StringComparison.OrdinalIgnoreCase))
        {
            SaveIfChanged();
            return;
        }

        SaveIfChanged();
        var fromDir = AppPaths.DataDir;
        var media = Path.Combine(Path.GetDirectoryName(file)!, ProjectPackage.MediaFolderName(file));
        Directory.CreateDirectory(media);
        foreach (var sub in new[] { "attachments", "backgrounds" })
        {
            var src = Path.Combine(fromDir, sub);
            if (!Directory.Exists(src)) continue;
            foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(media, sub, Path.GetRelativePath(src, f));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(f, target, true);
            }
        }

        var previous = Workspace.MediaFolder;
        Workspace.MediaFolder = Path.GetFileName(media);
        var json = JsonSerializer.Serialize(Workspace, Json.Options);
        Workspace.MediaFolder = previous; // the old file keeps pointing at its own folder
        _lastWorkspaceJson = JsonSerializer.Serialize(Workspace, Json.Options);
        ProjectPackage.Write(file, json);
        var error = OpenProject(file);
        if (error != null) throw new IOException(error);
    }

    private static string WithExtension(string file) =>
        string.Equals(Path.GetExtension(file), AppPaths.ProjectExtension, StringComparison.OrdinalIgnoreCase) ? file : file + AppPaths.ProjectExtension;

    /// <summary>True for the pre-projects data.json (plain JSON in the app folder).</summary>
    public bool IsLegacyWorkspace => ProjectFile != null && !string.Equals(Path.GetExtension(ProjectFile), AppPaths.ProjectExtension, StringComparison.OrdinalIgnoreCase);

    public void CloseProject()
    {
        if (!HasProject) return;
        SaveIfChanged();
        AppPaths.SetProject(null);
        Replace(new Workspace());
        LoadWarning = null;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveRecent(string path)
    {
        Settings.RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
    }

    private void AddRecent(string path)
    {
        RemoveRecent(path);
        Settings.RecentProjects.Insert(0, path);
        if (Settings.RecentProjects.Count > 15) Settings.RecentProjects.RemoveRange(15, Settings.RecentProjects.Count - 15);
    }

    /// <summary>Moves another workspace's contents into ours, keeping the same collection instances.</summary>
    private void Replace(Workspace from)
    {
        from.Hydrate();
        Workspace.Version = from.Version;
        Workspace.MediaFolder = from.MediaFolder;
        Refill(Workspace.Boards, from.Boards);
        Refill(Workspace.UserTemplates, from.UserTemplates);
        Refill(Workspace.Storyboards, from.Storyboards);
        Refill(Workspace.Canvases, from.Canvases);
        Refill(Workspace.Notes, from.Notes);
        _lastWorkspaceJson = JsonSerializer.Serialize(Workspace, Json.Options);
    }

    private static void Refill<T>(System.Collections.ObjectModel.ObservableCollection<T> target, IEnumerable<T> items)
    {
        var list = items.ToList();
        target.Clear();
        foreach (var i in list) target.Add(i);
    }

    // ---------- saving ----------

    public void SaveIfChanged()
    {
        try
        {
            if (HasProject)
            {
                var ws = JsonSerializer.Serialize(Workspace, Json.Options);
                if (ws != _lastWorkspaceJson)
                {
                    ProjectPackage.Write(AppPaths.DataFile, ws);
                    _lastWorkspaceJson = ws;
                    Saved?.Invoke(this, EventArgs.Empty);
                }
            }

            var st = JsonSerializer.Serialize(Settings, Json.Options);
            if (st != _lastSettingsJson)
            {
                WriteAtomic(AppPaths.SettingsFile, st);
                _lastSettingsJson = st;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Save failed: {ex}");
        }
    }

    /// <summary>Keeps up to 20 automatic copies of the project file in its backups folder, at most one every 12 hours.</summary>
    public void CreateBackupIfDue()
    {
        if (!HasProject || !File.Exists(AppPaths.DataFile)) return;
        try
        {
            Directory.CreateDirectory(AppPaths.BackupsDir);
            var existing = BackupFiles(AppPaths.BackupsDir).ToList();
            if (existing.Count > 0 && DateTime.Now - File.GetLastWriteTime(existing[0]) < TimeSpan.FromHours(12)) return;
            var target = Path.Combine(AppPaths.BackupsDir, $"backup-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(AppPaths.DataFile)}");
            File.Copy(AppPaths.DataFile, target, true);
            foreach (var old in existing.Skip(19)) File.Delete(old);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Backup failed: {ex}");
        }
    }

    /// <summary>Writes a full backup of the open project (data, attachments, wallpapers) to a zip file.</summary>
    public void ExportZip(string zipPath)
    {
        if (!HasProject) throw new InvalidOperationException("Open a project first.");
        SaveIfChanged();
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        void AddFile(string full)
        {
            var rel = Path.GetRelativePath(AppPaths.DataDir, full).Replace('\\', '/');
            zip.CreateEntryFromFile(full, rel, CompressionLevel.Optimal);
        }

        zip.CreateEntryFromFile(AppPaths.DataFile, Path.GetFileName(AppPaths.DataFile), CompressionLevel.Optimal);
        foreach (var dir in new[] { AppPaths.AttachmentsDir, AppPaths.BackgroundsDir })
            if (Directory.Exists(dir))
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    AddFile(f);
    }

    /// <summary>
    /// Unpacks a backup zip next to the other projects (never over existing data) and returns the project file.
    /// </summary>
    public string ImportZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e => !e.FullName.Contains('/') && e.FullName.EndsWith(AppPaths.ProjectExtension, StringComparison.OrdinalIgnoreCase))
                    ?? zip.GetEntry("data.json")
                    ?? throw new InvalidDataException("This zip does not contain a FlowBoard project.");

        var name = AppPaths.SafeName(Path.GetFileNameWithoutExtension(zipPath));
        var parent = AppPaths.DefaultProjectsDir;
        var file = Path.Combine(parent, name + AppPaths.ProjectExtension);
        int i = 2;
        while (File.Exists(file) || Directory.Exists(Path.Combine(parent, ProjectPackage.MediaFolderName(file))))
            file = Path.Combine(parent, $"{name} ({i++}){AppPaths.ProjectExtension}");
        var media = Path.Combine(parent, ProjectPackage.MediaFolderName(file));
        Directory.CreateDirectory(media);

        try
        {
            foreach (var e in zip.Entries)
            {
                // Old full-app backups also carried settings.json: app settings are left alone.
                if (e.FullName.EndsWith('/') || e.FullName == "settings.json") continue;
                var target = e == entry ? file : Path.GetFullPath(Path.Combine(media, e.FullName));
                if (e != entry && !target.StartsWith(media + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue; // no path escapes
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, true);
            }

            // Validate, and point the project at its media folder (this also turns an old data.json into a package).
            var ws = ProjectPackage.Read(file);
            ws.MediaFolder = Path.GetFileName(media);
            ProjectPackage.Write(file, JsonSerializer.Serialize(ws, Json.Options));
            return file;
        }
        catch
        {
            try { File.Delete(file); Directory.Delete(media, true); } catch { }
            throw;
        }
    }

    /// <summary>Automatic backups, newest first (current and older naming).</summary>
    private static IEnumerable<string> BackupFiles(string dir) =>
        Directory.Exists(dir)
            ? Directory.GetFiles(dir, "backup-*").Concat(Directory.GetFiles(dir, "data-*.json"))
                .Where(f => !f.EndsWith(".tmp")).OrderByDescending(File.GetLastWriteTime)
            : [];

    private static Workspace? LoadNewestBackup(string backupsDir)
    {
        foreach (var f in BackupFiles(backupsDir))
        {
            try
            {
                return ProjectPackage.Read(f);
            }
            catch
            {
                // Try the next one.
            }
        }

        return null;
    }

    private static T? TryRead<T>(string path, out string? error) where T : class
    {
        error = null;
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json.Options);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
