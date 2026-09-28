using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FlowBoard.Controls;

/// <summary>Editable text with a dropdown of suggestions, styled like the rest of the app.</summary>
public partial class ChoiceBox : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(ChoiceBox), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(IEnumerable), typeof(ChoiceBox), new PropertyMetadata(null));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(ChoiceBox), new PropertyMetadata(string.Empty));

    public ChoiceBox()
    {
        InitializeComponent();
        FontSize = 12;
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IEnumerable? Options
    {
        get => (IEnumerable?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (Options == null) return;
        var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.Bottom, MinWidth = ActualWidth };
        foreach (var o in Options)
        {
            var text = o?.ToString() ?? string.Empty;
            var item = new MenuItem { Header = text, IsCheckable = false, FontWeight = text == Value ? FontWeights.Bold : FontWeights.Normal };
            item.Click += (_, _) => Value = text;
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }
}
