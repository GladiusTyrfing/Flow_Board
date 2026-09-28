using System.IO;

namespace FlowBoard.Services;

/// <summary>
/// All data lives locally. App-wide things (settings, temp files, the error log) live in %AppData%\FlowBoard,
/// or in a "Data" folder next to the executable when a file named "portable.txt" sits beside it.
/// Each project is a .flowboard file; its attachments, wallpapers and backups sit in folders next to it.
/// </summary>
public static class AppPaths
{
    public const string ProjectExtension = ".flowboard";

    public static string AppDir { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlowBoard");

    public static bool IsPortable { get; private set; }

    /// <summary>The open project file, or null when no project is open.</summary>
    public static string? ProjectFile { get; private set; }

    private static string? _mediaDir;

    /// <summary>Media folder of the open project; media paths are stored relative to it.</summary>
    public static string DataDir => ProjectFile != null ? _mediaDir ?? Path.GetDirectoryName(ProjectFile)! : AppDir;

    public static string DataFile => ProjectFile ?? LegacyDataFile;
    /// <summary>Where everything was stored before projects existed. It opens like any other project.</summary>
    public static string LegacyDataFile => Path.Combine(AppDir, "data.json");
    public static string SettingsFile => Path.Combine(AppDir, "settings.json");
    public static string AttachmentsDir => Path.Combine(DataDir, "attachments");
    public static string BackgroundsDir => Path.Combine(DataDir, "backgrounds");
    public static string BackupsDir => Path.Combine(DataDir, "backups");
    public static string TempDir => Path.Combine(AppDir, "temp");

    /// <summary>Projects folder chosen in Settings (null = the built-in default).</summary>
    public static string? CustomProjectsDir { get; set; }

    public static string BuiltInProjectsDir => IsPortable
        ? Path.Combine(AppDir, "Projects")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FlowBoard Projects");

    public static string DefaultProjectsDir => string.IsNullOrWhiteSpace(CustomProjectsDir) ? BuiltInProjectsDir : CustomProjectsDir;

    /// <summary>Works out where app data lives (normal or portable) without touching any files.</summary>
    public static void DetectLocation()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "portable.txt")))
        {
            IsPortable = true;
            AppDir = Path.Combine(exeDir, "Data");
        }
    }

    /// <summary>A second FlowBoard started with a file leaves it here for the running one.</summary>
    public static string OpenRequestFile => Path.Combine(AppDir, "open-request.txt");

    public static void Initialize()
    {
        DetectLocation();

        foreach (var dir in new[] { AppDir, TempDir })
            Directory.CreateDirectory(dir);

        // Clean leftovers from interrupted recordings.
        try
        {
            foreach (var f in Directory.GetFiles(TempDir)) File.Delete(f);
        }
        catch
        {
            // Not critical.
        }
    }

    /// <summary>Switches to a project file (or to none) and makes sure its media folders exist.</summary>
    public static void SetProject(string? file, string? mediaDir = null)
    {
        ProjectFile = file == null ? null : Path.GetFullPath(file);
        _mediaDir = mediaDir == null ? null : Path.GetFullPath(mediaDir);
        if (ProjectFile == null) return;
        foreach (var dir in new[] { AttachmentsDir, BackgroundsDir, BackupsDir })
            Directory.CreateDirectory(dir);
    }

    /// <summary>Display name of a project file: its file name, or the folder name for a plain data.json.</summary>
    public static string ProjectNameOf(string file)
    {
        if (!string.Equals(Path.GetFileName(file), "data.json", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(file);
        var dir = Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty;
        return string.Equals(dir.TrimEnd('\\', '/'), AppDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
            ? "My workspace"
            : Path.GetFileName(dir);
    }

    /// <summary>Makes a file or folder name safe for the file system.</summary>
    public static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(name) ? "Untitled project" : name;
    }

    public static string CardFolder(Guid cardId)
    {
        var dir = Path.Combine(AttachmentsDir, cardId.ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string ToRelative(string fullPath) => Path.GetRelativePath(DataDir, fullPath);

    public static string ToFull(string relative) => Path.Combine(DataDir, relative);

    /// <summary>Returns a file path in <paramref name="dir"/> that does not yet exist, based on <paramref name="fileName"/>.</summary>
    public static string UniquePath(string dir, string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (string.IsNullOrWhiteSpace(name)) name = "file";
        var candidate = Path.Combine(dir, name + ext);
        int i = 2;
        while (File.Exists(candidate)) candidate = Path.Combine(dir, $"{name} ({i++}){ext}");
        return candidate;
    }
}
