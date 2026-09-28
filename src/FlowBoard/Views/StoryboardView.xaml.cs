using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FlowBoard.Models;
using FlowBoard.Services;
using FlowBoard.ViewModels;
using GongSolutions.Wpf.DragDrop;

namespace FlowBoard.Views;

public partial class StoryboardView : UserControl
{
    private StoryboardViewModel? _vm;

    public StoryboardView()
    {
        ShotDropHandler = new ShotReorderHandler(this);
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.RenderRequested -= OnRenderRequested;
            _vm = DataContext as StoryboardViewModel;
            if (_vm != null) _vm.RenderRequested += OnRenderRequested;
        };
    }

    public IDropTarget ShotDropHandler { get; }

    /// <summary>Mouse wheel scrolls the shot row sideways unless the column under the mouse can scroll itself.</summary>
    private void OnScrollerWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            for (var d = e.OriginalSource as DependencyObject; d != null && d != Scroller; d = VisualTreeHelper.GetParent(d))
            {
                if (d is ScrollViewer sv && sv.ScrollableHeight > 0)
                {
                    var canScroll = e.Delta > 0 ? sv.VerticalOffset > 0 : sv.VerticalOffset < sv.ScrollableHeight;
                    if (canScroll) return;
                }

                if (d is not Visual) break;
            }
        }

        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnFrameDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFrameDrop(object sender, DragEventArgs e)
    {
        if (_vm == null || sender is not FrameworkElement { Tag: Shot shot }) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            _vm.DropImage(shot, files[0]);
            e.Handled = true;
        }
    }

    private void OnRenderRequested(object? sender, string what)
    {
        if (_vm == null) return;
        try
        {
            // Render the whole row (not only the visible part) on the app background.
            ExportRoot.UpdateLayout();
            var bg = (Brush)FindResource("Fb.WindowBrush");
            var image = MediaStore.Render(ExportRoot, 2, bg);
            if (what == "print") MediaStore.Print(image, _vm.Board.Name);
            else if (MediaStore.ExportPng(image, _vm.Board.Name)) _vm.Main.ShowToast("Storyboard exported");
        }
        catch (Exception ex)
        {
            _vm.Main.ShowToast($"Export failed: {ex.Message}", isError: true);
        }
    }

    /// <summary>Reorders shots by dragging a column; snapshots first so Ctrl+Z restores the order.</summary>
    private sealed class ShotReorderHandler(StoryboardView view) : DefaultDropHandler
    {
        public override void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.Data is Shot && dropInfo.DragInfo?.SourceCollection == dropInfo.TargetCollection)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                dropInfo.Effects = DragDropEffects.Move;
            }
            else
            {
                dropInfo.Effects = DragDropEffects.None;
            }
        }

        public override void Drop(IDropInfo dropInfo)
        {
            if (dropInfo.Data is not Shot) return;
            view._vm?.BeforeReorder();
            base.Drop(dropInfo);
        }
    }
}
