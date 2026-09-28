using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FlowBoard.Helpers;
using FlowBoard.Models;
using FlowBoard.Services;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

/// <summary>
/// Keyboard flow of the block editor (Enter splits, Backspace merges, arrows move between blocks, "/" menu)
/// and rich text: every block is a RichTextBox kept in sync with the block's formatted spans.
/// </summary>
public partial class NoteView : UserControl, Helpers.ICapturable
{
    private static readonly string[] TextColors = ["#9CA3AF", "#EF4444", "#F97316", "#EAB308", "#22C55E", "#14B8A6", "#3B82F6", "#8B5CF6", "#EC4899", "#A16207"];
    private static readonly string[] HighlightColors = ["#66FACC15", "#6622C55E", "#663B82F6", "#66A855F7", "#66EC4899", "#66EF4444", "#66F97316", "#6694A3B8"];

    private NoteViewModel? _vm;
    private readonly HashSet<RichTextBox> _loading = [];
    private RichTextBox? _active;
    private bool _pickerOpen;
    private bool _highlightMode;
    private string _lastHighlight = HighlightColors[0];

    // "@" link search state
    private RichTextBox? _mentionBox;
    private int _mentionStart = -1;

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
            // New pages start in the title.
            if (_vm == null) return;
            if (_vm.Page.Title.StartsWith("Untitled page", StringComparison.Ordinal) && _vm.Page.Blocks.All(b => b.Text.Length == 0))
            {
                TitleBox.Focus();
                TitleBox.SelectAll();
            }
        };
        Unloaded += (_, _) =>
        {
            SlashPopup.IsOpen = false;
            CloseMention();
            HideFormatBar();
        };
        Scroller.ScrollChanged += (_, _) => HideFormatBar();
    }

    // ================= editor <-> block sync =================

    private void OnEditorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RichTextBox box || box.DataContext is not NoteBlock block) return;
        DataObject.AddPastingHandler(box, OnPasting);
        box.PreviewTextInput -= OnEditorTextInput;
        box.PreviewTextInput += OnEditorTextInput;
        box.PreviewMouseLeftButtonUp -= OnEditorClick;
        box.PreviewMouseLeftButtonUp += OnEditorClick;
        block.PropertyChanged -= OnBlockPropertyChanged;
        block.PropertyChanged += OnBlockPropertyChanged;
        LoadInto(box, block);
    }

    private void OnEditorUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RichTextBox box || box.DataContext is not NoteBlock block) return;
        DataObject.RemovePastingHandler(box, OnPasting);
        box.PreviewTextInput -= OnEditorTextInput;
        box.PreviewMouseLeftButtonUp -= OnEditorClick;
        block.PropertyChanged -= OnBlockPropertyChanged;
        if (_active == box) _active = null;
    }

    private void OnBlockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not NoteBlock block) return;
        if (e.PropertyName is nameof(NoteBlock.ContentVersion) or nameof(NoteBlock.IsChecked) && EditorFor(block) is { } box)
        {
            // Reload after the current input event finishes (the change may come from inside TextChanged).
            Dispatcher.BeginInvoke(DispatcherPriority.Send, () => LoadInto(box, block));
        }
    }

    private void LoadInto(RichTextBox box, NoteBlock block)
    {
        // Skip if the editor already shows exactly this content.
        if (!_loading.Add(box)) return;
        try
        {
            var current = RichDoc.Read(box);
            var wanted = block.GetSpans();
            var same = RichText.PlainText(current) == block.Text && current.Count == RichText.Normalize(wanted).Count
                       && current.Zip(RichText.Normalize(wanted)).All(p => p.First.SameStyle(p.Second) && p.First.Text == p.Second.Text);
            if (!same || box.Document.Blocks.Count == 0)
            {
                var caret = box.IsKeyboardFocusWithin ? RichDoc.OffsetOf(box, box.CaretPosition) : -1;
                box.IsUndoEnabled = false;
                RichDoc.Load(box, wanted);
                box.IsUndoEnabled = true;
                if (caret >= 0) box.CaretPosition = RichDoc.PointerAt(box, Math.Min(caret, block.Text.Length));
            }

            // Ticked to-dos are struck through.
            if (box.Document.Blocks.FirstBlock is Paragraph p)
                p.TextDecorations = block.Type == BlockType.Todo && block.IsChecked ? TextDecorations.Strikethrough : null;
        }
        finally
        {
            _loading.Remove(box);
        }
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not RichTextBox box || _loading.Contains(box) || box.DataContext is not NoteBlock block) return;
        _loading.Add(box);
        try
        {
            block.SetSpans(RichDoc.Read(box), fromEditor: true);
        }
        finally
        {
            _loading.Remove(box);
        }

        UpdateMention(box, block);
    }

    // ================= "@" links inside text =================

    private void UpdateMention(RichTextBox box, NoteBlock block)
    {
        if (block.Type == BlockType.Code) return;
        var caret = RichDoc.OffsetOf(box, box.CaretPosition);
        var text = block.Text;
        if (_mentionBox == box && _mentionStart >= 0)
        {
            // Still typing the search after "@"?
            if (caret <= _mentionStart || _mentionStart >= text.Length || text[_mentionStart] != '@' || caret - _mentionStart > 40
                || text[(_mentionStart + 1)..Math.Min(caret, text.Length)].Contains('\n'))
            {
                CloseMention();
                return;
            }

            ShowMention(box, text[(_mentionStart + 1)..Math.Min(caret, text.Length)]);
            return;
        }

        // A fresh "@" at the start of a word opens the search.
        if (caret >= 1 && caret <= text.Length && text[caret - 1] == '@' && (caret == 1 || char.IsWhiteSpace(text[caret - 2])))
        {
            _mentionBox = box;
            _mentionStart = caret - 1;
            ShowMention(box, string.Empty);
        }
    }

    private void ShowMention(RichTextBox box, string query)
    {
        if (_vm == null) return;
        var hits = LinkResolver.Search(_vm.Main.Workspace, query, LinkResolver.All, [_vm.Page.Id], _vm.Main.CurrentBoard, 30)
            .Select(LinkPickerViewModel.FromHit).ToList();
        MentionList.ItemsSource = hits;
        MentionList.SelectedIndex = hits.Count > 0 ? 0 : -1;
        var rect = box.CaretPosition.GetCharacterRect(LogicalDirection.Backward);
        MentionPopup.PlacementTarget = box;
        MentionPopup.HorizontalOffset = Math.Max(0, rect.Left - 20);
        MentionPopup.VerticalOffset = rect.Bottom + 4;
        MentionPopup.IsOpen = hits.Count > 0 || query.Length == 0;
    }

    private void CloseMention()
    {
        MentionPopup.IsOpen = false;
        _mentionBox = null;
        _mentionStart = -1;
    }

    private void InsertMention(LinkOption option)
    {
        if (_mentionBox is not { } box || box.DataContext is not NoteBlock block || _mentionStart < 0) return;
        _vm?.Checkpoint();
        var start = RichDoc.PointerAt(box, _mentionStart);
        // Replace "@query" with the link, followed by a space so typing continues as normal text.
        new TextRange(start, box.CaretPosition).Text = string.Empty;
        var at = RichDoc.PointerAt(box, _mentionStart);
        var run = RichDoc.MakeLinkRun(option.Title, option.Kind, option.Id, at);
        var space = new Run(" ", run.ElementEnd);
        box.CaretPosition = space.ContentEnd;
        CloseMention();
        Sync(box, block);
    }

    private void OnMentionClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject) is { } d && ItemsControl.ContainerFromElement(MentionList, d) is ListBoxItem { DataContext: LinkOption option })
        {
            InsertMention(option);
            _mentionBox?.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Links behave as one piece: typing next to one never extends it.</summary>
    private void OnEditorTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not RichTextBox box || !box.Selection.IsEmpty || e.Text.Length == 0) return;
        var pos = box.CaretPosition;
        if (pos.Parent is not Run run || !RichDoc.TryParseLink(run.Tag, out _, out _)) return;
        TextPointer insertAt;
        if (pos.CompareTo(run.ContentEnd) >= 0) insertAt = run.ElementEnd;
        else if (pos.CompareTo(run.ContentStart) <= 0) insertAt = run.ElementStart;
        else
        {
            e.Handled = true; // no typing inside a link
            return;
        }

        var plain = new Run(e.Text, insertAt);
        box.CaretPosition = plain.ContentEnd;
        e.Handled = true;
    }

    /// <summary>Clicking a link opens what it points to.</summary>
    private void OnEditorClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null || sender is not RichTextBox box || !box.Selection.IsEmpty) return;
        var pos = box.GetPositionFromPoint(e.GetPosition(box), false);
        if (pos?.Parent is Run run && RichDoc.TryParseLink(run.Tag, out var kind, out var id))
        {
            e.Handled = true;
            _vm.Main.OpenTarget(kind, id);
        }
    }

    /// <summary>Pasted text arrives as plain text (formatting from web pages or Word would clash with the theme).</summary>
    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText)) return;
        var text = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
        var clean = new DataObject();
        clean.SetData(DataFormats.UnicodeText, text.Replace("\r\n", "\n").Replace('\r', '\n'));
        e.DataObject = clean;
        e.FormatToApply = DataFormats.UnicodeText;
    }

    private void OnEditorFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_vm != null && sender is RichTextBox { DataContext: NoteBlock b }) _vm.LastFocused = b;
    }

    // ================= focus & slash menu =================

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NoteViewModel.IsSlashOpen) || _vm == null) return;
        if (!_vm.IsSlashOpen)
        {
            SlashPopup.IsOpen = false;
            return;
        }

        // A block that was just added has no editor yet: wait for layout.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_vm is { IsSlashOpen: true, SlashBlock: { } b } && EditorFor(b) is { } box)
            {
                SlashPopup.PlacementTarget = box;
                SlashPopup.IsOpen = true;
            }
        });
    }

    private void OnBlocksLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!MentionPopup.IsMouseOver && e.NewFocus != _mentionBox) CloseMention();
        if (!FormatBar.IsMouseOver && !ColorMenu.IsMouseOver && !_pickerOpen && e.NewFocus is not RichTextBox) HideFormatBar();

        // Clicking an entry in the "/" menu must not close it first.
        if (_vm is not { IsSlashOpen: true } || SlashPopup.IsMouseOver) return;
        if ((e.NewFocus as FrameworkElement)?.DataContext == _vm.SlashBlock) return; // focus is moving into the "/" block
        _vm.CloseSlash();
    }

    private void OnFocusRequested(object? sender, (NoteBlock Block, int Caret) e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (e.Block.Type == BlockType.Image)
            {
                if (FindNamed<TextBox>(Container(e.Block), "Caption") is { } caption)
                {
                    caption.Focus();
                    caption.CaretIndex = caption.Text.Length;
                }

                return;
            }

            if (EditorFor(e.Block) is not { } box)
            {
                if (Container(e.Block) is FrameworkElement fe) fe.BringIntoView();
                return;
            }

            LoadInto(box, e.Block);
            box.Focus();
            box.CaretPosition = e.Caret < 0 ? box.Document.ContentEnd : RichDoc.PointerAt(box, Math.Min(e.Caret, e.Block.Text.Length));
            box.BringIntoView();
        });

    private DependencyObject? Container(NoteBlock block) => BlocksHost.ItemContainerGenerator.ContainerFromItem(block) as DependencyObject;

    private RichTextBox? EditorFor(NoteBlock block) =>
        Container(block) is { } c && NoteMarkdown.HasText(block.Type) ? FindNamed<RichTextBox>(c, "Editor") : null;

    private static T? FindNamed<T>(DependencyObject? root, string name) where T : FrameworkElement
    {
        if (root == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && t.Name == name) return t;
            if (FindNamed<T>(child, name) is { } found) return found;
        }

        return null;
    }

    // ================= keyboard =================

    private void OnBlockKey(object sender, KeyEventArgs e)
    {
        if (_vm == null) return;
        var mods = Keyboard.Modifiers;
        var ctrl = mods.HasFlag(ModifierKeys.Control);
        var shift = mods.HasFlag(ModifierKeys.Shift);
        var alt = mods.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Image captions are plain text boxes.
        if (e.OriginalSource is TextBox { Name: "Caption", DataContext: NoteBlock cap } tb)
        {
            if (key == Key.Enter)
            {
                _vm.Enter(cap, cap.Text.Length);
                e.Handled = true;
            }
            else if (key == Key.Back && tb.CaretIndex == 0 && tb.SelectionLength == 0 && cap.Text.Length == 0)
            {
                _vm.DeleteBlockCommand.Execute(cap);
                e.Handled = true;
            }

            return;
        }

        if (e.OriginalSource is not RichTextBox { DataContext: NoteBlock block } box) return;

        // ----- "@" link search -----
        if (MentionPopup.IsOpen && _mentionBox == box)
        {
            var list = MentionList.ItemsSource as IList<LinkOption>;
            switch (key)
            {
                case Key.Down when list is { Count: > 0 }:
                    MentionList.SelectedIndex = (MentionList.SelectedIndex + 1) % list.Count;
                    MentionList.ScrollIntoView(MentionList.SelectedItem);
                    e.Handled = true;
                    return;
                case Key.Up when list is { Count: > 0 }:
                    MentionList.SelectedIndex = (MentionList.SelectedIndex - 1 + list.Count) % list.Count;
                    MentionList.ScrollIntoView(MentionList.SelectedItem);
                    e.Handled = true;
                    return;
                case Key.Enter or Key.Tab when MentionList.SelectedItem is LinkOption option:
                    InsertMention(option);
                    e.Handled = true;
                    return;
                case Key.Escape:
                    CloseMention();
                    e.Handled = true;
                    return;
            }
        }

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

        var caret = RichDoc.OffsetOf(box, box.CaretPosition);
        var noSelection = box.Selection.IsEmpty;
        switch (key)
        {
            case Key.Enter when ctrl && block.Type == BlockType.Todo:
                block.IsChecked = !block.IsChecked;
                break;
            case Key.Enter when ctrl || (!shift && block.Type != BlockType.Code):
                if (!noSelection) box.Selection.Text = string.Empty;
                Sync(box, block);
                _vm.Enter(block, ctrl ? block.Text.Length : RichDoc.OffsetOf(box, box.CaretPosition));
                break;
            case Key.Enter when shift || block.Type == BlockType.Code:
                // New line inside the block.
                box.CaretPosition = box.CaretPosition.InsertLineBreak();
                break;
            case Key.Back when caret == 0 && noSelection:
                _vm.BackspaceAtStart(block);
                break;
            case Key.Delete when caret >= block.Text.Length && noSelection:
                _vm.DeleteAtEnd(block);
                break;
            case Key.Tab when !ctrl:
                if (block.Type == BlockType.Code && !shift) box.CaretPosition.InsertTextInRun("    ");
                else _vm.Indent(block, shift ? -1 : 1);
                break;
            case Key.Up when alt:
                _vm.MoveUpCommand.Execute(block);
                break;
            case Key.Down when alt:
                _vm.MoveDownCommand.Execute(block);
                break;
            case Key.Up when !shift && RichDoc.IsOnFirstLine(box):
                _vm.FocusSibling(block, -1, caret);
                break;
            case Key.Down when !shift && RichDoc.IsOnLastLine(box):
                var lineStart = box.CaretPosition.GetLineStartPosition(0) ?? box.Document.ContentStart;
                _vm.FocusSibling(block, 1, caret - RichDoc.OffsetOf(box, lineStart));
                break;
            case Key.Left when caret == 0 && noSelection && !shift:
                _vm.FocusSibling(block, -1, -1);
                break;
            case Key.Right when caret >= block.Text.Length && noSelection && !shift:
                _vm.FocusSibling(block, 1, 0);
                break;
            case Key.V when ctrl && !shift:
                if (!_vm.PasteImage(block)) return; // not an image: normal (plain text) paste
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
            case Key.E when ctrl:
                ToggleCode(box);
                break;
            case Key.X when ctrl && shift:
                ToggleDecoration(box, TextDecorationLocation.Strikethrough);
                break;
            case Key.H when ctrl && shift:
                ApplyHighlight(box, _lastHighlight);
                break;
            case Key.U when ctrl:
                // The built-in toggle replaces strikethrough; keep both.
                ToggleDecoration(box, TextDecorationLocation.Underline);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private static void Sync(RichTextBox box, NoteBlock block) => block.SetSpans(RichDoc.Read(box), fromEditor: true);

    // ================= formatting bar =================

    private void OnEditorSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RichTextBox box || !box.IsKeyboardFocusWithin) return;
        if (box.Selection.IsEmpty)
        {
            if (!_pickerOpen && !ColorMenu.IsMouseOver) HideFormatBar();
            return;
        }

        _active = box;
        var rect = box.Selection.Start.GetCharacterRect(LogicalDirection.Forward);
        if (FormatBar.PlacementTarget != box)
        {
            FormatBar.IsOpen = false;
            FormatBar.PlacementTarget = box;
        }

        FormatBar.HorizontalOffset = Math.Max(0, rect.Left - 8);
        FormatBar.VerticalOffset = rect.Top - 50;
        if (!FormatBar.IsOpen) FormatBar.IsOpen = true;
    }

    private void HideFormatBar()
    {
        FormatBar.IsOpen = false;
        ColorMenu.IsOpen = false;
    }

    private RichTextBox? Target => _active is { } b && !b.Selection.IsEmpty ? b : null;

    private void AfterFormat(RichTextBox box)
    {
        if (box.DataContext is NoteBlock block) Sync(box, block);
    }

    private void OnBold(object sender, RoutedEventArgs e)
    {
        if (Target is not { } box) return;
        _vm?.Checkpoint();
        var current = box.Selection.GetPropertyValue(TextElement.FontWeightProperty);
        box.Selection.ApplyPropertyValue(TextElement.FontWeightProperty,
            current is FontWeight fw && fw.ToOpenTypeWeight() >= 600 ? FontWeights.Normal : FontWeights.Bold);
        AfterFormat(box);
    }

    private void OnItalic(object sender, RoutedEventArgs e)
    {
        if (Target is not { } box) return;
        _vm?.Checkpoint();
        var current = box.Selection.GetPropertyValue(TextElement.FontStyleProperty);
        box.Selection.ApplyPropertyValue(TextElement.FontStyleProperty, current is FontStyle fs && fs == FontStyles.Italic ? FontStyles.Normal : FontStyles.Italic);
        AfterFormat(box);
    }

    private void OnUnderline(object sender, RoutedEventArgs e)
    {
        if (Target is { } box) ToggleDecoration(box, TextDecorationLocation.Underline);
    }

    private void OnStrike(object sender, RoutedEventArgs e)
    {
        if (Target is { } box) ToggleDecoration(box, TextDecorationLocation.Strikethrough);
    }

    private void OnCode(object sender, RoutedEventArgs e)
    {
        if (Target is { } box) ToggleCode(box);
    }

    private void ToggleDecoration(RichTextBox box, TextDecorationLocation where)
    {
        if (box.Selection.IsEmpty) return;
        _vm?.Checkpoint();
        var current = box.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        var has = current?.Any(d => d.Location == where) == true;
        var next = new TextDecorationCollection(current?.Where(d => d.Location != where) ?? []);
        if (!has) next.Add(where == TextDecorationLocation.Underline ? TextDecorations.Underline : TextDecorations.Strikethrough);
        box.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, next);
        AfterFormat(box);
    }

    private void ToggleCode(RichTextBox box)
    {
        if (box.Selection.IsEmpty) return;
        _vm?.Checkpoint();
        var isCode = box.Selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily ff && ff.Source.Contains("Mono", StringComparison.OrdinalIgnoreCase);
        box.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, isCode ? (FontFamily)FindResource("Fb.Font") : RichDoc.CodeFont);
        AfterFormat(box);
    }

    private void OnClearFormat(object sender, RoutedEventArgs e)
    {
        if (Target is not { } box) return;
        _vm?.Checkpoint();
        box.Selection.ClearAllProperties();
        AfterFormat(box);
    }

    private static string? Hex(Brush b) => b is SolidColorBrush s ? $"#{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}" : null;

    private void OnTextColorMenu(object sender, RoutedEventArgs e) => OpenColorMenu(highlight: false);

    private void OnHighlightMenu(object sender, RoutedEventArgs e) => OpenColorMenu(highlight: true);

    private void OpenColorMenu(bool highlight)
    {
        _highlightMode = highlight;
        ColorMenuTitle.Text = highlight ? "HIGHLIGHT" : "TEXT COLOR";
        ColorSwatches.ItemsSource = highlight ? HighlightColors : TextColors;
        ColorMenu.IsOpen = true;
    }

    private void OnSwatchPicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string hex }) ApplyColor(hex);
    }

    private void OnCustomColor(object? sender, string hex)
    {
        // Custom highlights are made see-through so text stays readable in both themes.
        ApplyColor(_highlightMode ? "#66" + hex.TrimStart('#') : hex);
    }

    private void OnResetColor(object sender, RoutedEventArgs e) => ApplyColor(null);

    private void ApplyColor(string? hex)
    {
        ColorMenu.IsOpen = false;
        if (_active is not { } box || box.Selection.IsEmpty) return;
        if (_highlightMode)
        {
            ApplyHighlight(box, hex);
            return;
        }

        _vm?.Checkpoint();

        if (hex != null && ThemeService.TryParseColor(hex, out var c)) box.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(c));
        else box.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, box.Foreground);
        AfterFormat(box);
        if (hex == null && box.DataContext is NoteBlock block)
            block.SetSpans(block.GetSpans().Select(s => { if (s.Color == Hex(box.Foreground)) s.Color = null; return s; }));
    }

    private void ApplyHighlight(RichTextBox box, string? hex)
    {
        if (box.Selection.IsEmpty) return;
        _vm?.Checkpoint();
        if (hex != null && ThemeService.TryParseColor(hex, out var c))
        {
            _lastHighlight = hex;
            box.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(c));
        }
        else
        {
            box.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
        }

        AfterFormat(box);
    }

    private void OnPickerOpened(object? sender, EventArgs e) => _pickerOpen = true;

    private void OnPickerClosed(object? sender, EventArgs e) => _pickerOpen = false;

    // ================= misc =================

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

    // ----- high-res screenshot -----
    FrameworkElement Helpers.ICapturable.CaptureElement => PageContent;
    Rect? Helpers.ICapturable.CaptureArea => null;
    System.Windows.Media.Brush? Helpers.ICapturable.CaptureBackground => Helpers.CaptureHelpers.DocWallpaper();
    string Helpers.ICapturable.CaptureName => _vm?.Page.Title ?? "Page";
}
