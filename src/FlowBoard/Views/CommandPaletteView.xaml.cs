using System.Windows.Controls;
using System.Windows.Input;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
    }

    private CommandPaletteViewModel? Vm => DataContext as CommandPaletteViewModel;

    private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        switch (e.Key)
        {
            case Key.Down:
                Vm.MoveSelectionCommand.Execute("down");
                e.Handled = true;
                break;
            case Key.Up:
                Vm.MoveSelectionCommand.Execute("up");
                e.Handled = true;
                break;
            case Key.Enter:
                Vm.RunCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Results.SelectedItem != null) Results.ScrollIntoView(Results.SelectedItem);
    }

    private void Results_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Results.SelectedItem is PaletteItem item) Vm?.RunCommand.Execute(item);
    }
}
