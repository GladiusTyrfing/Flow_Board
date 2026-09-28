using System.Windows.Controls;
using System.Windows.Input;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

public partial class LinkPickerView : UserControl
{
    public LinkPickerView()
    {
        InitializeComponent();
    }

    private LinkPickerViewModel? Vm => DataContext as LinkPickerViewModel;

    private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        switch (e.Key)
        {
            case Key.Down:
                Vm.MoveSelectionCommand.Execute("down");
                break;
            case Key.Up:
                Vm.MoveSelectionCommand.Execute("up");
                break;
            case Key.Enter:
                Vm.ChooseCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Results.SelectedItem != null) Results.ScrollIntoView(Results.SelectedItem);
    }

    private void Results_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Results.SelectedItem is LinkOption option) Vm?.ChooseCommand.Execute(option);
    }
}
