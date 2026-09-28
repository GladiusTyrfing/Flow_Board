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
        var rect = area ?? new Rect(0, 0, Math.Max(1, element.ActualWidth), Math.Max(1, element.ActualHeight));
        var w = (int)Math.Ceiling(rect.Width * scale);
        var h = (int)Math.Ceiling(rect.Height * scale);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var target = new Rect(0, 0, rect.Width * scale, rect.Height * scale);
            if (background != null) dc.DrawRectangle(background, null, target);
            var brush = new VisualBrush(element)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = rect,
            };
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawRectangle(brush, null, new Rect(0, 0, rect.Width, rect.Height));
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
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
