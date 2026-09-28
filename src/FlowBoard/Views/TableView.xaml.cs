using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

public partial class TableView : UserControl, Helpers.ICapturable
{
    public TableView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Table.PropertyChanged -= OnTablePropertyChanged;
                vm.Table.PropertyChanged += OnTablePropertyChanged;
                UpdateBoardColumn(vm.Table.AllBoards);
            }
        };
    }

    private void OnTablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableViewModel.AllBoards) && sender is TableViewModel t) UpdateBoardColumn(t.AllBoards);
    }

    private void UpdateBoardColumn(bool show) => BoardColumn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Ignore double-clicks on headers or the empty area.
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(Grid, d) is DataGridRow { Item: TableRow row })
        {
            MainViewModel.Instance.OpenCard(row.Card);
            e.Handled = true;
        }
    }

    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Grid.SelectedItem is TableRow row && !Grid.IsKeyboardFocusWithin.Equals(false))
        {
            if (Keyboard.FocusedElement is TextBox) return;
            MainViewModel.Instance.OpenCard(row.Card);
            e.Handled = true;
        }
    }

    // ----- high-res screenshot -----
    FrameworkElement Helpers.ICapturable.CaptureElement => this;
    Rect? Helpers.ICapturable.CaptureArea => null;
    System.Windows.Media.Brush? Helpers.ICapturable.CaptureBackground => null;
    string Helpers.ICapturable.CaptureName => ((DataContext as ViewModels.MainViewModel)?.CurrentBoard?.Name ?? "Board") + " table";
}
