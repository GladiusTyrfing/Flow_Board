using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

/// <summary>Scroll syncing and bar dragging for the timeline.</summary>
public partial class TimelineView : UserControl, Helpers.ICapturable
{
    private TimelineViewModel? _vm;
    private TimelineRow? _dragRow;
    private string _dragMode = "move";
    private Point _dragStart;
    private int _dragDays;

    public TimelineView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => _vm?.ScrollToToday());
        };
    }

    private void Hook()
    {
        if (_vm != null) _vm.ScrollRequested -= OnScrollRequested;
        _vm = (DataContext as MainViewModel)?.Timeline;
        if (_vm != null) _vm.ScrollRequested += OnScrollRequested;
    }

    private void OnScrollRequested(object? sender, double x) =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => ChartScroller.ScrollToHorizontalOffset(Math.Max(0, x)));

    private void OnChartScroll(object sender, ScrollChangedEventArgs e)
    {
        HeaderScroller.ScrollToHorizontalOffset(ChartScroller.HorizontalOffset);
        NamesScroller.ScrollToVerticalOffset(ChartScroller.VerticalOffset);
    }

    private void OnNamesWheel(object sender, MouseWheelEventArgs e)
    {
        ChartScroller.ScrollToVerticalOffset(ChartScroller.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    /// <summary>Wheel scrolls rows; Shift+wheel moves through time; Ctrl+wheel zooms.</summary>
    private void OnChartWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm == null) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Delta > 0) _vm.ZoomInCommand.Execute(null);
            else _vm.ZoomOutCommand.Execute(null);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ChartScroller.ScrollToHorizontalOffset(ChartScroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private void OnChartDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null) return;
        for (var d = e.OriginalSource as DependencyObject; d is Visual && d != Chart; d = VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { Tag: string tag, DataContext: TimelineRow row } && tag is "move" or "start" or "end")
            {
                if (e.ClickCount == 2)
                {
                    _vm.OpenCommand.Execute(row);
                    e.Handled = true;
                    return;
                }

                _dragRow = row;
                _dragMode = tag;
                _dragStart = e.GetPosition(Chart);
                _dragDays = 0;
                Chart.CaptureMouse();
                e.Handled = true;
                return;
            }
        }
    }

    private void OnChartMove(object sender, MouseEventArgs e)
    {
        if (_vm == null || _dragRow == null) return;
        var days = (int)Math.Round((e.GetPosition(Chart).X - _dragStart.X) / _vm.DayWidth);
        if (days == _dragDays) return;
        _dragDays = days;
        _vm.Preview(_dragRow, _dragMode, days);
    }

    private void OnChartUp(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null || _dragRow == null) return;
        Chart.ReleaseMouseCapture();
        var row = _dragRow;
        _dragRow = null;
        _vm.Commit(row, _dragMode, _dragDays);
    }

    // ----- high-res screenshot -----
    FrameworkElement Helpers.ICapturable.CaptureElement => Chart;
    Rect? Helpers.ICapturable.CaptureArea => null;
    System.Windows.Media.Brush? Helpers.ICapturable.CaptureBackground => null;
    string Helpers.ICapturable.CaptureName => ((DataContext as ViewModels.MainViewModel)?.CurrentBoard?.Name ?? "Board") + " timeline";
}
