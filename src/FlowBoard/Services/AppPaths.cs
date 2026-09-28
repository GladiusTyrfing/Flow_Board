using System.IO;

namespace FlowBoard.Services;

/// <summary>
/// All data lives locally. By default in %AppData%\FlowBoard; if a file named "portable.txt"
/// sits next to the executable, data is kept in a "Data" folder beside it instead.
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlowBoard");

    public static bool IsPortable { get; private set; }

    public static string DataFile => Path.Combine(DataDir, "data.json");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string AttachmentsDir => Path.Combine(DataDir, "attachments");
    public static string BackgroundsDir => Path.Combine(DataDir, "backgrounds");
    public static string BackupsDir => Path.Combine(DataDir, "backups");
    public static string TempDir => Path.Combine(DataDir, "temp");

    public static void Initialize()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "portable.txt")))
        {
            IsPortable = true;
            DataDir = Path.Combine(exeDir, "Data");
        }

        foreach (var dir in new[] { DataDir, AttachmentsDir, BackgroundsDir, BackupsDir, TempDir })
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
