using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FlowBoard.Helpers;

/// <summary>Small attached behaviors used across the XAML.</summary>
public static class Behaviors
{
    // ---------- Corner radius for shared button templates ----------

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Behaviors), new PropertyMetadata(new CornerRadius(10)));

    public static CornerRadius GetCornerRadius(DependencyObject d) => (CornerRadius)d.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject d, CornerRadius v) => d.SetValue(CornerRadiusProperty, v);

    // ---------- Rounded clipping (Border.CornerRadius does not clip its children) ----------

    public static readonly DependencyProperty ClipRadiusProperty = DependencyProperty.RegisterAttached(
        "ClipRadius", typeof(double), typeof(Behaviors), new PropertyMetadata(0.0, OnClipRadiusChanged));

    public static double GetClipRadius(DependencyObject d) => (double)d.GetValue(ClipRadiusProperty);
    public static void SetClipRadius(DependencyObject d, double v) => d.SetValue(ClipRadiusProperty, v);

    private static readonly DependencyProperty ClipHookedProperty = DependencyProperty.RegisterAttached(
        "ClipHooked", typeof(bool), typeof(Behaviors), new PropertyMetadata(false));

    private static void OnClipRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if (!(bool)fe.GetValue(ClipHookedProperty))
        {
            fe.SetValue(ClipHookedProperty, true);
            fe.SizeChanged += (_, _) => UpdateClip(fe);
        }

        UpdateClip(fe);
    }

    private static void UpdateClip(FrameworkElement fe)
    {
        var r = GetClipRadius(fe);
        // Radius 0 still clips (sharp corners) so content never spills outside rounded parents.
        fe.Clip = new RectangleGeometry(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight), r, r);
    }

    // ---------- Focus when a bound flag becomes true ----------

    public static readonly DependencyProperty FocusWhenProperty = DependencyProperty.RegisterAttached(
        "FocusWhen", typeof(bool), typeof(Behaviors), new PropertyMetadata(false, OnFocusWhenChanged));

    public static bool GetFocusWhen(DependencyObject d) => (bool)d.GetValue(FocusWhenProperty);
    public static void SetFocusWhen(DependencyObject d, bool v) => d.SetValue(FocusWhenProperty, v);

    private static void OnFocusWhenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && d is UIElement el) FocusLater(el);
    }

    /// <summary>Focus the element as soon as it is loaded and visible.</summary>
    public static readonly DependencyProperty FocusOnLoadProperty = DependencyProperty.RegisterAttached(
        "FocusOnLoad", typeof(bool), typeof(Behaviors), new PropertyMetadata(false, OnFocusOnLoadChanged));

    public static bool GetFocusOnLoad(DependencyObject d) => (bool)d.GetValue(FocusOnLoadProperty);
    public static void SetFocusOnLoad(DependencyObject d, bool v) => d.SetValue(FocusOnLoadProperty, v);

    private static void OnFocusOnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && d is FrameworkElement fe)
        {
            fe.Loaded += (_, _) => FocusLater(fe);
            fe.IsVisibleChanged += (_, args) =>
            {
                if (args.NewValue is true) FocusLater(fe);
            };
        }
    }

    public static void FocusLater(UIElement el)
    {
        el.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!el.IsVisible) return;
            el.Focus();
            Keyboard.Focus(el);
            if (el is TextBox tb && GetSelectAllOnFocus(tb)) tb.SelectAll();
            else if (el is TextBox t2) t2.CaretIndex = t2.Text.Length;
        });
    }

    public static readonly DependencyProperty SelectAllOnFocusProperty = DependencyProperty.RegisterAttached(
        "SelectAllOnFocus", typeof(bool), typeof(Behaviors), new PropertyMetadata(false));

    public static bool GetSelectAllOnFocus(DependencyObject d) => (bool)d.GetValue(SelectAllOnFocusProperty);
    public static void SetSelectAllOnFocus(DependencyObject d, bool v) => d.SetValue(SelectAllOnFocusProperty, v);

    // ---------- Enter / Escape commands on text boxes ----------

    public static readonly DependencyProperty EnterCommandProperty = DependencyProperty.RegisterAttached(
        "EnterCommand", typeof(ICommand), typeof(Behaviors), new PropertyMetadata(null, OnKeyCommandChanged));

    public static ICommand? GetEnterCommand(DependencyObject d) => (ICommand?)d.GetValue(EnterCommandProperty);
    public static void SetEnterCommand(DependencyObject d, ICommand? v) => d.SetValue(EnterCommandProperty, v);

    public static readonly DependencyProperty EscapeCommandProperty = DependencyProperty.RegisterAttached(
        "EscapeCommand", typeof(ICommand), typeof(Behaviors), new PropertyMetadata(null, OnKeyCommandChanged));

    public static ICommand? GetEscapeCommand(DependencyObject d) => (ICommand?)d.GetValue(EscapeCommandProperty);
    public static void SetEscapeCommand(DependencyObject d, ICommand? v) => d.SetValue(EscapeCommandProperty, v);

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.RegisterAttached(
        "CommandParameter", typeof(object), typeof(Behaviors), new PropertyMetadata(null));

    public static object? GetCommandParameter(DependencyObject d) => d.GetValue(CommandParameterProperty);
    public static void SetCommandParameter(DependencyObject d, object? v) => d.SetValue(CommandParameterProperty, v);

    /// <summary>Command executed when the element loses keyboard focus (commit inline edits).</summary>
    public static readonly DependencyProperty LostFocusCommandProperty = DependencyProperty.RegisterAttached(
        "LostFocusCommand", typeof(ICommand), typeof(Behaviors), new PropertyMetadata(null, OnLostFocusCommandChanged));

    public static ICommand? GetLostFocusCommand(DependencyObject d) => (ICommand?)d.GetValue(LostFocusCommandProperty);
    public static void SetLostFocusCommand(DependencyObject d, ICommand? v) => d.SetValue(LostFocusCommandProperty, v);

    private static void OnKeyCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        el.PreviewKeyDown -= OnPreviewKeyDown;
        el.PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var d = (DependencyObject)sender;
        var param = GetCommandParameter(d) ?? (d as FrameworkElement)?.DataContext;
        if (e.Key == Key.Enter && GetEnterCommand(d) is { } enter)
        {
            // Shift+Enter inserts a newline in multi-line boxes.
            if (sender is TextBox { AcceptsReturn: true } && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
            if (sender is TextBox tb) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (enter.CanExecute(param)) enter.Execute(param);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && GetEscapeCommand(d) is { } esc)
        {
            if (esc.CanExecute(param)) esc.Execute(param);
            e.Handled = true;
        }
    }

    private static void OnLostFocusCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        el.LostKeyboardFocus -= OnLostKeyboardFocus;
        el.LostKeyboardFocus += OnLostKeyboardFocus;
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var d = (DependencyObject)sender;
        // Ignore focus moving into a popup/context menu opened from within the element.
        if (e.NewFocus is DependencyObject nf && IsDescendant(d, nf)) return;
        if (sender is TextBox tb) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        var param = GetCommandParameter(d) ?? (d as FrameworkElement)?.DataContext;
        if (GetLostFocusCommand(d) is { } cmd && cmd.CanExecute(param)) cmd.Execute(param);
    }

    private static bool IsDescendant(DependencyObject parent, DependencyObject child)
    {
        var cur = child;
        while (cur != null)
        {
            if (cur == parent) return true;
            cur = cur is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur);
        }

        return false;
    }

    // ---------- Entrance animation ----------

    public static bool AnimationsEnabled { get; set; } = true;

    public static readonly DependencyProperty AnimateInProperty = DependencyProperty.RegisterAttached(
        "AnimateIn", typeof(string), typeof(Behaviors), new PropertyMetadata(null, OnAnimateInChanged));

    public static string? GetAnimateIn(DependencyObject d) => (string?)d.GetValue(AnimateInProperty);
    public static void SetAnimateIn(DependencyObject d, string? v) => d.SetValue(AnimateInProperty, v);

    private static void OnAnimateInChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe || e.NewValue is not string kind) return;
        fe.Loaded += (_, _) => AnimateIn(fe, kind);
    }

    /// <summary>kind: "fade", "rise" (fade + slide up), "pop" (fade + scale).</summary>
    public static void AnimateIn(FrameworkElement fe, string kind)
    {
        if (!AnimationsEnabled) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(kind == "pop" ? 200 : 260);
        fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

        if (kind == "rise")
        {
            var tt = new TranslateTransform(0, 10);
            fe.RenderTransform = tt;
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, duration) { EasingFunction = ease });
        }
        else if (kind == "pop")
        {
            var st = new ScaleTransform(0.96, 0.96);
            fe.RenderTransformOrigin = new Point(0.5, 0.5);
            fe.RenderTransform = st;
            var a = new DoubleAnimation(0.96, 1, duration) { EasingFunction = ease };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        else if (kind == "slide")
        {
            var tt = new TranslateTransform(24, 0);
            fe.RenderTransform = tt;
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(24, 0, duration) { EasingFunction = ease });
        }
    }

    // ---------- Popup toggles ----------

    /// <summary>Opens the named Popup (resolved from the element's namescope) when the button is clicked.</summary>
    public static readonly DependencyProperty OpensPopupProperty = DependencyProperty.RegisterAttached(
        "OpensPopup", typeof(Popup), typeof(Behaviors), new PropertyMetadata(null, OnOpensPopupChanged));

    public static Popup? GetOpensPopup(DependencyObject d) => (Popup?)d.GetValue(OpensPopupProperty);
    public static void SetOpensPopup(DependencyObject d, Popup? v) => d.SetValue(OpensPopupProperty, v);

    private static void OnOpensPopupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase b) return;
        b.Click -= OnOpenPopupClick;
        b.Click += OnOpenPopupClick;
    }

    private static void OnOpenPopupClick(object sender, RoutedEventArgs e)
    {
        if (GetOpensPopup((DependencyObject)sender) is { } popup)
        {
            popup.PlacementTarget ??= (UIElement)sender;
            popup.IsOpen = !popup.IsOpen;
        }
    }

    /// <summary>Opens a button's ContextMenu on left click, positioned below the button.</summary>
    public static readonly DependencyProperty ClickOpensContextMenuProperty = DependencyProperty.RegisterAttached(
        "ClickOpensContextMenu", typeof(bool), typeof(Behaviors), new PropertyMetadata(false, OnClickOpensContextMenuChanged));

    public static bool GetClickOpensContextMenu(DependencyObject d) => (bool)d.GetValue(ClickOpensContextMenuProperty);
    public static void SetClickOpensContextMenu(DependencyObject d, bool v) => d.SetValue(ClickOpensContextMenuProperty, v);

    private static void OnClickOpensContextMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase b) return;
        b.Click += (_, _) =>
        {
            if (b.ContextMenu == null) return;
            b.ContextMenu.DataContext = b.DataContext;
            b.ContextMenu.PlacementTarget = b;
            b.ContextMenu.Placement = PlacementMode.Bottom;
            b.ContextMenu.IsOpen = true;
        };
    }

    // ---------- Close parent popup on click ----------

    public static readonly DependencyProperty ClosesPopupProperty = DependencyProperty.RegisterAttached(
        "ClosesPopup", typeof(bool), typeof(Behaviors), new PropertyMetadata(false, OnClosesPopupChanged));

    public static bool GetClosesPopup(DependencyObject d) => (bool)d.GetValue(ClosesPopupProperty);
    public static void SetClosesPopup(DependencyObject d, bool v) => d.SetValue(ClosesPopupProperty, v);

    private static void OnClosesPopupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase b) return;
        b.Click += (_, _) =>
        {
            DependencyObject? cur = b;
            while (cur != null)
            {
                if (cur is Popup p)
                {
                    p.IsOpen = false;
                    return;
                }

                // Popup.Child's logical parent is the Popup itself; templated content only has visual parents.
                cur = LogicalTreeHelper.GetParent(cur) ?? (cur is Visual ? VisualTreeHelper.GetParent(cur) : null);
            }
        };
    }
}
