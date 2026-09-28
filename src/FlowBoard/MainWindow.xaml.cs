using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using FlowBoard.Helpers;
using FlowBoard.Services;
using FlowBoard.ViewModels;
using Wpf.Ui.Controls;

namespace FlowBoard;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        ApplySavedBounds(vm);

        vm.ViewChanged += (_, _) => Behaviors.AnimateIn(ViewHost, "rise");
        vm.FocusFilterRequested += (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                FilterText.Focus();
                System.Windows.Input.Keyboard.Focus(FilterText);
            });
        ThemeService.ThemeApplied += (_, _) => UpdateThemeIcon();
        UpdateThemeIcon();
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        _vm.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Models.AppSettings.SidebarVisible)) UpdateResponsiveLayout();
        };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.NarrowSidebarOpen)) UpdateResponsiveLayout();
        };

        // Board styles (preset + transparency) are applied to the board area only.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentBoard)) ApplyBoardTheme();
        };
        ThemeService.ThemeApplied += (_, _) => ApplyBoardTheme();
        ApplyBoardTheme();
    }

    /// <summary>Adapts the title bar and board header to the window width so nothing gets clipped.</summary>
    private void UpdateResponsiveLayout()
    {
        var w = ActualWidth;
        if (w <= 0) return;
        // Title bar: small search box that never pushes into the buttons on the right.
        SearchButton.Width = Math.Clamp(w - 900, 150, 300);

        // Below ~1100px the sidebar hides itself (the header button still opens it).
        _vm.IsNarrow = w < 1100;

        // Board header: icons only when space is tight; the board name trims with "…".
        var boardWidth = w - 20 - (_vm.IsSidebarShown ? 274 : 0);
        _vm.IsCompact = boardWidth < 1000;
        var reserved = _vm.IsCompact ? 380 : 620;
        _vm.BoardNameMaxWidth = Math.Clamp(boardWidth - reserved, 80, 420);
    }

    private Models.Board? _themedBoard;

    private void ApplyBoardTheme()
    {
        if (_themedBoard != null) _themedBoard.PropertyChanged -= OnBoardPropertyChanged;
        _themedBoard = _vm.CurrentBoard;
        if (_themedBoard != null) _themedBoard.PropertyChanged += OnBoardPropertyChanged;
        BoardThemeService.Apply(BoardArea, _themedBoard);
    }

    private bool _boardThemePending;

    private void OnBoardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Models.Board.Theme) or nameof(Models.Board.ListOpacity) or nameof(Models.Board.CardOpacity)
            or nameof(Models.Board.CornerRadius))) return;

        // Sliders fire many changes per second: coalesce them into one restyle after rendering.
        if (_boardThemePending) return;
        _boardThemePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _boardThemePending = false;
            BoardThemeService.Apply(BoardArea, _themedBoard);
        });
    }

    /// <summary>Routes hotkeys that plain KeyBindings can't express (single letters, hover-card actions).</summary>
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var focused = System.Windows.Input.Keyboard.FocusedElement;
        var isTyping = focused is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.PasswordBox
                       || focused is System.Windows.Controls.ComboBox { IsEditable: true };
        if (_vm.HandleKey(key, System.Windows.Input.Keyboard.Modifiers, isTyping)) e.Handled = true;
    }

    private void UpdateThemeIcon() =>
        ThemeIcon.Symbol = ThemeService.IsDark ? SymbolRegular.WeatherSunny24 : SymbolRegular.WeatherMoon24;

    private void ApplySavedBounds(MainViewModel vm)
    {
        var s = vm.Settings;
        if (s.WindowWidth > 400 && s.WindowHeight > 300)
        {
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }

        // Only restore the position when it is still on a visible screen area.
        if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop)
            && s.WindowLeft >= SystemParameters.VirtualScreenLeft - 50
            && s.WindowTop >= SystemParameters.VirtualScreenTop - 50
            && s.WindowLeft + 200 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && s.WindowTop + 100 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WindowLeft;
            Top = s.WindowTop;
        }

        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveBounds()
    {
        var s = _vm.Settings;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (b.Width > 0 && b.Height > 0)
        {
            s.WindowLeft = b.Left;
            s.WindowTop = b.Top;
            s.WindowWidth = b.Width;
            s.WindowHeight = b.Height;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveBounds();
        if (App.IsExiting) return;

        if (_vm.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            App.Current.OnHiddenToTray();
            return;
        }

        App.Current.ExitFromWindowClosing();
    }
}
