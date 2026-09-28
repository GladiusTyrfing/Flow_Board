using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FlowBoard.Models;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

public partial class BoardView : UserControl
{
    private Point _cardDownPoint;
    private bool _isPanning;
    private Point _panStart;
    private double _panStartOffset;

    public BoardView()
    {
        InitializeComponent();
    }

    // ---------- open a card on click (but not after a drag) ----------

    private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _cardDownPoint = e.GetPosition(this);
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Card card }) return;
        if ((e.GetPosition(this) - _cardDownPoint).Length > 6) return;
        if (IsInside<ButtonBase>(e.OriginalSource as DependencyObject, (DependencyObject)sender)) return;
        MainViewModel.Instance.OpenCard(card);
        e.Handled = true;
    }

    // ---------- hover tracking for card hotkeys ----------

    private void Card_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Card card }) MainViewModel.Instance.HoveredCard = card;
    }

    private void Card_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Card card } && MainViewModel.Instance.HoveredCard == card)
            MainViewModel.Instance.HoveredCard = null;
    }

    // ---------- mouse wheel scrolls sideways unless a list can scroll ----------

    private void BoardScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var inner = FindAncestor<ScrollViewer>(e.OriginalSource as DependencyObject, BoardScroller);
            if (inner != null && inner.ScrollableHeight > 0) return;
        }

        BoardScroller.ScrollToHorizontalOffset(BoardScroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    // ---------- drag the empty board background to pan ----------

    private void BoardScroller_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsBackground(e.OriginalSource as DependencyObject)) return;
        _isPanning = true;
        _panStart = e.GetPosition(BoardScroller);
        _panStartOffset = BoardScroller.HorizontalOffset;
        BoardScroller.CaptureMouse();
        Cursor = Cursors.SizeWE;
        e.Handled = true;
    }

    private void BoardScroller_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var dx = e.GetPosition(BoardScroller).X - _panStart.X;
        BoardScroller.ScrollToHorizontalOffset(_panStartOffset - dx);
    }

    private void BoardScroller_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        BoardScroller.ReleaseMouseCapture();
        Cursor = null;
    }

    /// <summary>True when the click landed on bare board area (not a list, card, button or scrollbar).</summary>
    private bool IsBackground(DependencyObject? d)
    {
        while (d != null && d != BoardScroller)
        {
            if (d is ButtonBase or TextBoxBase or ScrollBar or Thumb) return false;
            if (d is FrameworkElement { DataContext: BoardList or Card }) return false;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }

        return true;
    }

    private static T? FindAncestor<T>(DependencyObject? d, DependencyObject stopAt) where T : DependencyObject
    {
        while (d != null && d != stopAt)
        {
            if (d is T t) return t;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }

        return null;
    }

    private static bool IsInside<T>(DependencyObject? d, DependencyObject stopAt) where T : DependencyObject =>
        FindAncestor<T>(d, stopAt) != null;
}
