using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Threading;
using FlowBoard.Models;

namespace FlowBoard.Services;

/// <summary>
/// Loads and saves the workspace as local JSON. Autosave works by serializing periodically and
/// writing only when the content changed, so no change tracking is needed anywhere else.
/// </summary>
public sealed class DataStore
{
    private readonly DispatcherTimer _timer;
    private string _lastWorkspaceJson = string.Empty;
    private string _lastSettingsJson = string.Empty;

    public Workspace Workspace { get; private set; } = new();
    public AppSettings Settings { get; private set; } = new();
    public bool IsFirstRun { get; private set; }
    public string? LoadWarning { get; private set; }

    public event EventHandler? Saved;

    public DataStore()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => SaveIfChanged();
    }

    public void Load()
    {
        Settings = TryRead<AppSettings>(AppPaths.SettingsFile, out _) ?? new AppSettings();

        if (!File.Exists(AppPaths.DataFile))
        {
            IsFirstRun = true;
            Workspace = new Workspace();
        }
        else
        {
            var ws = TryRead<Workspace>(AppPaths.DataFile, out var error);
            if (ws == null)
            {
                // Keep the damaged file and fall back to the newest backup.
                try { File.Copy(AppPaths.DataFile, AppPaths.DataFile + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}", true); } catch { }
                ws = LoadNewestBackup();
                LoadWarning = ws != null
                    ? "Your data file could not be read, so the newest backup was restored. The damaged file was kept next to it."
                    : $"Your data file could not be read ({error}). Starting with an empty workspace; the damaged file was kept.";
                ws ??= new Workspace();
            }

            Workspace = ws;
        }

        Workspace.Hydrate();
        _lastWorkspaceJson = JsonSerializer.Serialize(Workspace, Json.Options);
        _lastSettingsJson = JsonSerializer.Serialize(Settings, Json.Options);
    }

    public void StartAutoSave() => _timer.Start();

    public void SaveIfChanged()
    {
        try
        {
            var ws = JsonSerializer.Serialize(Workspace, Json.Options);
            if (ws != _lastWorkspaceJson)
            {
                WriteAtomic(AppPaths.DataFile, ws);
                _lastWorkspaceJson = ws;
                Saved?.Invoke(this, EventArgs.Empty);
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

    public void CreateBackupIfDue()
    {
        if (!File.Exists(AppPaths.DataFile)) return;
        if (DateTime.Now - Settings.LastBackup < TimeSpan.FromHours(12)) return;
        try
        {
            var target = Path.Combine(AppPaths.BackupsDir, $"data-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(AppPaths.DataFile, target, true);
            Settings.LastBackup = DateTime.Now;
            foreach (var old in Directory.GetFiles(AppPaths.BackupsDir, "data-*.json").OrderByDescending(f => f).Skip(20))
                File.Delete(old);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Backup failed: {ex}");
        }
    }

    /// <summary>Writes a full backup (data, settings, attachments, backgrounds) to a zip file.</summary>
    public void ExportZip(string zipPath)
    {
        SaveIfChanged();
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        void AddFile(string full)
        {
            var rel = Path.GetRelativePath(AppPaths.DataDir, full).Replace('\\', '/');
            zip.CreateEntryFromFile(full, rel, CompressionLevel.Optimal);
        }

        if (File.Exists(AppPaths.DataFile)) AddFile(AppPaths.DataFile);
        if (File.Exists(AppPaths.SettingsFile)) AddFile(AppPaths.SettingsFile);
        foreach (var dir in new[] { AppPaths.AttachmentsDir, AppPaths.BackgroundsDir })
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                AddFile(f);
    }

    /// <summary>Validates a backup zip and restores it over the current data. The app must restart afterwards.</summary>
    public void ImportZip(string zipPath)
    {
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var entry = zip.GetEntry("data.json") ?? throw new InvalidDataException("This zip does not contain a FlowBoard data.json.");
            using var s = entry.Open();
            _ = JsonSerializer.Deserialize<Workspace>(s, Json.Options) ?? throw new InvalidDataException("data.json in the backup is empty.");
        }

        _timer.Stop();
        // Safety copy of the current state first.
        ExportZip(Path.Combine(AppPaths.BackupsDir, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.zip"));
        ZipFile.ExtractToDirectory(zipPath, AppPaths.DataDir, overwriteFiles: true);
    }

    private static Workspace? LoadNewestBackup()
    {
        foreach (var f in Directory.GetFiles(AppPaths.BackupsDir, "data-*.json").OrderByDescending(f => f))
        {
            var ws = TryRead<Workspace>(f, out _);
            if (ws != null) return ws;
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
