using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using FlowBoard.Models;

namespace FlowBoard.Services;

/// <summary>Changes that came in from someone else's copy of the project.</summary>
/// <param name="ChangedItems">Ids of the boards, storyboards, canvases and pages that changed.</param>
public sealed record ExternalChange(string? Editor, int Conflicts, IReadOnlyCollection<object> ChangedObjects, IReadOnlySet<Guid> ChangedItems, bool FromConflictCopy);

/// <summary>
/// Teamwork over a shared folder (network drive, Syncthing, OneDrive, Dropbox…), no server needed:
/// the project file is watched, and when another FlowBoard saves it, its changes are merged into what's open
/// (item by item, keeping both people's edits) before anything is written back. Conflict copies made by sync tools
/// are merged in too. Each open copy leaves a small "presence" note so everyone can see who else is in the project.
/// </summary>
public sealed partial class DataStore
{
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private string _baseRaw = string.Empty;          // the file's content as we last read or wrote it
    private (DateTime Time, long Length) _diskStamp;  // …and its timestamp/size then
    private bool _merging;
    private int _ticks;
    private string? _presenceFile;

    /// <summary>Someone else's changes were merged into the open project.</summary>
    public event EventHandler<ExternalChange>? ExternalChangeMerged;

    /// <summary>The list of other people who have the project open changed.</summary>
    public event EventHandler? CollaboratorsChanged;

    /// <summary>Other people who have this project open right now.</summary>
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
            // Events arrive on a worker thread and in bursts: check once, shortly after, on the UI thread.
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
            // Some network drives can't be watched: the regular check every couple of seconds still works.
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

    /// <summary>Call when the app exits: others stop seeing us in the project.</summary>
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

    /// <summary>Remembers what is on disk now (after we read or wrote it).</summary>
    private void RememberDisk(string raw)
    {
        _baseRaw = raw;
        if (ProjectFile != null) _diskStamp = Stamp(ProjectFile);
    }

    /// <summary>Merges the project file into the open project if someone else changed it.</summary>
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
            return; // being written right now: try again on the next check
        }

        var stampAfter = Stamp(file);
        if (stampAfter != stamp) return; // changed while reading
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
            // A conflict copy started from some unknown older version, so it's merged as a union:
            // everything from both is kept, nothing is deleted, and where both differ ours stays.
            // Without a known starting point for the main file, our version also wins where both differ.
            JsonNode? @base = fromConflictCopy ? null
                : string.IsNullOrEmpty(_baseRaw) ? theirs.DeepClone() : JsonNode.Parse(_baseRaw);
            var stats = new MergeStats();
            if (JsonMerge.Merge(@base, ours, theirs, stats) is not JsonObject merged) return;
            if (fromConflictCopy) stats.Conflicts = 0; // differences are expected there, not overlapping edits

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

            // When the result is exactly their version there's nothing new to write back.
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

    /// <summary>Boards, storyboards, canvases and pages that differ between two versions of the project.</summary>
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

    /// <summary>
    /// Sync tools keep both versions when two people save at once ("… sync-conflict …", "… conflicted copy …").
    /// Those copies are merged into the project and then moved to the backups folder.
    /// </summary>
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

    /// <summary>
    /// A short-lived "&lt;project&gt;.flowboard.lock" file while saving, so two people on the same network drive
    /// never write at the same moment. A lock older than 15 s is left over from a crash and is ignored.
    /// </summary>
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
            return false; // someone else is saving right now: try again on the next autosave
        }
        catch (UnauthorizedAccessException)
        {
            return true;  // read-only folder or no rights to create files: save without a lock
        }
    }

    /// <summary>Runs with autosave (every 2 s): slower housekeeping for shared projects.</summary>
    private void SyncTick()
    {
        _ticks++;
        // Some network drives keep coarse timestamps: compare the content itself now and then.
        if (_ticks % 5 == 0)
        {
            _diskStamp = default;
            CheckForExternalChanges();
        }

        if (_ticks % 5 == 0) ReadPresence();            // ~10 s
        if (_ticks % 8 == 0) MergeConflictCopies();     // ~16 s
        if (_ticks % 10 == 0) WritePresence();          // ~20 s
    }

    // ---------- presence: who else has the project open ----------

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
            // Stale notes expire on their own.
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
                        // Half-synced note: skip it this time.
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
