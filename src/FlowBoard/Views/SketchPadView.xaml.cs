using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using FlowBoard.Services;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

public partial class SketchPadView : UserControl
{
    private SketchPadViewModel? _vm;

    public SketchPadView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as SketchPadViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmChanged;
            ApplyPen();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _vm?.UndoStrokeCommand.Execute(null);
                e.Handled = true;
            }
        };
        Loaded += (_, _) => Ink.Focus();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e) => ApplyPen();

    private void ApplyPen()
    {
        if (_vm == null) return;
        if (_vm.IsEraser)
        {
            Ink.EditingMode = InkCanvasEditingMode.EraseByPoint;
            Ink.EraserShape = new EllipseStylusShape(Math.Max(10, _vm.PenSize * 3), Math.Max(10, _vm.PenSize * 3));
            return;
        }

        Ink.EditingMode = InkCanvasEditingMode.Ink;
        ThemeService.TryParseColor(_vm.PenColor, out var color);
        Ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = color,
            Width = _vm.IsHighlighter ? _vm.PenSize * 3 : _vm.PenSize,
            Height = _vm.IsHighlighter ? _vm.PenSize * 3 : _vm.PenSize,
            FitToCurve = true,
            IgnorePressure = false,
            IsHighlighter = _vm.IsHighlighter,
            StylusTip = StylusTip.Ellipse,
        };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var guides = Guides.Visibility;
        Guides.Visibility = Visibility.Collapsed;
        Surface.UpdateLayout();
        try
        {
            var frame = MediaStore.Render(Surface, 2, Brushes.White);
            _vm.Save(frame);
        }
        finally
        {
            Guides.Visibility = guides;
        }
    }
}
