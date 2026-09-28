using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FlowBoard.Controls;

/// <summary>
/// "Custom color" swatch: opens a <see cref="ColorPicker"/>; "Use color" runs <see cref="Command"/> with the hex
/// (and raises <see cref="ColorChosen"/>). Put it at the end of any palette.
/// </summary>
public partial class ColorButton : UserControl
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(ColorButton), new PropertyMetadata(null));

    public static readonly DependencyProperty InitialColorProperty = DependencyProperty.Register(
        nameof(InitialColor), typeof(string), typeof(ColorButton), new PropertyMetadata(null));

    /// <summary>Text put before the hex when running the command (e.g. "color:" for board backgrounds).</summary>
    public static readonly DependencyProperty PrefixProperty = DependencyProperty.Register(
        nameof(Prefix), typeof(string), typeof(ColorButton), new PropertyMetadata(string.Empty));

    public string Prefix
    {
        get => (string)GetValue(PrefixProperty);
        set => SetValue(PrefixProperty, value);
    }

    public ColorButton()
    {
        InitializeComponent();
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public string? InitialColor
    {
        get => (string?)GetValue(InitialColorProperty);
        set => SetValue(InitialColorProperty, value);
    }

    public event EventHandler<string>? ColorChosen;

    /// <summary>Raised when the popup opens / closes (e.g. to keep a text selection alive).</summary>
    public event EventHandler? Opened;
    public event EventHandler? ClosedPicker;

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(InitialColor)) Picker.Color = InitialColor!;
        else if (ColorPicker.Recent.FirstOrDefault() is { } recent) Picker.Color = recent;
        Pop.IsOpen = true;
        Opened?.Invoke(this, EventArgs.Empty);
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        var hex = Picker.Color;
        ColorPicker.Remember(hex);
        Pop.IsOpen = false;
        var arg = Prefix + hex;
        if (Command?.CanExecute(arg) == true) Command.Execute(arg);
        ColorChosen?.Invoke(this, hex);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Pop.IsOpen = false;

    private void OnClosed(object? sender, EventArgs e) => ClosedPicker?.Invoke(this, EventArgs.Empty);
}
