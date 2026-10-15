using System.IO;

namespace FlowBoard.Services;

public static class AppPaths
{
    public const string ProjectExtension = ".flowboard";

    public static string AppDir { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlowBoard");

    public static bool IsPortable { get; private set; }

    public static string? ProjectFile { get; private set; }

    private static string? _mediaDir;

    public static string DataDir => ProjectFile != null ? _mediaDir ?? Path.GetDirectoryName(ProjectFile)! : AppDir;

    public static string DataFile => ProjectFile ?? LegacyDataFile;
    public static string LegacyDataFile => Path.Combine(AppDir, "data.json");
    public static string SettingsFile => Path.Combine(AppDir, "settings.json");
    public static string AttachmentsDir => Path.Combine(DataDir, "attachments");
    public static string BackgroundsDir => Path.Combine(DataDir, "backgrounds");
    public static string BackupsDir => Path.Combine(DataDir, "backups");
    public static string TempDir => Path.Combine(AppDir, "temp");

    public static string? CustomProjectsDir { get; set; }

    public static string BuiltInProjectsDir => IsPortable
        ? Path.Combine(AppDir, "Projects")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FlowBoard Projects");

    public static string DefaultProjectsDir => string.IsNullOrWhiteSpace(CustomProjectsDir) ? BuiltInProjectsDir : CustomProjectsDir;

    public static void DetectLocation()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "portable.txt")))
        {
            IsPortable = true;
            AppDir = Path.Combine(exeDir, "Data");
        }
    }

    public static string OpenRequestFile => Path.Combine(AppDir, "open-request.txt");

    public static void Initialize()
    {
        DetectLocation();

        foreach (var dir in new[] { AppDir, TempDir })
            Directory.CreateDirectory(dir);

        try
        {
            foreach (var f in Directory.GetFiles(TempDir)) File.Delete(f);
        }
        catch
        {
        }
    }

    public static void SetProject(string? file, string? mediaDir = null)
    {
        ProjectFile = file == null ? null : Path.GetFullPath(file);
        _mediaDir = mediaDir == null ? null : Path.GetFullPath(mediaDir);
        if (ProjectFile == null) return;
        foreach (var dir in new[] { AttachmentsDir, BackgroundsDir, BackupsDir })
            Directory.CreateDirectory(dir);
    }

    public static string ProjectNameOf(string file)
    {
        if (!string.Equals(Path.GetFileName(file), "data.json", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(file);
        var dir = Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty;
        return string.Equals(dir.TrimEnd('\\', '/'), AppDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
            ? "My workspace"
            : Path.GetFileName(dir);
    }

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
