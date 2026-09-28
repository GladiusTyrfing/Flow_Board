using System.IO;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowBoard.Services;

namespace FlowBoard.Models;

public partial class Attachment : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private AttachmentKind _kind;
    [ObservableProperty] private string _name = string.Empty;
    /// <summary>Path relative to the data folder (files, images, voice notes).</summary>
    [ObservableProperty] private string? _relativePath;
    /// <summary>Web address for link attachments.</summary>
    [ObservableProperty] private string? _url;
    [ObservableProperty] private long _size;
    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private DateTime _addedAt = DateTime.Now;

    // Runtime-only playback state for voice notes.
    [ObservableProperty][property: JsonIgnore] private bool _isPlaying;
    [ObservableProperty][property: JsonIgnore] private double _playbackProgress;
    [ObservableProperty][property: JsonIgnore] private string _playbackPositionText = string.Empty;

    [JsonIgnore]
    public string? FullPath => RelativePath == null ? null : Path.Combine(AppPaths.DataDir, RelativePath);

    [JsonIgnore]
    public bool Exists => Kind == AttachmentKind.Link || (FullPath != null && File.Exists(FullPath));

    [JsonIgnore]
    public string Extension => Kind == AttachmentKind.Link
        ? "LINK"
        : (Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext : "FILE");

    [JsonIgnore]
    public string SizeText => Kind == AttachmentKind.Link ? (Url ?? string.Empty) : FormatSize(Size);

    [JsonIgnore]
    public string DurationText => TimeSpan.FromSeconds(DurationSeconds).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{v:0} {units[u]}" : $"{v:0.#} {units[u]}";
    }

    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico"];
    public static readonly string[] AudioExtensions = [".wav", ".mp3", ".m4a", ".wma", ".aac"];
}
