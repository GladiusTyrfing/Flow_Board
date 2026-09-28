using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FlowBoard.Models;
using FlowBoard.Services;
using FlowBoard.ViewModels;
using GongSolutions.Wpf.DragDrop;

namespace FlowBoard.Views;

public partial class StoryboardView : UserControl, Helpers.ICapturable
{
    private StoryboardViewModel? _vm;

    public StoryboardView()
    {
        ShotDropHandler = new ShotReorderHandler(this);
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.RenderRequested -= OnRenderRequested;
                _vm.ShotFocusRequested -= OnShotFocus;
            }

            _vm = DataContext as StoryboardViewModel;
            if (_vm != null)
            {
                _vm.RenderRequested += OnRenderRequested;
                _vm.ShotFocusRequested += OnShotFocus;
            }
        };
    }

    /// <summary>Scrolls a linked shot into the middle of the view.</summary>
    private void OnShotFocus(object? sender, Shot shot) =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (ShotsHost.ItemContainerGenerator.ContainerFromItem(shot) is not FrameworkElement c) return;
            var x = c.TransformToAncestor(ExportRoot).Transform(new Point(0, 0)).X;
            Scroller.ScrollToHorizontalOffset(Math.Max(0, x - (Scroller.ViewportWidth - c.ActualWidth) / 2));
        });

    public IDropTarget ShotDropHandler { get; }

    /// <summary>Mouse wheel scrolls the shot row sideways unless the column under the mouse can scroll itself.</summary>
    private void OnScrollerWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // Over a shot column: only that column scrolls (never the whole row, even at its top or bottom).
            for (var d = e.OriginalSource as DependencyObject; d is Visual && d != Scroller; d = VisualTreeHelper.GetParent(d))
            {
                if (d is ScrollViewer sv)
                {
                    sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 120.0 * 48);
                    e.Handled = true;
                    return;
                }
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

    // ----- high-res screenshot -----
    FrameworkElement Helpers.ICapturable.CaptureElement => ExportRoot;
    Rect? Helpers.ICapturable.CaptureArea => null;
    System.Windows.Media.Brush? Helpers.ICapturable.CaptureBackground => Helpers.CaptureHelpers.DocWallpaper();
    string Helpers.ICapturable.CaptureName => _vm?.Board.Name ?? "Storyboard";
}
