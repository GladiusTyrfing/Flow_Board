using System.Windows;
using System.Windows.Media;

namespace FlowBoard.Helpers;

public interface ICapturable
{
    FrameworkElement CaptureElement { get; }

    Rect? CaptureArea { get; }

    Brush? CaptureBackground { get; }

    string CaptureName { get; }
}
