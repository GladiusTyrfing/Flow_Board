using System.Windows;
using System.Windows.Media;
using FlowBoard.Models;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FlowBoard.Services;

/// <summary>Applies dark/light theme, accent color and window backdrop (Mica/Acrylic).</summary>
public static class ThemeService
{
    private static AppSettings? _settings;
    private static Window? _window;

    public static bool IsDark { get; private set; } = true;

    public static event EventHandler? ThemeApplied;

    public static void Initialize(AppSettings settings)
    {
        _settings = settings;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _settings?.Theme == ThemeMode.System)
                Application.Current?.Dispatcher.BeginInvoke(() => Apply());
        };
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
        ApplicationThemeManager.Apply(theme, backdrop, updateAccent: s.AccentColor == null);
        if (TryParseColor(s.AccentColor, out var accent))
            ApplicationAccentColorManager.Apply(accent, theme);

        if (_window is FluentWindow fw)
        {
            fw.WindowBackdropType = backdrop;
            ApplicationThemeManager.Apply(fw);
        }

        ThemeApplied?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Mica/Acrylic/Tabbed backdrops need Windows 11 (build 22000+). Windows 10 gets a solid window.</summary>
    public static bool SupportsBackdrop { get; } = Environment.OSVersion.Version.Build >= 22000;

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
