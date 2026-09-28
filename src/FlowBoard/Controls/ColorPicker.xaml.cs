using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FlowBoard.Services;

namespace FlowBoard.Controls;

/// <summary>Any color: saturation/brightness square, hue bar, hex input, presets and recently used colors.</summary>
public partial class ColorPicker : UserControl
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(string), typeof(ColorPicker),
        new FrameworkPropertyMetadata("#8B5CF6", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((ColorPicker)d).OnColorChanged()));

    public static readonly string[] Presets =
    [
        "#000000", "#374151", "#6B7280", "#D1D5DB", "#FFFFFF", "#7F1D1D", "#EF4444", "#F97316", "#F59E0B", "#FACC15",
        "#84CC16", "#22C55E", "#10B981", "#14B8A6", "#06B6D4", "#3B82F6", "#6366F1", "#8B5CF6", "#A855F7", "#EC4899",
    ];

    /// <summary>Colors picked recently (shared by every picker in the app).</summary>
    public static ObservableCollection<string> Recent { get; } = [];

    private double _h, _s = 1, _v = 1;
    private bool _updating;

    public ColorPicker()
    {
        InitializeComponent();
        PresetList.ItemsSource = Presets;
        RecentList.ItemsSource = Recent;
        Loaded += (_, _) =>
        {
            RecentTitle.Visibility = Recent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            OnColorChanged();
        };
        SizeChanged += (_, _) => PlaceThumbs();
    }

    public string Color
    {
        get => (string)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public static void Remember(string hex)
    {
        hex = hex.ToUpperInvariant();
        Recent.Remove(hex);
        Recent.Insert(0, hex);
        while (Recent.Count > 10) Recent.RemoveAt(Recent.Count - 1);
    }

    private void OnColorChanged()
    {
        if (_updating || HexBox == null) return;
        if (ThemeService.TryParseColor(Color, out var c))
        {
            (_h, _s, _v) = ToHsv(c);
            HexBox.Text = ToHex(c);
        }

        Refresh();
    }

    private void Refresh()
    {
        var c = FromHsv(_h, _s, _v);
        HueFill.Background = new SolidColorBrush(FromHsv(_h, 1, 1));
        Preview.Background = new SolidColorBrush(c);
        SvThumb.Fill = new SolidColorBrush(c);
        PlaceThumbs();
    }

    private void PlaceThumbs()
    {
        if (SvArea.ActualWidth <= 0) return;
        Canvas.SetLeft(SvThumb, _s * SvArea.ActualWidth - 7);
        Canvas.SetTop(SvThumb, (1 - _v) * SvArea.ActualHeight - 7);
        Canvas.SetLeft(HueThumb, _h / 360 * HueArea.ActualWidth - 4);
    }

    private void Commit()
    {
        var hex = ToHex(FromHsv(_h, _s, _v));
        _updating = true;
        Color = hex;
        HexBox.Text = hex;
        _updating = false;
        Refresh();
    }

    // ---------- mouse ----------

    private void OnSvDown(object sender, MouseButtonEventArgs e)
    {
        SvArea.CaptureMouse();
        SetSv(e.GetPosition(SvArea));
    }

    private void OnSvMove(object sender, MouseEventArgs e)
    {
        if (SvArea.IsMouseCaptured) SetSv(e.GetPosition(SvArea));
    }

    private void SetSv(Point p)
    {
        _s = Math.Clamp(p.X / Math.Max(1, SvArea.ActualWidth), 0, 1);
        _v = 1 - Math.Clamp(p.Y / Math.Max(1, SvArea.ActualHeight), 0, 1);
        Commit();
    }

    private void OnHueDown(object sender, MouseButtonEventArgs e)
    {
        HueArea.CaptureMouse();
        SetHue(e.GetPosition(HueArea));
    }

    private void OnHueMove(object sender, MouseEventArgs e)
    {
        if (HueArea.IsMouseCaptured) SetHue(e.GetPosition(HueArea));
    }

    private void SetHue(Point p)
    {
        _h = Math.Clamp(p.X / Math.Max(1, HueArea.ActualWidth), 0, 0.9999) * 360;
        Commit();
    }

    private void OnRelease(object sender, MouseButtonEventArgs e) => ((UIElement)sender).ReleaseMouseCapture();

    private void OnSwatch(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string hex }) Color = hex;
    }

    private void OnHexKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnHexCommit(sender, null!);
    }

    private void OnHexCommit(object sender, KeyboardFocusChangedEventArgs? e)
    {
        var t = HexBox.Text.Trim();
        if (!t.StartsWith('#')) t = "#" + t;
        if (ThemeService.TryParseColor(t, out var c)) Color = ToHex(c);
        else HexBox.Text = Color;
    }

    // ---------- color math ----------

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }

        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    public static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (h % 360) switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return System.Windows.Media.Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
