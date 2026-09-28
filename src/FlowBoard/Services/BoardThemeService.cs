using System.Windows;
using System.Windows.Media;
using FlowBoard.Models;
using static FlowBoard.Services.ThemeService;

namespace FlowBoard.Services;

/// <summary>A board look: base colors for lists and cards plus readable text colors.</summary>
public sealed record BoardThemePreset(string Name, bool IsDark, Color List, Color Card, Color Text, Color Muted)
{
    /// <summary>Small preview tile (list color with a card on top) for the style picker.</summary>
    public Brush ListPreview => Frozen(new SolidColorBrush(List));
    public Brush CardPreview => Frozen(new SolidColorBrush(Card));
    public Brush TextPreview => Frozen(new SolidColorBrush(Text));
}

/// <summary>
/// Applies a board's style (preset + list/card transparency) by overriding the shared color
/// resources on the board area only, so the rest of the app keeps the app theme.
/// </summary>
public static class BoardThemeService
{
    public static IReadOnlyList<BoardThemePreset> Presets { get; } =
    [
        // "Auto" is resolved at apply time from the app theme.
        new("Auto", true, Hex("#0E1118"), Hex("#1A1E29"), Hex("#EEF1F6"), Hex("#98A2B3")),
        new("Glass", true, Hex("#0B0D14"), Hex("#1C2130"), Hex("#F3F5FA"), Hex("#A3ADBF")),
        new("Frost", false, Hex("#FFFFFF"), Hex("#FFFFFF"), Hex("#111827"), Hex("#5B6475")),
        new("Midnight", true, Hex("#07080C"), Hex("#12151D"), Hex("#E6E9F0"), Hex("#8A93A6")),
        new("Ink", true, Hex("#0F172A"), Hex("#1E293B"), Hex("#E2E8F0"), Hex("#94A3B8")),
        new("Plum", true, Hex("#1B1030"), Hex("#2A1A48"), Hex("#F3EAFF"), Hex("#B9A6DA")),
        new("Ocean", true, Hex("#062431"), Hex("#0C3445"), Hex("#E2F6FF"), Hex("#8DB9CC")),
        new("Forest", true, Hex("#0C2116"), Hex("#143222"), Hex("#E4F7EB"), Hex("#8FB9A0")),
        new("Ember", true, Hex("#24110B"), Hex("#351B12"), Hex("#FFF0E8"), Hex("#D1A897")),
        new("Neon", true, Hex("#050507"), Hex("#11111A"), Hex("#F5F5FF"), Hex("#9C9CC4")),
        new("Paper", false, Hex("#F3F1EC"), Hex("#FFFFFF"), Hex("#2A2722"), Hex("#6F685C")),
        new("Sand", false, Hex("#F4E7D6"), Hex("#FFF8EE"), Hex("#3B2F22"), Hex("#7D6A55")),
        new("Mint", false, Hex("#DDF3EA"), Hex("#F7FFFB"), Hex("#10271D"), Hex("#4F6F61")),
        new("Lilac", false, Hex("#ECE6FB"), Hex("#FFFFFF"), Hex("#1F1535"), Hex("#6A5F85")),
    ];

    public static BoardThemePreset Resolve(string? name)
    {
        var preset = Presets.FirstOrDefault(p => p.Name == name) ?? Presets[0];
        if (preset.Name != "Auto") return preset;
        return IsDark
            ? preset
            : new BoardThemePreset("Auto", false, Hex("#F6F7FB"), Hex("#FFFFFF"), Hex("#111827"), Hex("#5B6475"));
    }

    // The board dictionary currently merged into a host (board area or a preview).
    private static readonly DependencyProperty CurrentDictionaryProperty = DependencyProperty.RegisterAttached(
        "CurrentDictionary", typeof(ResourceDictionary), typeof(BoardThemeService), new PropertyMetadata(null));

    /// <summary>
    /// Applies <paramref name="board"/>'s look to everything inside <paramref name="host"/> by swapping in one
    /// merged dictionary (a single resource refresh, so sliders stay smooth). Null clears it.
    /// </summary>
    public static void Apply(FrameworkElement host, Board? board)
    {
        var current = (ResourceDictionary?)host.GetValue(CurrentDictionaryProperty);
        var fresh = board == null ? null : Build(board);
        ThemeService.SwapMerged(host.Resources, ref current, fresh);
        host.SetValue(CurrentDictionaryProperty, current);
    }

    public static ResourceDictionary Build(Board board)
    {
        var d = new ResourceDictionary();
        var p = Resolve(board.Theme);
        var listA = (byte)Math.Round(255 * Math.Clamp(board.ListOpacity, 0, 1));
        var cardA = (byte)Math.Round(255 * Math.Clamp(board.CardOpacity, 0.05, 1));
        var ink = p.IsDark ? Colors.White : Hex("#0B1220");

        Set(d, "Fb.ListBrush", WithAlpha(p.List, listA));
        Set(d, "Fb.ListBorderBrush", WithAlpha(ink, p.IsDark ? (byte)0x1A : (byte)0x14));
        Set(d, "Fb.CardBrush", WithAlpha(p.Card, cardA));
        Set(d, "Fb.CardHoverBrush", WithAlpha(Mix(p.Card, ink, p.IsDark ? 0.07 : 0.03), (byte)Math.Max((int)cardA, 0xE6)));
        Set(d, "Fb.CardBorderBrush", WithAlpha(ink, p.IsDark ? (byte)0x14 : (byte)0x12));
        Set(d, "Fb.ListHeaderTextBrush", p.Text);
        Set(d, "Fb.TextBrush", p.Text);
        Set(d, "Fb.MutedTextBrush", p.Muted);
        Set(d, "Fb.FaintTextBrush", WithAlpha(p.Muted, 0xB0));
        Set(d, "Fb.SubtleBrush", WithAlpha(ink, 0x10));
        Set(d, "Fb.SubtleHoverBrush", WithAlpha(ink, 0x1E));
        Set(d, "Fb.SubtlePressedBrush", WithAlpha(ink, 0x2B));
        Set(d, "Fb.InputBrush", WithAlpha(p.Card, 0xFF));
        Set(d, "Fb.InputBorderBrush", WithAlpha(ink, 0x33));
        Set(d, "Fb.DividerBrush", WithAlpha(ink, 0x18));
        Set(d, "Fb.HeaderBarBrush", p.IsDark ? WithAlpha(p.List, 0x80) : WithAlpha(Colors.White, 0xA6));
        Set(d, "Fb.HeaderTextBrush", p.Text);
        Set(d, "Fb.SurfaceBrush", WithAlpha(p.List, (byte)Math.Max((int)listA, 0xD9)));
        Set(d, "Fb.SurfaceAltBrush", WithAlpha(ink, 0x0A));
        Set(d, "Fb.SurfaceRaisedBrush", WithAlpha(ink, 0x14));
        Set(d, "Fb.CalendarCellBrush", WithAlpha(p.Card, (byte)Math.Max(cardA * 0.55, 0x30)));

        // Board-specific corner roundness (negative = inherit the app setting).
        if (board.CornerRadius >= 0) ApplyRadius(d, board.CornerRadius);
        return d;
    }

    private static void Set(ResourceDictionary d, string key, Color c) => d[key] = Frozen(new SolidColorBrush(c));
}
