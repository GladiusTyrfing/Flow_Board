using System.Windows;
using System.Windows.Controls;

namespace FlowBoard.Views;

public partial class DashboardView : UserControl, Helpers.ICapturable
{
    public DashboardView()
    {
        InitializeComponent();
    }

    // ----- high-res screenshot -----
    FrameworkElement Helpers.ICapturable.CaptureElement => DashContent;
    Rect? Helpers.ICapturable.CaptureArea => null;
    System.Windows.Media.Brush? Helpers.ICapturable.CaptureBackground => null;
    string Helpers.ICapturable.CaptureName => "Dashboard";
}
