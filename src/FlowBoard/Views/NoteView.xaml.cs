using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FlowBoard.Models;
using FlowBoard.Services;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

/// <summary>Keyboard flow of the block editor: Enter splits, Backspace merges, arrows move between blocks, "/" menu.</summary>
public partial class NoteView : UserControl
{
    private NoteViewModel? _vm;

    public NoteView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.FocusRequested -= OnFocusRequested;
                _vm.PropertyChanged -= OnVmChanged;
            }

            _vm = DataContext as NoteViewModel;
            if (_vm == null) return;
            _vm.FocusRequested += OnFocusRequested;
            _vm.PropertyChanged += OnVmChanged;
        };
        Loaded += (_, _) =>
        {
            // New pages start in the title; existing ones at the end of the first line.
            if (_vm == null) return;
            if (_vm.Page.Title.StartsWith("Untitled page", StringComparison.Ordinal) && _vm.Page.Blocks.All(b => b.Text.Length == 0))
            {
                TitleBox.Focus();
                TitleBox.SelectAll();
            }
        };
        Unloaded += (_, _) => SlashPopup.IsOpen = false;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NoteViewModel.IsSlashOpen) || _vm == null) return;
        if (!_vm.IsSlashOpen)
        {
            SlashPopup.IsOpen = false;
            return;
        }

        // A block that was just added has no text box yet: wait for layout.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_vm is { IsSlashOpen: true, SlashBlock: { } b } && EditorFor(b) is { } tb)
            {
                SlashPopup.PlacementTarget = tb;
                SlashPopup.IsOpen = true;
            }
        });
    }

    private void OnBlocksLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Clicking an entry in the "/" menu must not close it first.
        if (_vm is not { IsSlashOpen: true } || SlashPopup.IsMouseOver) return;
        if ((e.NewFocus as FrameworkElement)?.DataContext == _vm.SlashBlock) return; // focus is moving into the "/" block
        _vm.CloseSlash();
    }

    private void OnFocusRequested(object? sender, (NoteBlock Block, int Caret) e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (EditorFor(e.Block) is not { } tb) return;
            tb.Focus();
            tb.CaretIndex = e.Caret < 0 ? tb.Text.Length : Math.Min(e.Caret, tb.Text.Length);
            tb.BringIntoView();
        });

    /// <summary>The text box of a block (the caption box for images).</summary>
    private TextBox? EditorFor(NoteBlock block)
    {
        if (BlocksHost.ItemContainerGenerator.ContainerFromItem(block) is not DependencyObject container) return null;
        var name = block.Type == BlockType.Image ? "Caption" : "Editor";
        return FindNamed<TextBox>(container, name);
    }

    private static T? FindNamed<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && t.Name == name) return t;
            if (FindNamed<T>(child, name) is { } found) return found;
        }

        return null;
    }

    private void OnBlockKey(object sender, KeyEventArgs e)
    {
        if (_vm == null || e.OriginalSource is not TextBox { DataContext: NoteBlock block } tb) return;
        var mods = Keyboard.Modifiers;
        var ctrl = mods.HasFlag(ModifierKeys.Control);
        var shift = mods.HasFlag(ModifierKeys.Shift);
        var alt = mods.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var isCaption = tb.Name == "Caption";

        // ----- "/" menu -----
        if (_vm.IsSlashOpen && _vm.SlashBlock == block)
        {
            switch (key)
            {
                case Key.Down:
                    _vm.MoveSlash(1);
                    e.Handled = true;
                    return;
                case Key.Up:
                    _vm.MoveSlash(-1);
                    e.Handled = true;
                    return;
                case Key.Enter:
                case Key.Tab:
                    _ = _vm.ApplySlash(null);
                    e.Handled = true;
                    return;
                case Key.Escape:
                    _vm.CloseSlash();
                    e.Handled = true;
                    return;
            }
        }

        var caret = tb.CaretIndex;
        var noSelection = tb.SelectionLength == 0;
        switch (key)
        {
            case Key.Enter when ctrl && block.Type == BlockType.Todo:
                block.IsChecked = !block.IsChecked;
                break;
            case Key.Enter when ctrl || (!shift && block.Type != BlockType.Code):
                if (!noSelection) tb.SelectedText = string.Empty;
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                _vm.Enter(block, ctrl ? block.Text.Length : tb.CaretIndex);
                break;
            case Key.Back when caret == 0 && noSelection:
                if (isCaption)
                {
                    if (block.Text.Length == 0) _vm.DeleteBlockCommand.Execute(block);
                    else return;
                }
                else
                {
                    _vm.BackspaceAtStart(block);
                }

                break;
            case Key.Delete when caret == tb.Text.Length && noSelection && !isCaption:
                _vm.DeleteAtEnd(block);
                break;
            case Key.Tab when !ctrl:
                if (block.Type == BlockType.Code && !shift)
                {
                    tb.SelectedText = "    ";
                    tb.CaretIndex += 4;
                    tb.SelectionLength = 0;
                }
                else
                {
                    _vm.Indent(block, shift ? -1 : 1);
                }

                break;
            case Key.Up when alt:
                _vm.MoveUpCommand.Execute(block);
                break;
            case Key.Down when alt:
                _vm.MoveDownCommand.Execute(block);
                break;
            case Key.Up when !shift && tb.GetLineIndexFromCharacterIndex(caret) <= 0:
                _vm.FocusSibling(block, -1, caret);
                break;
            case Key.Down when !shift && IsOnLastLine(tb):
                _vm.FocusSibling(block, 1, caret - tb.GetCharacterIndexFromLineIndex(Math.Max(0, tb.GetLineIndexFromCharacterIndex(caret))));
                break;
            case Key.Left when caret == 0 && noSelection && !shift:
                _vm.FocusSibling(block, -1, -1);
                break;
            case Key.Right when caret == tb.Text.Length && noSelection && !shift:
                _vm.FocusSibling(block, 1, 0);
                break;
            case Key.V when ctrl && !isCaption:
                if (!_vm.PasteImage(block)) return;
                break;
            case Key.D when ctrl:
                _vm.DuplicateBlockCommand.Execute(block);
                break;
            case Key.K when ctrl && shift:
                _vm.DeleteBlockCommand.Execute(block);
                break;
            case Key.D1 or Key.D2 or Key.D3 when ctrl && alt:
                _ = _vm.TurnInto(block, key == Key.D1 ? BlockType.Heading1 : key == Key.D2 ? BlockType.Heading2 : BlockType.Heading3);
                break;
            case Key.D0 when ctrl && alt:
                _ = _vm.TurnInto(block, BlockType.Paragraph);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private static bool IsOnLastLine(TextBox tb)
    {
        var line = tb.GetLineIndexFromCharacterIndex(tb.CaretIndex);
        return line < 0 || line >= tb.LineCount - 1;
    }

    private void OnSlashClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null) return;
        if ((e.OriginalSource as DependencyObject) is { } d && ItemsControl.ContainerFromElement(SlashList, d) is ListBoxItem { DataContext: SlashOption option })
        {
            _ = _vm.ApplySlash(option);
            e.Handled = true;
        }
    }

    private void OnTurnInto(object sender, RoutedEventArgs e)
    {
        if (_vm != null && sender is MenuItem { Tag: string type, DataContext: NoteBlock block } && Enum.TryParse<BlockType>(type, out var t))
            _ = _vm.TurnInto(block, t);
    }

    private void OnTailClick(object sender, MouseButtonEventArgs e)
    {
        _vm?.AddAtEndCommand.Execute(null);
        e.Handled = true;
    }

    private void OnIconClick(object sender, RoutedEventArgs e) => IconPopup.IsOpen = true;

    private void OnIconPicked(object sender, RoutedEventArgs e) => IconPopup.IsOpen = false;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        // Only take over file drags; text drags between blocks keep working.
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_vm == null || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        NoteBlock? at = null;
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(BlocksHost, d) is FrameworkElement { DataContext: NoteBlock b }) at = b;
        _vm.DropImages(files.Where(MediaStore.IsImageFile), at);
        e.Handled = true;
    }
}
