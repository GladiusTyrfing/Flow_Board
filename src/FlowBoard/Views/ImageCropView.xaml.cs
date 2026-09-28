using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

/// <summary>Crop box interaction: drag to move, drag corners to resize (keeping the aspect when one is chosen).</summary>
public partial class ImageCropView : UserControl
{
    private ImageCropViewModel? _vm;
    private string? _mode;
    private Point _start;
    private Rect _startCrop;

    public ImageCropView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as ImageCropViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmChanged;
            Layout();
        };
        Picture.SizeChanged += (_, _) => Layout();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImageCropViewModel.Crop)) Layout();
    }

    private double W => Picture.ActualWidth;
    private double H => Picture.ActualHeight;

    private void Layout()
    {
        if (_vm == null || W <= 0 || H <= 0) return;
        Overlay.Width = W;
        Overlay.Height = H;
        var c = _vm.Crop;
        var r = new Rect(c.X * W, c.Y * H, c.Width * W, c.Height * H);
        Canvas.SetLeft(Box, r.X);
        Canvas.SetTop(Box, r.Y);
        Box.Width = r.Width;
        Box.Height = r.Height;
        Place(HandleNW, r.Left, r.Top);
        Place(HandleNE, r.Right, r.Top);
        Place(HandleSW, r.Left, r.Bottom);
        Place(HandleSE, r.Right, r.Bottom);
        var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
        g.Children.Add(new RectangleGeometry(new Rect(0, 0, W, H)));
        g.Children.Add(new RectangleGeometry(r));
        Shade.Data = g;
    }

    private static void Place(FrameworkElement handle, double x, double y)
    {
        Canvas.SetLeft(handle, x - 7);
        Canvas.SetTop(handle, y - 7);
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null || e.OriginalSource is not FrameworkElement { Tag: string tag }) return;
        _mode = tag;
        _start = e.GetPosition(Overlay);
        _startCrop = _vm.Crop;
        Overlay.CaptureMouse();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_vm == null || _mode == null || W <= 0) return;
        var p = e.GetPosition(Overlay);
        double dx = (p.X - _start.X) / W, dy = (p.Y - _start.Y) / H;
        var s = _startCrop;
        const double min = 0.04;

        if (_mode == "move")
        {
            var x = Math.Clamp(s.X + dx, 0, 1 - s.Width);
            var y = Math.Clamp(s.Y + dy, 0, 1 - s.Height);
            _vm.Crop = new Rect(x, y, s.Width, s.Height);
            return;
        }

        // Fixed corner is the one opposite the handle being dragged.
        bool west = _mode.Contains('w'), north = _mode.Contains('n');
        var fixedX = west ? s.Right : s.Left;
        var fixedY = north ? s.Bottom : s.Top;
        var movingX = Math.Clamp((west ? s.Left : s.Right) + dx, 0, 1);
        var movingY = Math.Clamp((north ? s.Top : s.Bottom) + dy, 0, 1);
        var w = Math.Max(min, Math.Abs(movingX - fixedX));
        var h = Math.Max(min, Math.Abs(movingY - fixedY));

        var ratio = _vm.SelectedAspect.Ratio;
        if (ratio > 0)
        {
            // Normalized units: pixel aspect = (w * imgW) / (h * imgH).
            var k = ratio / _vm.ImageAspect;
            if (h * k > w) w = h * k; // follow whichever direction moved more
            h = w / k;
            var maxH = north ? fixedY : 1 - fixedY;
            var maxW = west ? fixedX : 1 - fixedX;
            if (h > maxH) { h = maxH; w = h * k; }
            if (w > maxW) { w = maxW; h = w / k; }
        }

        var left = west ? fixedX - w : fixedX;
        var top = north ? fixedY - h : fixedY;
        _vm.Crop = new Rect(Math.Max(0, left), Math.Max(0, top), Math.Min(w, 1), Math.Min(h, 1));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        _mode = null;
        Overlay.ReleaseMouseCapture();
    }
}
