using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;
using Microsoft.Win32;

namespace FlowBoard.ViewModels;

public sealed record SlashOption(string Title, string Hint, string Icon, BlockType Type, string Keywords);

/// <summary>A note page edited as blocks (Notion-style): Markdown shortcuts, "/" menu, to-dos, images and links.</summary>
public sealed partial class NoteViewModel : DocumentViewModel
{
    private bool _converting;

    public NoteViewModel(MainViewModel main, NotePage page) : base(main)
    {
        Page = page;
        if (Page.Blocks.Count == 0) Page.Blocks.Add(new NoteBlock());
        foreach (var b in Page.Blocks) b.PropertyChanged += OnBlockChanged;
        Page.Blocks.CollectionChanged += OnBlocksChanged;
        Page.PropertyChanged += OnPageChanged;
        Renumber();
    }

    public NotePage Page { get; }
    public override object Model => Page;
    public override ActiveView Kind => ActiveView.Note;

    /// <summary>Raised when a block's text box should take focus; caret -1 = end.</summary>
    public event EventHandler<(NoteBlock Block, int Caret)>? FocusRequested;

    public static readonly SlashOption[] AllSlashOptions =
    [
        new("Text", "Plain paragraph", "TextT24", BlockType.Paragraph, "paragraph text plain"),
        new("Heading 1", "Big section title", "TextHeader124", BlockType.Heading1, "h1 heading title"),
        new("Heading 2", "Medium title", "TextHeader224", BlockType.Heading2, "h2 heading subtitle"),
        new("Heading 3", "Small title", "TextHeader324", BlockType.Heading3, "h3 heading"),
        new("To-do", "Checkbox item", "CheckboxChecked24", BlockType.Todo, "todo task check checkbox"),
        new("Bulleted list", "Simple list", "TextBulletListLtr24", BlockType.Bullet, "bullet list ul"),
        new("Numbered list", "1. 2. 3.", "TextNumberListLtr24", BlockType.Numbered, "numbered ordered list ol"),
        new("Quote", "Highlighted quote", "TextQuote24", BlockType.Quote, "quote blockquote"),
        new("Callout", "Note that stands out", "Lightbulb24", BlockType.Callout, "callout info tip note"),
        new("Code", "Monospace block", "Code24", BlockType.Code, "code snippet"),
        new("Divider", "Horizontal line", "LineHorizontal124", BlockType.Divider, "divider line hr separator"),
        new("Image", "Upload or paste", "Image24", BlockType.Image, "image picture photo"),
        new("Link", "Card, board, storyboard, canvas or page", "Link24", BlockType.Link, "link mention card board page canvas storyboard"),
    ];

    public static readonly string[] Icons =
        ["DocumentText24", "Notebook24", "Lightbulb24", "Rocket24", "Star24", "Heart24", "Target24", "Book24", "Briefcase24", "Calendar24", "Code24", "People24", "Video24", "Games24", "MusicNote224", "Sparkle24"];

    public string[] PageIcons => Icons;

    public ObservableCollection<SlashOption> SlashOptions { get; } = [];
    [ObservableProperty] private NoteBlock? _slashBlock;
    [ObservableProperty] private SlashOption? _slashSelected;

    public bool IsSlashOpen => SlashBlock != null && SlashOptions.Count > 0;

