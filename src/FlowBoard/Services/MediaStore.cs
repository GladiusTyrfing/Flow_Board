using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FlowBoard.Services;

/// <summary>Images and renders for storyboards, canvases and notes (stored under attachments\docs\&lt;id&gt;).</summary>
public static class MediaStore
{
    public const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.tif;*.tiff|All files|*.*";

    public const string AudioFilter = "Audio|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.flac;*.ogg|All files|*.*";

    public static bool IsAudioFile(string path) =>
        Models.Attachment.AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()) || Path.GetExtension(path).ToLowerInvariant() is ".flac";

    /// <summary>Length of an audio file in seconds (0 when it can't be read).</summary>
    public static double AudioSeconds(string fullPath)
    {
        try
        {
            using var reader = new NAudio.Wave.AudioFileReader(fullPath);
            return reader.TotalTime.TotalSeconds;
        }
        catch
        {
            try
            {
                using var mf = new NAudio.Wave.MediaFoundationReader(fullPath);
                return mf.TotalTime.TotalSeconds;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>Lets the user pick audio files from the PC.</summary>
    public static string[] PickAudio(bool multiple)
    {
        var dlg = new OpenFileDialog { Filter = AudioFilter, Title = "Choose audio", Multiselect = multiple };
        return dlg.ShowDialog() == true ? dlg.FileNames : [];
    }

    public static string DocFolder(Guid owner)
    {
        var dir = Path.Combine(AppPaths.AttachmentsDir, "docs", owner.ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static bool IsImageFile(string path) =>
        Models.Attachment.ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Copies a file into the document folder and returns its path relative to the data folder.</summary>
    public static string ImportFile(string source, Guid owner)
    {
        var dest = AppPaths.UniquePath(DocFolder(owner), Path.GetFileName(source));
        File.Copy(source, dest);
        return AppPaths.ToRelative(dest);
    }

    /// <summary>Lets the user pick an image; returns the stored relative path or null.</summary>
    public static string? PickImage(Guid owner)
    {
        var dlg = new OpenFileDialog { Filter = ImageFilter, Title = "Choose an image" };
        return dlg.ShowDialog() == true ? ImportFile(dlg.FileName, owner) : null;
    }

    /// <summary>Stores a clipboard image (or the first copied image file). Returns null when there is none.</summary>
    public static string? FromClipboard(Guid owner)
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var file = Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(IsImageFile);
                return file == null ? null : ImportFile(file, owner);
            }

            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
                return SavePng(image, owner, $"Pasted image {DateTime.Now:yyyy-MM-dd HHmmss}.png");
        }
        catch
        {
            // Clipboard can be locked by another app.
        }

        return null;
    }

    public static string SavePng(BitmapSource image, Guid owner, string fileName)
    {
        var dest = AppPaths.UniquePath(DocFolder(owner), fileName);
        WritePng(image, dest);
        return AppPaths.ToRelative(dest);
    }

    public static void WritePng(BitmapSource image, string path)
    {
        using var fs = File.Create(path);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(image));
        enc.Save(fs);
    }

    public static void TryDelete(string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return;
        try { File.Delete(AppPaths.ToFull(relative)); } catch { }
    }

    /// <summary>
    /// Renders any element (even one inside a transformed/scrolled parent) at its own size, optionally
    /// scaled up for a crisp export. <paramref name="background"/> fills behind transparent areas.
    /// </summary>
    public static BitmapSource Render(FrameworkElement element, double scale = 2, Brush? background = null, Rect? area = null)
    {
        // The element is rendered directly at the target resolution (text and shapes stay vector-sharp).
        // A VisualBrush would go through an intermediate bitmap of limited size, which blurs large captures.
        var rect = area ?? new Rect(0, 0, Math.Max(1, element.ActualWidth), Math.Max(1, element.ActualHeight));
        var w = Math.Max(1, (int)Math.Ceiling(rect.Width * scale));
        var h = Math.Max(1, (int)Math.Ceiling(rect.Height * scale));
        var dpi = 96 * scale;
        var rtb = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);

        if (background != null)
        {
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(background, null, new Rect(0, 0, rect.Width, rect.Height));
            rtb.Render(bg);
        }

        // RenderTargetBitmap draws the element at its offset in the parent (scroll position, margins...):
        // shift it so the requested area lands at the top-left, then put everything back.
        var offset = VisualTreeHelper.GetOffset(element);
        var local = element.ReadLocalValue(UIElement.RenderTransformProperty);
        var original = element.RenderTransform;
        var shift = new TranslateTransform(-rect.X - offset.X, -rect.Y - offset.Y);
        element.RenderTransform = original == null || original.Value.IsIdentity
            ? shift
            : new TransformGroup { Children = { original, shift } };
        try
        {
            rtb.Render(element);
        }
        finally
        {
            if (local == DependencyProperty.UnsetValue) element.ClearValue(UIElement.RenderTransformProperty);
            else element.SetValue(UIElement.RenderTransformProperty, local);
        }

        rtb.Freeze();
        return rtb;
    }

    /// <summary>Asks where to save and writes a PNG. Returns true when saved.</summary>
    public static bool ExportPng(BitmapSource image, string suggestedName)
    {
        var name = suggestedName;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        var dlg = new SaveFileDialog { Filter = "PNG image|*.png", FileName = name + ".png", Title = "Export as image" };
        if (dlg.ShowDialog() != true) return false;
        WritePng(image, dlg.FileName);
        return true;
    }

    /// <summary>Prints an image scaled to fit the page.</summary>
    public static void Print(BitmapSource image, string description)
    {
        var pd = new PrintDialog();
        if (pd.ShowDialog() != true) return;
        var pageW = pd.PrintableAreaWidth - 48;
        var pageH = pd.PrintableAreaHeight - 48;
        var s = Math.Min(pageW / image.PixelWidth, pageH / image.PixelHeight);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawImage(image, new Rect(24, 24, image.PixelWidth * s, image.PixelHeight * s));
        pd.PrintVisual(dv, description);
    }
}
