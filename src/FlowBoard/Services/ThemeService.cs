using System.Windows;
using System.Windows.Media;
using FlowBoard.Models;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FlowBoard.Services;

public sealed record AccentPreset(string Name, Color Primary, Color Secondary)
{
    public Brush Preview
    {
        get
        {
            var b = new LinearGradientBrush(Primary, Secondary, 45);
            b.Freeze();
            return b;
        }
    }
}

/// <summary>Applies dark/light theme, the accent gradient, the background glow and the window backdrop.</summary>
public static class ThemeService
{
    private static AppSettings? _settings;
    private static Window? _window;

    public static bool IsDark { get; private set; } = true;

    /// <summary>Mica/Acrylic/Tabbed backdrops need Windows 11 (build 22000+). Windows 10 gets a solid window.</summary>
    public static bool SupportsBackdrop { get; } = Environment.OSVersion.Version.Build >= 22000;

    public static event EventHandler? ThemeApplied;

    public static IReadOnlyList<AccentPreset> AccentPresets { get; } =
    [
        new("Aurora", Hex("#8B5CF6"), Hex("#06B6D4")),
        new("Sunset", Hex("#F97316"), Hex("#EC4899")),
        new("Ocean", Hex("#2563EB"), Hex("#06B6D4")),
        new("Emerald", Hex("#10B981"), Hex("#84CC16")),
        new("Rose", Hex("#F43F5E"), Hex("#A855F7")),
        new("Gold", Hex("#F59E0B"), Hex("#EF4444")),
        new("Ice", Hex("#38BDF8"), Hex("#A5B4FC")),
        new("Mono", Hex("#64748B"), Hex("#A1A1AA")),
    ];

    public static AccentPreset CurrentAccent =>
        AccentPresets.FirstOrDefault(a => a.Name == _settings?.AccentPreset) ?? AccentPresets[0];

