using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using FlowBoard.Models;

namespace FlowBoard.Services;

public sealed record ExternalChange(string? Editor, int Conflicts, IReadOnlyCollection<object> ChangedObjects, IReadOnlySet<Guid> ChangedItems, bool FromConflictCopy);

public sealed partial class DataStore
{
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private string _baseRaw = string.Empty;
    private (DateTime Time, long Length) _diskStamp;
    private bool _merging;
    private int _ticks;
    private string? _presenceFile;

    public event EventHandler<ExternalChange>? ExternalChangeMerged;

    public event EventHandler? CollaboratorsChanged;

    public IReadOnlyList<string> Collaborators { get; private set; } = [];

    private void InitSync()
    {
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            CheckForExternalChanges();
            MergeConflictCopies();
            ReadPresence();
        };
    }

    private void StartSync(string file)
    {
        RememberDisk(ReadRawOrEmpty(file));
        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(file)!)
            {
                Filter = "*" + AppPaths.ProjectExtension,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler poke = (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _debounce?.Stop();
                _debounce?.Start();
            });
            _watcher.Changed += poke;
            _watcher.Created += poke;
            _watcher.Renamed += (s, e) => poke(s, e);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Watcher failed: {ex.Message}");
            _watcher = null;
        }

        WritePresence();
        ReadPresence();
    }

    private void StopSync()
    {
        _watcher?.Dispose();
        _watcher = null;
        _debounce?.Stop();
        RemovePresence();
        _baseRaw = string.Empty;
        _diskStamp = default;
        if (Collaborators.Count > 0)
        {
            Collaborators = [];
            CollaboratorsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void LeaveProject() => RemovePresence();

    private static string ReadRawOrEmpty(string file)
    {
        try
        {
            return File.Exists(file) ? ProjectPackage.ReadJson(file) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static (DateTime, long) Stamp(string file)
    {
        var fi = new FileInfo(file);
        return fi.Exists ? (fi.LastWriteTimeUtc, fi.Length) : default;
    }

    private void RememberDisk(string raw)
    {
        _baseRaw = raw;
        if (ProjectFile != null) _diskStamp = Stamp(ProjectFile);
    }

    public void CheckForExternalChanges()
    {
        if (!HasProject || _merging || ProjectFile is not { } file) return;
        var stamp = Stamp(file);
        if (stamp == default || stamp == _diskStamp) return;

        string theirs;
        try
        {
            theirs = ProjectPackage.ReadJson(file);
        }
        catch
        {
            return;
        }

        var stampAfter = Stamp(file);
        if (stampAfter != stamp) return;
        _diskStamp = stamp;
        if (theirs == _baseRaw) return;

        MergeIn(theirs, ProjectPackage.ReadSavedBy(file), fromConflictCopy: false);
        _baseRaw = theirs;
    }

    private void MergeIn(string theirsRaw, string? editor, bool fromConflictCopy)
    {
        JsonNode? theirs;
        try
        {
            theirs = JsonNode.Parse(theirsRaw);
        }
        catch
        {
            return;
        }

        if (theirs is not JsonObject) return;
        _merging = true;
        try
        {
            var oursRaw = JsonSerializer.Serialize(Workspace, Json.Options);
            var ours = JsonNode.Parse(oursRaw);
            JsonNode? @base = fromConflictCopy ? null
                : string.IsNullOrEmpty(_baseRaw) ? theirs.DeepClone() : JsonNode.Parse(_baseRaw);
            var stats = new MergeStats();
            if (JsonMerge.Merge(@base, ours, theirs, stats) is not JsonObject merged) return;
            if (fromConflictCopy) stats.Conflicts = 0;

            HashSet<object> changed;
            ModelEvents.Suspended = true;
            try
            {
                changed = ModelReconciler.Apply(Workspace, merged);
                Workspace.Hydrate();
            }
            finally
            {
                ModelEvents.Suspended = false;
            }

            var now = JsonSerializer.Serialize(Workspace, Json.Options);
            if (!fromConflictCopy && JsonNode.DeepEquals(JsonNode.Parse(now), theirs)) _lastWorkspaceJson = now;

            if (changed.Count > 0 || stats.Conflicts > 0)
                ExternalChangeMerged?.Invoke(this, new ExternalChange(editor, stats.Conflicts, changed, ChangedTopLevel(ours, merged), fromConflictCopy));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Merge failed: {ex}");
        }
        finally
        {
            _merging = false;
        }
    }

    public static HashSet<Guid> ChangedTopLevel(JsonNode? before, JsonNode? after)
    {
        static Dictionary<Guid, JsonNode> Items(JsonNode? root, string key)
        {
            var map = new Dictionary<Guid, JsonNode>();
            if (root?[key] is not JsonArray arr) return map;
            foreach (var o in arr.OfType<JsonObject>())
                if (o["Id"] is JsonValue v && v.TryGetValue<Guid>(out var g)) map.TryAdd(g, o);
            return map;
        }

        var result = new HashSet<Guid>();
        foreach (var section in new[] { "Boards", "Storyboards", "Canvases", "Notes" })
        {
            var a = Items(before, section);
            var b = Items(after, section);
            foreach (var id in a.Keys.Union(b.Keys))
                if (!a.TryGetValue(id, out var x) || !b.TryGetValue(id, out var y) || !JsonNode.DeepEquals(x, y)) result.Add(id);
        }

        return result;
    }

    private void MergeConflictCopies()
    {
        if (!HasProject || ProjectFile is not { } file) return;
        try
        {
            var dir = Path.GetDirectoryName(file)!;
            var name = Path.GetFileNameWithoutExtension(file);
            foreach (var copy in Directory.EnumerateFiles(dir, name + "*" + AppPaths.ProjectExtension))
            {
                if (string.Equals(copy, file, StringComparison.OrdinalIgnoreCase)) continue;
                var tail = Path.GetFileNameWithoutExtension(copy)[name.Length..].ToLowerInvariant();
                if (!tail.Contains("conflict")) continue;
                string raw;
                try
                {
                    raw = ProjectPackage.ReadJson(copy);
                }
                catch
                {
                    continue;
                }

                MergeIn(raw, ProjectPackage.ReadSavedBy(copy), fromConflictCopy: true);
                Directory.CreateDirectory(AppPaths.BackupsDir);
                File.Move(copy, Path.Combine(AppPaths.BackupsDir, $"merged-{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileName(copy)}"), true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Conflict copies: {ex.Message}");
        }
    }

    private bool TryLockForSave(out IDisposable? handle)
    {
        handle = null;
        if (ProjectFile is not { } file) return true;
        var lockPath = file + ".lock";
        try
        {
            if (File.Exists(lockPath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath) > TimeSpan.FromSeconds(15))
                File.Delete(lockPath);
            var fs = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            try { File.SetAttributes(lockPath, FileAttributes.Hidden); } catch { }
            handle = fs;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private void SyncTick()
    {
        _ticks++;
        if (_ticks % 5 == 0)
        {
            _diskStamp = default;
            CheckForExternalChanges();
        }

        if (_ticks % 5 == 0) ReadPresence();
        if (_ticks % 8 == 0) MergeConflictCopies();
        if (_ticks % 10 == 0) WritePresence();
    }

    private const int PresenceTimeoutSeconds = 75;

    private sealed record PresenceNote(string Name, string Machine, DateTime Seen);

    private static string PresenceDir => Path.Combine(AppPaths.DataDir, ".presence");

    private void WritePresence()
    {
        if (!HasProject) return;
        try
        {
            var dir = Directory.CreateDirectory(PresenceDir);
            dir.Attributes |= FileAttributes.Hidden;
            _presenceFile ??= Path.Combine(PresenceDir, AppPaths.SafeName($"{Environment.MachineName}-{Environment.UserName}") + ".json");
            File.WriteAllText(_presenceFile, JsonSerializer.Serialize(new PresenceNote(Settings.DisplayName, Environment.MachineName, DateTime.UtcNow)));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Presence: {ex.Message}");
        }
    }

    private void RemovePresence()
    {
        try
        {
            if (_presenceFile != null && File.Exists(_presenceFile)) File.Delete(_presenceFile);
        }
        catch
        {
        }

        _presenceFile = null;
    }

    private void ReadPresence()
    {
        if (!HasProject) return;
        var names = new List<string>();
        try
        {
            if (Directory.Exists(PresenceDir))
            {
                foreach (var f in Directory.EnumerateFiles(PresenceDir, "*.json"))
                {
                    if (_presenceFile != null && string.Equals(f, _presenceFile, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var note = JsonSerializer.Deserialize<PresenceNote>(File.ReadAllText(f));
                        if (note == null) continue;
                        var seen = note.Seen > File.GetLastWriteTimeUtc(f) ? note.Seen : File.GetLastWriteTimeUtc(f);
                        if ((DateTime.UtcNow - seen).TotalSeconds <= PresenceTimeoutSeconds) names.Add(note.Name);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Presence: {ex.Message}");
        }

        names = names.Distinct().OrderBy(n => n).ToList();
        if (names.SequenceEqual(Collaborators)) return;
        Collaborators = names;
        CollaboratorsChanged?.Invoke(this, EventArgs.Empty);
    }
}
