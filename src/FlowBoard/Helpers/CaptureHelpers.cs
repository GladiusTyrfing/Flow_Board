using System.Windows;
using System.Windows.Media;
using FlowBoard.Converters;
using FlowBoard.Models;
using FlowBoard.ViewModels;

namespace FlowBoard.Helpers;

public static class CaptureHelpers
{
    /// <summary>The open document's wallpaper (for screenshots), or null for the app background.</summary>
    public static Brush? DocWallpaper()
    {
        if (MainViewModel.Instance.ActiveDocument?.Model is not StyledDocument { HasWallpaper: true } d) return null;
        var brush = BoardBackgroundConverter.ToBrush(d.Background);
        if (d.BackgroundDim <= 0) return brush;
        var group = new DrawingBrush(new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(0, 0, 1, 1))),
                new GeometryDrawing(new SolidColorBrush(Color.FromArgb((byte)(d.BackgroundDim * 255), 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 0, 1, 1))),
            },
        }) { Stretch = Stretch.Fill };
        return group;
    }
}
