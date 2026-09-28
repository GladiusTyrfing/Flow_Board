using System.Windows.Controls;
using System.Windows.Input;
using FlowBoard.Services;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>Records a key combination for a global hotkey.</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || DataContext is not SettingsViewModel vm) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Let Tab move focus and Esc close the dialog as usual.
        if (key is Key.Tab || (key is Key.Escape && Keyboard.Modifiers == ModifierKeys.None)) return;
        e.Handled = true;

        string? text;
        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            text = string.Empty;
        }
        else
        {
            text = GlobalHotkeyService.Format(Keyboard.Modifiers, key);
            if (text == null) return; // only modifiers pressed so far
            if (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift)
            {
                vm.Main.ShowToast("Global hotkeys need Ctrl, Alt or Win (e.g. Ctrl+Alt+Space).", isError: true);
                return;
            }
        }

        if ((string)box.Tag == "QuickAdd") vm.Settings.QuickAddHotkey = text;
        else vm.Settings.ShowHideHotkey = text;
        box.Text = text;
    }
}
