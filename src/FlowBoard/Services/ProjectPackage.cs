using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FlowBoard.Models;

namespace FlowBoard.Services;

public static class ProjectPackage
{
    public const string ContentEntry = "project.json";
    public const string ManifestEntry = "manifest.json";
    public const int FormatVersion = 1;

    public static bool IsPackage(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return fs.ReadByte() == 'P' && fs.ReadByte() == 'K';
        }
        catch
        {
            return false;
        }
    }

    public static string ReadJson(string path)
    {
        if (!IsPackage(path)) return File.ReadAllText(path);
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry(ContentEntry) ?? throw new InvalidDataException("This file is not a FlowBoard project (no project data inside).");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static string? ReadSavedBy(string path)
    {
        try
        {
            if (!IsPackage(path)) return null;
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry(ManifestEntry);
            if (entry == null) return null;
            using var s = entry.Open();
            using var doc = JsonDocument.Parse(s);
            return doc.RootElement.TryGetProperty("savedBy", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public static Workspace Read(string path) =>
        JsonSerializer.Deserialize<Workspace>(ReadJson(path), Json.Options) ?? throw new InvalidDataException("The project file is empty.");

    public static void Write(string path, string json, string? savedBy = null)
    {
        var tmp = path + ".tmp";
        if (string.Equals(Path.GetExtension(path), AppPaths.ProjectExtension, StringComparison.OrdinalIgnoreCase))
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var manifest = zip.CreateEntry(ManifestEntry, CompressionLevel.Fastest);
                using (var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
                {
                    w.Write(JsonSerializer.Serialize(new
                    {
                        app = "FlowBoard",
                        format = FormatVersion,
                        savedWith = typeof(ProjectPackage).Assembly.GetName().Version?.ToString(3),
                        saved = DateTime.Now,
                        savedBy,
                        machine = Environment.MachineName,
                    }));
                }

                var content = zip.CreateEntry(ContentEntry, CompressionLevel.Optimal);
                using var cw = new StreamWriter(content.Open(), new UTF8Encoding(false));
                cw.Write(json);
            }
        }
        else
        {
            File.WriteAllText(tmp, json);
        }

        File.Move(tmp, path, overwrite: true);
    }

    public static string MediaDirFor(string file, string? rememberedFolder)
    {
        var full = Path.GetFullPath(file);
        var dir = Path.GetDirectoryName(full)!;
        if (string.Equals(Path.GetFileName(full), "data.json", StringComparison.OrdinalIgnoreCase)) return dir;
        if (!string.IsNullOrWhiteSpace(rememberedFolder))
        {
            var remembered = Path.Combine(dir, rememberedFolder);
            if (Directory.Exists(remembered)) return remembered;
        }

        var named = Path.Combine(dir, MediaFolderName(full));
        if (Directory.Exists(named)) return named;
        var legacy = new[] { "attachments", "backgrounds", "backups" }.Any(d => Directory.Exists(Path.Combine(dir, d)));
        return legacy ? dir : named;
    }

    public static string MediaFolderName(string file) => Path.GetFileNameWithoutExtension(file) + " files";
}
