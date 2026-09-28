using System.Collections;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlowBoard.Models;
using FlowBoard.Services;
using Wpf.Ui.Controls;

namespace FlowBoard.Converters;

/// <summary>true → Visible. ConverterParameter "invert" flips it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is true;
        if (parameter as string == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var v = value is Visibility.Visible;
        return parameter as string == "invert" ? !v : v;
    }
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>null, empty string, 0 or empty collection → Collapsed. "invert" flips it.</summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool has = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i != 0,
            double d => d != 0,
            ICollection c => c.Count > 0,
            IEnumerable e => e.GetEnumerator().MoveNext(),
            _ => true,
        };
        if (parameter as string == "invert") has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Compares an enum (or any value) to the ConverterParameter by name. Works two-way for radio buttons.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var eq = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        if (targetType == typeof(Visibility)) return eq ? Visibility.Visible : Visibility.Collapsed;
        return eq;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string p)
        {
            var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (t.IsEnum) return Enum.Parse(t, p);
            return p;
        }

        return Binding.DoNothing;
    }
}

public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex)) return Brushes.Transparent;
        if (Cache.TryGetValue(hex, out var b)) return b;
        if (!ThemeService.TryParseColor(hex, out var c)) return Brushes.Transparent;
        b = new SolidColorBrush(c);
        b.Freeze();
        Cache[hex] = b;
        return b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class HexToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string hex && ThemeService.TryParseColor(hex, out var c) ? c : Colors.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Black or white text depending on the brightness of a hex background.</summary>
public sealed class ContrastBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Dark = Freeze(new SolidColorBrush(Color.FromRgb(0x1D, 0x21, 0x25)));
    private static readonly SolidColorBrush Light = Freeze(new SolidColorBrush(Colors.White));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && ThemeService.TryParseColor(hex, out var c))
        {
            var lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
            return lum > 0.55 ? Dark : Light;
        }

        return Light;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    private static SolidColorBrush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}

/// <summary>Turns a board background spec ("gradient:#a,#b", "color:#a", "image:path") into a brush.</summary>
public sealed class BoardBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => ToBrush(value as string);

    public static Brush ToBrush(string? spec, int decodeWidth = 0)
    {
        if (string.IsNullOrEmpty(spec)) return Brushes.SlateGray;
        try
        {
            if (spec == "none") return Brushes.Transparent;
            if (spec.StartsWith("gradient:"))
            {
                var parts = spec[9..].Split(',');
                var stops = new GradientStopCollection();
                for (int i = 0; i < parts.Length; i++)
                    stops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(parts[i]), parts.Length == 1 ? 0 : (double)i / (parts.Length - 1)));
                var g = new LinearGradientBrush(stops, new Point(0, 0), new Point(1, 1));
                g.Freeze();
                return g;
            }

            if (spec.StartsWith("color:"))
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(spec[6..]));
                b.Freeze();
                return b;
            }

            if (spec.StartsWith("image:"))
            {
                var path = AppPaths.ToFull(spec[6..]);
                var img = ImageLoader.Load(path, decodeWidth > 0 ? decodeWidth : 2560);
                if (img != null)
                {
                    var ib = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
                    ib.Freeze();
                    return ib;
                }
            }
        }
        catch
        {
            // Fall back below.
        }

        return Brushes.SlateGray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Small thumbnail version of a board background (for the sidebar and pickers).</summary>
public sealed class BoardThumbnailConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value as string == "none" ? Checker : BoardBackgroundConverter.ToBrush(value as string, 160);

    /// <summary>Checkerboard that signals "transparent".</summary>
    private static readonly Brush Checker = CreateChecker();

    private static Brush CreateChecker()
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA8)), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        var dark = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x68));
        g.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 4, 4))));
        g.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(4, 4, 4, 4))));
        var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute };
        b.Freeze();
        return b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Loads images without locking the file, decoded at a reduced size. ConverterParameter = decode width.</summary>
public sealed class ImagePathConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var width = parameter is string s && int.TryParse(s, out var w) ? w : 480;
        return value is string path ? ImageLoader.Load(path, width) : null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public static class ImageLoader
{
    private static readonly Dictionary<(string, int), WeakReference<BitmapImage>> Cache = new();

    public static BitmapImage? Load(string? path, int decodeWidth)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var key = (path, decodeWidth);
        if (Cache.TryGetValue(key, out var weak) && weak.TryGetTarget(out var cached)) return cached;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
            bmp.EndInit();
            bmp.Freeze();
            Cache[key] = new WeakReference<BitmapImage>(bmp);
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}

