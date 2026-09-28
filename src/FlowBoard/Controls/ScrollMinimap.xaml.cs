using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FlowBoard.ViewModels;

namespace FlowBoard.Controls;

/// <summary>
/// Overview of a scrollable area (live miniature of its content) with the visible part outlined.
/// Click or drag on it to jump. Hidden automatically when everything already fits on screen.
/// </summary>
public partial class ScrollMinimap : UserControl
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(ScrollViewer), typeof(ScrollMinimap), new PropertyMetadata(null, (d, e) => ((ScrollMinimap)d).OnTargetChanged(e)));

    public static readonly DependencyProperty MaxMapWidthProperty = DependencyProperty.Register(
        nameof(MaxMapWidth), typeof(double), typeof(ScrollMinimap), new PropertyMetadata(220.0));

    public static readonly DependencyProperty MaxMapHeightProperty = DependencyProperty.Register(
        nameof(MaxMapHeight), typeof(double), typeof(ScrollMinimap), new PropertyMetadata(130.0));

    private double _scale = 1;
    private bool _dragging;

    public ScrollMinimap()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            MainViewModel.Instance.Settings.PropertyChanged += OnSettingsChanged;
            Refresh();
        };
        Unloaded += (_, _) => MainViewModel.Instance.Settings.PropertyChanged -= OnSettingsChanged;
    }

    public ScrollViewer? Target
    {
        get => (ScrollViewer?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    public double MaxMapWidth
    {
        get => (double)GetValue(MaxMapWidthProperty);
        set => SetValue(MaxMapWidthProperty, value);
    }

    public double MaxMapHeight
    {
        get => (double)GetValue(MaxMapHeightProperty);
        set => SetValue(MaxMapHeightProperty, value);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Models.AppSettings.ShowMinimap)) Refresh();
    }

    private void OnTargetChanged(DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ScrollViewer old)
        {
            old.ScrollChanged -= OnScroll;
            old.SizeChanged -= OnSize;
        }

        if (e.NewValue is ScrollViewer sv)
        {
            sv.ScrollChanged += OnScroll;
            sv.SizeChanged += OnSize;
        }

        Refresh();
    }

    private void OnScroll(object sender, ScrollChangedEventArgs e) => Refresh();

    private void OnSize(object sender, SizeChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var sv = Target;
        var show = MainViewModel.Instance?.Settings.ShowMinimap ?? true;
        if (sv?.Content is not FrameworkElement content || content.ActualWidth <= 0 || content.ActualHeight <= 0)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        // Nothing to navigate when it all fits.
        var fits = sv.ExtentWidth <= sv.ViewportWidth + 2 && sv.ExtentHeight <= sv.ViewportHeight + 2;
        Visibility = fits ? Visibility.Collapsed : Visibility.Visible;
        Panel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ShowButton.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (fits || !show) return;

        double w = content.ActualWidth, h = content.ActualHeight;
        _scale = Math.Min(MaxMapWidth / w, MaxMapHeight / h);
        Map.Width = w * _scale;
        Map.Height = h * _scale;
        Preview.Width = Map.Width;
        Preview.Height = Map.Height;
        if (Preview.Fill is not VisualBrush vb || vb.Visual != content)
            Preview.Fill = new VisualBrush(content) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h) };
        else
            vb.Viewbox = new Rect(0, 0, w, h);

        Canvas.SetLeft(ViewportBox, sv.HorizontalOffset * _scale);
        Canvas.SetTop(ViewportBox, sv.VerticalOffset * _scale);
        ViewportBox.Width = Math.Min(Map.Width, sv.ViewportWidth * _scale);
        ViewportBox.Height = Math.Min(Map.Height, sv.ViewportHeight * _scale);
    }

    private void Jump(Point p)
    {
        if (Target is not { } sv || _scale <= 0) return;
        sv.ScrollToHorizontalOffset(p.X / _scale - sv.ViewportWidth / 2);
        sv.ScrollToVerticalOffset(p.Y / _scale - sv.ViewportHeight / 2);
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        Map.CaptureMouse();
        Jump(e.GetPosition(Map));
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_dragging) Jump(e.GetPosition(Map));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        Map.ReleaseMouseCapture();
    }

    private void OnHide(object sender, RoutedEventArgs e) => MainViewModel.Instance.Settings.ShowMinimap = false;

    private void OnShow(object sender, RoutedEventArgs e) => MainViewModel.Instance.Settings.ShowMinimap = true;
}
