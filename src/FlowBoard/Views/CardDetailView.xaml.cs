using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FlowBoard.Models;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

public partial class CardDetailView : UserControl
{
    public CardDetailView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // Hover hotkeys (D, L) ask for a popup to be opened right away.
            if (Vm?.PendingPopup is { } popup)
            {
                Vm.PendingPopup = null;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => OpenPopup(popup));
            }
            else
            {
                Focus();
            }
        };
    }

    private void OpenPopup(string name)
    {
        var popup = name switch
        {
            "dates" => DatesPopup,
            "labels" => LabelsPopup,
            "priority" => PriorityPopup,
            "cover" => CoverPopup,
            "move" => MovePopup,
            _ => null,
        };
        if (popup == null || (popup == MovePopup && MoveBtn.Visibility != Visibility.Visible)) return;
        popup.IsOpen = true;
    }

    private CardDetailViewModel? Vm => DataContext as CardDetailViewModel;

    /// <summary>Ctrl+V pastes images/files as attachments (text still pastes normally inside text boxes).</summary>
    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        if (HandleCardHotkey(e))
        {
            e.Handled = true;
            return;
        }

        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
        bool inTextBox = Keyboard.FocusedElement is TextBox;
        bool hasAttachable = Clipboard.ContainsImage() || Clipboard.ContainsFileDropList();
        if (!inTextBox || hasAttachable)
        {
            Vm.PasteFromClipboard();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Single-key shortcuts while the card is open and you're not typing:
    /// L labels · D dates · P priority · K checklist · A attach · R record/stop · T timer · F focus · M move · B cover · X complete.
    /// Ctrl+D duplicate · Ctrl+Shift+C archive · Ctrl+Delete delete.
    /// </summary>
    private bool HandleCardHotkey(KeyEventArgs e)
    {
        var vm = Vm!;
        var mods = Keyboard.Modifiers;
        var typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or ComboBox { IsEditable: true };

        if (mods == ModifierKeys.Control && e.Key == Key.D) { vm.DuplicateCommand.Execute(null); return true; }
        if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C) { vm.ArchiveCommand.Execute(null); return true; }
        if (mods == ModifierKeys.Control && e.Key == Key.Delete) { vm.DeleteCommand.Execute(null); return true; }
        if (mods == ModifierKeys.Control && e.Key == Key.Enter && typing)
        {
            // Ctrl+Enter: finish editing and leave the text box.
            Keyboard.ClearFocus();
            Focus();
            return true;
        }

        if (typing || mods != ModifierKeys.None) return false;
        switch (e.Key)
        {
            case Key.L: OpenPopup("labels"); return true;
            case Key.D: OpenPopup("dates"); return true;
            case Key.P: OpenPopup("priority"); return true;
            case Key.B: OpenPopup("cover"); return true;
            case Key.M: OpenPopup("move"); return true;
            case Key.K: vm.AddChecklistCommand.Execute(null); return true;
            case Key.A: vm.AddFilesCommand.Execute(null); return true;
            case Key.T: vm.ToggleTimerCommand.Execute(null); return true;
            case Key.F: vm.StartFocusCommand.Execute(null); return true;
            case Key.X: vm.ToggleCompleteCommand.Execute(null); return true;
            case Key.R:
                if (vm.Recorder.IsRecording) vm.StopRecordingCommand.Execute(null);
                else vm.StartRecordingCommand.Execute(null);
                return true;
        }

        return false;
    }

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            if (Vm != null) Vm.IsDragOver = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void Card_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between child elements; only reset when really leaving the card.
        var pos = e.GetPosition(Card);
        if (pos.X <= 0 || pos.Y <= 0 || pos.X >= Card.ActualWidth || pos.Y >= Card.ActualHeight)
            if (Vm != null) Vm.IsDragOver = false;
    }

    private void Card_Drop(object sender, DragEventArgs e)
    {
        if (Vm == null) return;
        Vm.IsDragOver = false;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) Vm.HandleFileDrop(files);
        e.Handled = true;
    }

    private void Seek_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Attachment a } fe && fe.ActualWidth > 0 && Vm != null)
        {
            if (!a.IsPlaying && a.PlaybackProgress <= 0) Vm.TogglePlayCommand.Execute(a);
            Vm.Seek(a, e.GetPosition(fe).X / fe.ActualWidth);
        }
    }

    private void LabelChip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LabelOption option }) option.IsSelected = !option.IsSelected;
    }
}