public sealed class DueStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            DueState.Overdue => "Fb.DangerBrush",
            DueState.Soon => "Fb.WarningBrush",
            DueState.Done => "Fb.SuccessBrush",
            _ => null,
        };
        if (key != null && Application.Current.TryFindResource(key) is Brush b) return b;
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class DueStateToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DueState.Overdue or DueState.Soon or DueState.Done)
            return Application.Current.TryFindResource("Fb.OnStatusBrush") as Brush ?? Brushes.Black;
        return Application.Current.TryFindResource("Fb.MutedTextBrush") as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class PriorityToBrushConverter : IValueConverter
{
    public static Color ColorOf(Priority p) => p switch
    {
        Priority.Low => Color.FromRgb(0x57, 0x9D, 0xFF),
        Priority.Medium => Color.FromRgb(0xF5, 0xCD, 0x47),
        Priority.High => Color.FromRgb(0xFE, 0xA3, 0x62),
        Priority.Urgent => Color.FromRgb(0xF8, 0x71, 0x68),
        _ => Colors.Transparent,
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = new SolidColorBrush(value is Priority p ? ColorOf(p) : Colors.Transparent);
        b.Freeze();
        return b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class PriorityToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Priority p && p != Priority.None ? p.ToString() : "No priority";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InitialsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var name = (value as string ?? "?").Trim();
        var parts = name.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class RelativeTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime t) return string.Empty;
        return Format(t);
    }

    public static string Format(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalSeconds < 45) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)Math.Max(1, d.TotalMinutes)} minute{((int)d.TotalMinutes == 1 ? "" : "s")} ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} hour{((int)d.TotalHours == 1 ? "" : "s")} ago";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays} day{((int)d.TotalDays == 1 ? "" : "s")} ago";
        return t.ToString(t.Year == DateTime.Now.Year ? "MMM d 'at' HH:mm" : "MMM d, yyyy 'at' HH:mm");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class StringToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && Enum.TryParse<SymbolRegular>(s, out var sym) ? sym : SymbolRegular.Board24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class AttachmentIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Attachment a) return SymbolRegular.Document24;
        if (a.Kind == AttachmentKind.Link) return SymbolRegular.Link24;
        if (a.Kind == AttachmentKind.Voice) return SymbolRegular.Mic24;
        if (a.Kind == AttachmentKind.Image) return SymbolRegular.Image24;
        return Path.GetExtension(a.Name).ToLowerInvariant() switch
        {
            ".pdf" => SymbolRegular.DocumentPdf24,
            ".zip" or ".7z" or ".rar" => SymbolRegular.FolderZip24,
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => SymbolRegular.Video24,
            ".mp3" or ".wav" or ".m4a" or ".flac" or ".ogg" => SymbolRegular.MusicNote224,
            ".xls" or ".xlsx" or ".csv" => SymbolRegular.DocumentTable24,
            ".cs" or ".js" or ".ts" or ".py" or ".json" or ".xml" or ".html" or ".css" or ".cpp" or ".h" => SymbolRegular.Code24,
            ".txt" or ".md" or ".log" => SymbolRegular.DocumentText24,
            _ => SymbolRegular.Document24,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Multiplies a number by the ConverterParameter (used for progress widths etc.).</summary>
public sealed class MultiplyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var v = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        var m = double.TryParse(parameter as string, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 1;
        return v * m;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class DateOnlyTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateTime d ? Models.Card.FormatDate(d) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Is the given label id (value[1]) contained in the card's label ids (value[0])?</summary>
public sealed class ContainsMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is IEnumerable<Guid> ids && values[1] is Guid id && ids.Contains(id);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => [];
}

/// <summary>Packs several bindings into an object[] (for commands that need two parameters).</summary>
public sealed class ArrayMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values.Clone();

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => [];
}

/// <summary>True when all bound values are the same object (e.g. "is this the current board?").</summary>
public sealed class SameMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] != null && ReferenceEquals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => [];
}

/// <summary>Binds int settings to NumberBox (double?) values.</summary>
public sealed class IntToDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int i ? (double?)i : null!;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? (int)Math.Round(d) : Binding.DoNothing;
}

/// <summary>Returns "selected" when both values are equal (drives the swatch ring via Tag).</summary>
public sealed class SelectedTagMultiConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && Equals(values[0]?.ToString(), values[1]?.ToString()) ? "selected" : null;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => [];
}

/// <summary>0.42 → "42%".</summary>
public sealed class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? $"{Math.Round(d * 100)}%" : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Live preview of what smart quick add understood ("Due Fri 15:00 · High · #video").</summary>
public sealed class SmartPreviewConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return string.Empty;
        var first = s.Split('\n')[0];
        var parsed = SmartParser.Parse(first, DateTime.Now);
        return parsed.HasExtras ? parsed.Summary : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Opposite of <see cref="EqualsConverter"/> (one-way): true / Visible when the value differs from the parameter.</summary>
public sealed class NotEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var differs = !string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        if (targetType == typeof(Visibility)) return differs ? Visibility.Visible : Visibility.Collapsed;
        return differs;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>3.5 → "3.5s", 75 → "1:15".</summary>
public sealed class SecondsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value is double d ? d : 0;
        return s < 60 ? $"{s:0.#}s" : TimeSpan.FromSeconds(s).ToString(@"m\:ss");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible / true when a number is greater than the parameter (default 0).</summary>
public sealed class GreaterThanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var v = value == null ? 0 : System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        var limit = double.TryParse(parameter as string, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0;
        var result = v > limit;
        if (targetType == typeof(Visibility)) return result ? Visibility.Visible : Visibility.Collapsed;
        return result;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Path mini-language string → frozen Geometry (empty string → nothing).</summary>
public sealed class GeometryConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string s || s.Length == 0) return null;
        try
        {
            var g = Geometry.Parse(s);
            g.Freeze();
            return g;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Indent level → left margin (ConverterParameter = pixels per level, default 28).</summary>
public sealed class IndentToMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var step = double.TryParse(parameter as string, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 28;
        return new Thickness((value is int i ? i : 0) * step, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Symbol name string ("Rocket24") → SymbolRegular for dynamic icons.</summary>
public sealed class SymbolNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && Enum.TryParse<SymbolRegular>(s, out var sym) ? sym : SymbolRegular.DocumentText24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Storyboard mode → sidebar icon.</summary>
public sealed class StoryboardIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is StoryboardMode.Animation ? SymbolRegular.Sparkle24 : SymbolRegular.VideoClip24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
