using System.Windows;
using System.Windows.Controls;

namespace FlowBoard.Controls;

/// <summary>Small date button with a calendar popup (no text box, no focus outline).</summary>
public partial class DateChip : UserControl
{
    public static readonly DependencyProperty DateProperty = DependencyProperty.Register(
        nameof(Date), typeof(DateTime?), typeof(DateChip),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((DateChip)d).UpdateLabel()));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(DateChip), new PropertyMetadata("Add date", (d, _) => ((DateChip)d).UpdateLabel()));

    private bool _syncing;

    public DateChip()
    {
        InitializeComponent();
        UpdateLabel();
    }

    public DateTime? Date
    {
        get => (DateTime?)GetValue(DateProperty);
        set => SetValue(DateProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    private void UpdateLabel()
    {
        if (Label == null) return;
        Label.Text = Date is { } d ? Models.Card.FormatDate(d, includeTime: false) : Placeholder;
        Label.Opacity = Date == null ? 0.7 : 1;
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        _syncing = true;
        Cal.SelectedDate = Date;
        Cal.DisplayDate = Date ?? DateTime.Today;
        _syncing = false;
        Pop.IsOpen = true;
    }

    private void OnPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || Cal.SelectedDate is not { } d) return;
        Date = d.Date;
        Pop.IsOpen = false;
    }

    private void OnToday(object sender, RoutedEventArgs e) => Set(DateTime.Today);
    private void OnTomorrow(object sender, RoutedEventArgs e) => Set(DateTime.Today.AddDays(1));
    private void OnClear(object sender, RoutedEventArgs e) => Set(null);

    private void Set(DateTime? d)
    {
        Date = d;
        Pop.IsOpen = false;
    }
}