    public string Stats
    {
        get
        {
            var words = Page.Blocks.Sum(b => b.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
            var todos = Page.Blocks.Count(b => b.Type == BlockType.Todo);
            var done = Page.Blocks.Count(b => b.Type == BlockType.Todo && b.IsChecked);
            var s = $"{words} words · {Math.Max(1, (int)Math.Ceiling(words / 220.0))} min read";
            if (todos > 0) s += $" · {done}/{todos} to-dos";
            return s + $" · edited {Page.UpdatedAt:d MMM HH:mm}";
        }
    }

    // ================= change tracking =================

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotePage.Title) or nameof(NotePage.Icon)) Touch();
    }

    private void OnBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (NoteBlock b in e.NewItems) { b.PropertyChanged -= OnBlockChanged; b.PropertyChanged += OnBlockChanged; }
        if (e.OldItems != null) foreach (NoteBlock b in e.OldItems) b.PropertyChanged -= OnBlockChanged;
        Renumber();
        Touch();
    }

    private void OnBlockChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not NoteBlock b) return;
        switch (e.PropertyName)
        {
            case nameof(NoteBlock.Text):
                if (!_converting) ApplyShortcut(b);
                UpdateSlash(b);
                Touch();
                break;
            case nameof(NoteBlock.Type):
            case nameof(NoteBlock.Indent):
                Renumber();
                Touch();
                break;
            case nameof(NoteBlock.IsChecked):
                Touch();
                break;
        }
    }

    private void Touch()
    {
        Page.UpdatedAt = DateTime.Now;
        OnPropertyChanged(nameof(Stats));
    }

    private void Renumber() => NoteMarkdown.Renumber(Page.Blocks);

    public override void OnDeactivated()
    {
        foreach (var b in Page.Blocks) b.PropertyChanged -= OnBlockChanged;
        Page.Blocks.CollectionChanged -= OnBlocksChanged;
        Page.PropertyChanged -= OnPageChanged;
        // Drop trailing empty paragraphs (but keep one block).
        while (Page.Blocks.Count > 1 && Page.Blocks[^1] is { Type: BlockType.Paragraph, Text: "" } && Page.Blocks[^2] is { Type: BlockType.Paragraph, Text: "" })
            Page.Blocks.RemoveAt(Page.Blocks.Count - 1);
        Main.RefreshDocSidebar();
    }

    protected override void Restore(string json)
    {
        var snap = JsonSerializer.Deserialize<NotePage>(json, Json.Options);
        if (snap == null) return;
        Page.Title = snap.Title;
        Page.Icon = snap.Icon;
        Page.Blocks.Clear();
        foreach (var b in snap.Blocks) Page.Blocks.Add(b);
        if (Page.Blocks.Count == 0) Page.Blocks.Add(new NoteBlock());
        Focus(Page.Blocks[0], -1);
    }

    private void Focus(NoteBlock b, int caret) => FocusRequested?.Invoke(this, (b, caret));

    // ================= Markdown shortcuts & slash menu =================

    private void ApplyShortcut(NoteBlock b)
    {
        if (b.Type is not (BlockType.Paragraph or BlockType.Bullet)) return;
        if (NoteMarkdown.DetectShortcut(b.Text) is not { } hit) return;
        if (b.Type == hit.Type) return;
        Checkpoint();
        _converting = true;
        b.Type = hit.Type;
        b.Text = hit.Text;
        _converting = false;
        if (hit.Type == BlockType.Divider)
        {
            var next = InsertAfter(b, BlockType.Paragraph);
            Focus(next, 0);
        }
        else
        {
            Focus(b, -1);
        }
    }

    private void UpdateSlash(NoteBlock b)
    {
        if (b.Type is BlockType.Code || !b.Text.StartsWith('/') || b.Text.Contains(' ') || b.Text.Contains('\n'))
        {
            if (SlashBlock == b) CloseSlash();
            return;
        }

        var q = b.Text[1..].Trim();
        SlashOptions.Clear();
        foreach (var o in AllSlashOptions)
            if (q.Length == 0 || o.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || o.Keywords.Contains(q, StringComparison.OrdinalIgnoreCase))
                SlashOptions.Add(o);
        SlashBlock = b;
        SlashSelected = SlashOptions.FirstOrDefault();
        OnPropertyChanged(nameof(IsSlashOpen));
    }

    public void CloseSlash()
    {
        SlashBlock = null;
        SlashOptions.Clear();
        OnPropertyChanged(nameof(IsSlashOpen));
    }

    public void MoveSlash(int delta)
    {
        if (SlashOptions.Count == 0) return;
        var i = SlashSelected == null ? 0 : SlashOptions.IndexOf(SlashSelected);
        SlashSelected = SlashOptions[(i + delta + SlashOptions.Count) % SlashOptions.Count];
    }

    [RelayCommand]
    public async Task ApplySlash(SlashOption? option)
    {
        option ??= SlashSelected;
        var b = SlashBlock;
        CloseSlash();
        if (option == null || b == null) return;
        _converting = true;
        b.Text = string.Empty;
        _converting = false;
        await TurnInto(b, option.Type);
    }

    // ================= block operations =================

    public NoteBlock InsertAfter(NoteBlock? after, BlockType type, string text = "")
    {
        var index = after == null ? Page.Blocks.Count : Page.Blocks.IndexOf(after) + 1;
        var b = new NoteBlock { Type = type, Text = text, Indent = after != null && NoteMarkdown.IsListType(type) ? after.Indent : 0 };
        Page.Blocks.Insert(index, b);
        return b;
    }

    /// <summary>Enter: split the block at the caret; lists continue, an empty list item ends the list.</summary>
    public void Enter(NoteBlock b, int caret)
    {
        Checkpoint();
        if (NoteMarkdown.IsListType(b.Type) && b.Text.Length == 0)
        {
            if (b.Indent > 0) b.Indent--;
            else b.Type = BlockType.Paragraph;
            Focus(b, 0);
            return;
        }

        caret = Math.Clamp(caret, 0, b.Text.Length);
        var rest = b.Text[caret..];
        _converting = true;
        b.Text = b.Text[..caret];
        _converting = false;
        var type = NoteMarkdown.IsListType(b.Type) || b.Type is BlockType.Quote or BlockType.Callout && rest.Length > 0 ? b.Type : BlockType.Paragraph;
        if (!NoteMarkdown.HasText(b.Type)) type = BlockType.Paragraph;
        var next = InsertAfter(b, type, rest);
        next.Indent = NoteMarkdown.IsListType(type) ? b.Indent : 0;
        Focus(next, 0);
    }

    /// <summary>Backspace at the very start: un-style, outdent, then merge into the previous block.</summary>
    public void BackspaceAtStart(NoteBlock b)
    {
        var i = Page.Blocks.IndexOf(b);
        if (b.Type != BlockType.Paragraph && NoteMarkdown.HasText(b.Type))
        {
            Checkpoint();
            b.Type = BlockType.Paragraph;
            Focus(b, 0);
            return;
        }

        if (b.Indent > 0)
        {
            b.Indent--;
            return;
        }

        if (i <= 0) return;
        Checkpoint();
        var prev = Page.Blocks[i - 1];
        if (!NoteMarkdown.HasText(prev.Type))
        {
            Page.Blocks.Remove(prev);
            Focus(b, 0);
            return;
        }

        var caret = prev.Text.Length;
        _converting = true;
        prev.Text += b.Text;
        _converting = false;
        Page.Blocks.Remove(b);
        Focus(prev, caret);
    }

    /// <summary>Delete at the very end: pull the next block's text up.</summary>
    public void DeleteAtEnd(NoteBlock b)
    {
        var i = Page.Blocks.IndexOf(b);
        if (i < 0 || i >= Page.Blocks.Count - 1) return;
        Checkpoint();
        var next = Page.Blocks[i + 1];
        var caret = b.Text.Length;
        if (NoteMarkdown.HasText(next.Type))
        {
            _converting = true;
            b.Text += next.Text;
            _converting = false;
        }

        Page.Blocks.Remove(next);
        Focus(b, caret);
    }

    public void Indent(NoteBlock b, int delta)
    {
        var v = Math.Clamp(b.Indent + delta, 0, 5);
        if (v == b.Indent) return;
        Checkpoint();
        b.Indent = v;
    }

    /// <summary>Arrow up/down across blocks.</summary>
    public void FocusSibling(NoteBlock b, int direction, int caret)
    {
        var i = Page.Blocks.IndexOf(b) + direction;
        while (i >= 0 && i < Page.Blocks.Count && !NoteMarkdown.HasText(Page.Blocks[i].Type) && Page.Blocks[i].Type != BlockType.Image) i += direction;
        if (i < 0 || i >= Page.Blocks.Count) return;
        Focus(Page.Blocks[i], direction < 0 ? -1 : Math.Min(caret, Page.Blocks[i].Text.Length));
    }

    [RelayCommand]
    private void MoveUp(NoteBlock b)
    {
        var i = Page.Blocks.IndexOf(b);
        if (i <= 0) return;
        Checkpoint();
        Page.Blocks.Move(i, i - 1);
        Focus(b, -1);
    }

    [RelayCommand]
    private void MoveDown(NoteBlock b)
    {
        var i = Page.Blocks.IndexOf(b);
        if (i < 0 || i >= Page.Blocks.Count - 1) return;
        Checkpoint();
        Page.Blocks.Move(i, i + 1);
        Focus(b, -1);
    }

    [RelayCommand]
    private void DeleteBlock(NoteBlock b)
    {
        Checkpoint();
        var i = Page.Blocks.IndexOf(b);
        Page.Blocks.Remove(b);
        if (Page.Blocks.Count == 0) Page.Blocks.Add(new NoteBlock());
        Focus(Page.Blocks[Math.Clamp(i - 1, 0, Page.Blocks.Count - 1)], -1);
    }

    [RelayCommand]
    private void DuplicateBlock(NoteBlock b)
    {
        Checkpoint();
        var copy = Json.CloneDocument(b);
        copy.Id = Guid.NewGuid();
        Page.Blocks.Insert(Page.Blocks.IndexOf(b) + 1, copy);
    }

    /// <summary>"+" in the gutter: new empty line below, with the slash menu ready.</summary>
    [RelayCommand]
    private void AddBelow(NoteBlock b)
    {
        Checkpoint();
        var n = InsertAfter(b, BlockType.Paragraph);
        n.Text = "/";
        Focus(n, -1);
    }

    [RelayCommand]
    private void AddAtEnd()
    {
        var last = Page.Blocks.LastOrDefault();
        if (last is { Type: BlockType.Paragraph, Text: "" })
        {
            Focus(last, 0);
            return;
        }

        Focus(InsertAfter(last, BlockType.Paragraph), 0);
    }

    /// <summary>Gutter menu "Turn into": parameter is [block, type name].</summary>
    [RelayCommand]
    private Task Convert(object[] args) =>
        args is [NoteBlock b, string t] && Enum.TryParse<BlockType>(t, out var type) ? TurnInto(b, type) : Task.CompletedTask;

    public async Task TurnInto(NoteBlock b, BlockType type)
    {
        switch (type)
        {
            case BlockType.Image:
                var rel = PickImage();
                if (rel == null) return;
                Checkpoint();
                ReplaceOrInsert(b, new NoteBlock { Type = BlockType.Image, ImagePath = rel });
                return;
            case BlockType.Link:
                var pick = await Main.PickLinkAsync("Link to…", LinkTarget.Card, LinkTarget.Board, LinkTarget.Storyboard, LinkTarget.Canvas, LinkTarget.Note);
                if (pick == null)
                {
                    Focus(b, -1);
                    return;
                }

                Checkpoint();
                ReplaceOrInsert(b, new NoteBlock { Type = BlockType.Link, LinkKind = pick.Value.Kind, LinkId = pick.Value.Id, Text = pick.Value.Title });
                return;
            case BlockType.Divider:
                Checkpoint();
                ReplaceOrInsert(b, new NoteBlock { Type = BlockType.Divider });
                return;
            default:
                Checkpoint();
                b.Type = type;
                if (type == BlockType.Paragraph) b.Indent = 0;
                Focus(b, -1);
                return;
        }
    }

    /// <summary>An empty text block becomes the new block; otherwise the new block goes below it. A text line follows.</summary>
    private void ReplaceOrInsert(NoteBlock b, NoteBlock replacement)
    {
        var i = Page.Blocks.IndexOf(b);
        if (b.Text.Length == 0 && NoteMarkdown.HasText(b.Type)) Page.Blocks[i] = replacement;
        else Page.Blocks.Insert(++i, replacement);
        var next = i + 1 < Page.Blocks.Count && Page.Blocks[i + 1] is { Type: BlockType.Paragraph } p ? p : InsertAfter(replacement, BlockType.Paragraph);
        Focus(next, 0);
    }

    private string? PickImage()
    {
        try
        {
            return MediaStore.PickImage(Page.Id);
        }
        catch (Exception ex)
        {
            Main.ShowToast($"Couldn't add the image: {ex.Message}", isError: true);
            return null;
        }
    }

    /// <summary>Ctrl+V with an image on the clipboard inserts an image block. Returns false when there was none.</summary>
    public bool PasteImage(NoteBlock at)
    {
        var rel = MediaStore.FromClipboard(Page.Id);
        if (rel == null) return false;
        Checkpoint();
        ReplaceOrInsert(at, new NoteBlock { Type = BlockType.Image, ImagePath = rel });
        return true;
    }

    public void DropImages(IEnumerable<string> files, NoteBlock? at)
    {
        Checkpoint();
        var anchor = at ?? Page.Blocks.LastOrDefault();
        foreach (var f in files.Where(MediaStore.IsImageFile))
        {
            try
            {
                var block = new NoteBlock { Type = BlockType.Image, ImagePath = MediaStore.ImportFile(f, Page.Id) };
                Page.Blocks.Insert(anchor == null ? Page.Blocks.Count : Page.Blocks.IndexOf(anchor) + 1, block);
                anchor = block;
            }
            catch (Exception ex)
            {
                Main.ShowToast($"Couldn't add {Path.GetFileName(f)}: {ex.Message}", isError: true);
            }
        }
    }

    [RelayCommand]
    private void OpenLink(NoteBlock b)
    {
        if (b.LinkId is { } id) Main.OpenTarget(b.LinkKind, id);
    }

    [RelayCommand]
    private void ViewImage(NoteBlock b)
    {
        if (b.ImagePath != null)
            Main.ShowDialog(new ImagePreviewViewModel(new Attachment { Kind = AttachmentKind.Image, Name = b.Text, RelativePath = b.ImagePath }));
    }

    [RelayCommand]
    private void SetIcon(string icon) => Page.Icon = icon;

    [RelayCommand]
    private void FocusFirst()
    {
        if (Page.Blocks.FirstOrDefault(x => NoteMarkdown.HasText(x.Type)) is { } b) Focus(b, 0);
        else AddAtEnd();
    }

    // ================= export =================

    [RelayCommand]
    private void ExportMarkdown()
    {
        var name = Page.Title;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        var dlg = new SaveFileDialog { Filter = "Markdown|*.md|Text|*.txt", FileName = name + ".md", Title = "Export page" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, NoteMarkdown.ToMarkdown(Page, LinkTitle));
            Main.ShowToast("Page exported");
        }
        catch (Exception ex)
        {
            Main.ShowToast($"Export failed: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void CopyMarkdown()
    {
        try
        {
            System.Windows.Clipboard.SetText(NoteMarkdown.ToMarkdown(Page, LinkTitle));
            Main.ShowToast("Copied as Markdown");
        }
        catch
        {
            Main.ShowToast("The clipboard is busy — try again.", isError: true);
        }
    }

    /// <summary>Turns every unchecked to-do on the page into a card on the current board.</summary>
    [RelayCommand]
    private void TodosToCards()
    {
        var board = Main.CurrentBoard;
        var list = board?.Lists.FirstOrDefault(l => !l.IsDoneList);
        var todos = Page.Blocks.Where(b => b.Type == BlockType.Todo && !b.IsChecked && !string.IsNullOrWhiteSpace(b.Text)).ToList();
        if (board == null || list == null || todos.Count == 0)
        {
            Main.ShowToast(todos.Count == 0 ? "There are no open to-dos on this page." : "Open a board with a list first.", isError: true);
            return;
        }

        foreach (var t in todos)
        {
            var card = Main.CreateCardFromText(t.Text, board, list);
            card.Description = $"From the page \"{Page.Title}\"";
            list.Cards.Add(card);
        }

        Main.ShowToast($"Added {todos.Count} cards to \"{list.Name}\" on {board.Name}");
    }

    private string? LinkTitle(LinkTarget kind, Guid id) => kind switch
    {
        LinkTarget.Card => Main.Workspace.FindCard(id, out _, out _)?.Title,
        LinkTarget.Board => Main.Workspace.Boards.FirstOrDefault(b => b.Id == id)?.Name,
        LinkTarget.Storyboard => Main.Workspace.Storyboards.FirstOrDefault(s => s.Id == id)?.Name,
        LinkTarget.Canvas => Main.Workspace.Canvases.FirstOrDefault(c => c.Id == id)?.Name,
        LinkTarget.Note => Main.Workspace.Notes.FirstOrDefault(n => n.Id == id)?.Title,
        _ => null,
    };
}
