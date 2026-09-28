using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FlowBoard.Models;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

public partial class CalendarView : UserControl, Helpers.ICapturable
{
    private Point _down;

    public CalendarView()
    {
        InitializeComponent();
    }

    private void Chip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _down = e.GetPosition(this);

    private void Chip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((e.GetPosition(this) - _down).Length > 6) return;
        if (sender is FrameworkElement { DataContext: Card card })
        {
            MainViewModel.Instance.OpenCard(card);
            e.Handled = true;
        }
    }

    // ----- high-res screenshot -----
    FrameworkElement Helpers.ICapturable.CaptureElement => this;
    Rect? Helpers.ICapturable.CaptureArea => null;
    System.Windows.Media.Brush? Helpers.ICapturable.CaptureBackground => null;
    string Helpers.ICapturable.CaptureName => ((DataContext as ViewModels.MainViewModel)?.CurrentBoard?.Name ?? "Board") + " calendar";
}