    public static void Initialize(AppSettings settings)
    {
        _settings = settings;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _settings?.Theme == ThemeMode.System)
                Application.Current?.Dispatcher.BeginInvoke(() => Apply());
        };
    }

    private static System.Windows.Threading.DispatcherTimer? _radiusTimer;
    private static double _appliedRadius = double.NaN;
    private static ResourceDictionary? _radiusDictionary;

    /// <summary>
    /// Cheap path for the corner slider: only the corner resources change (no palette/theme swap).
    /// Updates are applied at most every 40 ms while dragging, so the preview follows the thumb smoothly.
    /// </summary>
    public static void QueueRadiusUpdate()
    {
        if (_radiusTimer == null)
        {
            _radiusTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(40),
            };
            _radiusTimer.Tick += (_, _) =>
            {
                _radiusTimer.Stop();
                SetAppRadius(_settings?.CornerRadius ?? 8);
            };
        }

        if (!_radiusTimer.IsEnabled) _radiusTimer.Start();
    }

    private static void SetAppRadius(double value)
    {
        var r = Math.Round(Math.Clamp(value, 0, 24));
        if (r.Equals(_appliedRadius)) return;
        _appliedRadius = r;
        SwapMerged(Application.Current.Resources, ref _radiusDictionary, BuildRadius(r));
    }

    /// <summary>Replaces one merged dictionary in a single step (one resource refresh instead of one per key).</summary>
    public static void SwapMerged(ResourceDictionary target, ref ResourceDictionary? current, ResourceDictionary? fresh)
    {
        var list = target.MergedDictionaries;
        var index = current != null ? list.IndexOf(current) : -1;
        if (fresh == null)
        {
            if (index >= 0) list.RemoveAt(index);
        }
        else if (index >= 0)
        {
            list[index] = fresh;
        }
        else
        {
            list.Add(fresh);
        }

        current = fresh;
    }

    public static void Attach(Window window)
    {
        _window = window;
        Apply();
    }

    public static void Apply()
    {
        if (_settings == null) return;
        var s = _settings;
        IsDark = s.Theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemPrefersDark(),
        };

        var theme = IsDark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        var backdrop = ToBackdrop(s.Backdrop);

        SetPalette(IsDark);
        ApplicationThemeManager.Apply(theme, backdrop, updateAccent: false);
        var accent = CurrentAccent;
        ApplicationAccentColorManager.Apply(accent.Primary, theme);
        SetAccentResources(accent);
        _appliedRadius = double.NaN;
        SetAppRadius(s.CornerRadius);

        if (_window is FluentWindow fw)
        {
            fw.WindowBackdropType = backdrop;
            ApplicationThemeManager.Apply(fw);
        }

        ThemeApplied?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// One roundness value drives every corner: small chips, controls, grouped pills and large panels.
    /// Also feeds WPF-UI's own corner keys so its text boxes, check boxes and menus match.
    /// </summary>
    public static ResourceDictionary BuildRadius(double r)
    {
        var res = new ResourceDictionary();
        ApplyRadius(res, r);
        return res;
    }

    public static void ApplyRadius(ResourceDictionary res, double r)
    {
        r = Math.Clamp(r, 0, 24);
        var large = r <= 0 ? 0 : r + 6;
        res["Fb.RadiusSmall"] = new CornerRadius(Math.Min(r, 6));
        res["Fb.Radius"] = new CornerRadius(r);
        res["Fb.RadiusGroup"] = new CornerRadius(r <= 0 ? 0 : r + 3);
        res["Fb.RadiusLarge"] = new CornerRadius(large);
        res["Fb.RadiusValue"] = r;
        res["Fb.RadiusLargeValue"] = large;
        res["ControlCornerRadius"] = new CornerRadius(r);
        res["OverlayCornerRadius"] = new CornerRadius(large);
        res["PopupCornerRadius"] = new CornerRadius(large);
    }

    public static readonly string[] RadiusKeys =
    [
        "Fb.RadiusSmall", "Fb.Radius", "Fb.RadiusGroup", "Fb.RadiusLarge", "Fb.RadiusValue", "Fb.RadiusLargeValue",
        "ControlCornerRadius", "OverlayCornerRadius", "PopupCornerRadius",
    ];

    private static void SetAccentResources(AccentPreset accent)
    {
        var res = Application.Current.Resources;
        res["Fb.AccentColor"] = accent.Primary;
        res["Fb.Accent2Color"] = accent.Secondary;
        res["Fb.AccentBrush"] = Frozen(new SolidColorBrush(accent.Primary));
        res["Fb.Accent2Brush"] = Frozen(new SolidColorBrush(accent.Secondary));
        res["Fb.AccentGradientBrush"] = Frozen(new LinearGradientBrush(accent.Primary, accent.Secondary, new Point(0, 0), new Point(1, 1)));
        res["Fb.AccentSoftBrush"] = Frozen(new SolidColorBrush(WithAlpha(accent.Primary, 0x38)));
        res["Fb.AccentTextBrush"] = Frozen(new SolidColorBrush(IsDark ? Mix(accent.Primary, Colors.White, 0.35) : Mix(accent.Primary, Colors.Black, 0.1)));
        res["Fb.OnAccentBrush"] = Frozen(new SolidColorBrush(Colors.White));

        // Soft glow blobs behind the app (strength differs per theme).
        byte a = IsDark ? (byte)0x55 : (byte)0x40;
        res["Fb.Aurora1Brush"] = Glow(WithAlpha(accent.Primary, a));
        res["Fb.Aurora2Brush"] = Glow(WithAlpha(accent.Secondary, a));
        res["Fb.Aurora3Brush"] = Glow(WithAlpha(Mix(accent.Primary, Hex("#EC4899"), 0.6), (byte)(a * 0.7)));
    }

    private static Brush Glow(Color c)
    {
        var b = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(c, 0),
                new GradientStop(WithAlpha(c, (byte)(c.A * 0.45)), 0.45),
                new GradientStop(WithAlpha(c, 0), 1),
            },
        };
        b.Freeze();
        return b;
    }

    public static WindowBackdropType ToBackdrop(BackdropMode mode) => !SupportsBackdrop ? WindowBackdropType.None : mode switch
    {
        BackdropMode.Acrylic => WindowBackdropType.Acrylic,
        BackdropMode.Tabbed => WindowBackdropType.Tabbed,
        BackdropMode.None => WindowBackdropType.None,
        _ => WindowBackdropType.Mica,
    };

    public static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    private static void SetPalette(bool dark)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        var existing = dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains("Palette.") == true);
        var fresh = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/Palette.{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };
        if (existing != null) dicts[dicts.IndexOf(existing)] = fresh;
        else dicts.Add(fresh);
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v ? v == 0 : true;
        }
        catch
        {
            return true;
        }
    }
}
