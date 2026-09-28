using System.Windows;
using System.Windows.Media;

namespace FlowBoard.Helpers;

/// <summary>A view that can be saved as a high-resolution screenshot (its whole content, not only what's on screen).</summary>
public interface ICapturable
{
    /// <summary>The element to render (scrolled-away parts included).</summary>
    FrameworkElement CaptureElement { get; }

    /// <summary>Part of the element to render (null = all of it).</summary>
    Rect? CaptureArea { get; }

    /// <summary>What fills transparent areas (a board's wallpaper, for example). Null = the app background.</summary>
    Brush? CaptureBackground { get; }

    string CaptureName { get; }
}
