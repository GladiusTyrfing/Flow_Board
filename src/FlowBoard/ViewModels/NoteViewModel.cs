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
        RefreshLinks();
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
        OnPropertyChanged(nameof(Outline));
        OnPropertyChanged(nameof(ShowTemplates));
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
                if (b.Type is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3) OnPropertyChanged(nameof(Outline));
                break;
            case nameof(NoteBlock.Spans):
                Touch();
                break;
            case nameof(NoteBlock.Type):
            case nameof(NoteBlock.Indent):
                Renumber();
                Touch();
                OnPropertyChanged(nameof(Outline));
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
        Page.CoverPath = snap.CoverPath;
        Page.FullWidth = snap.FullWidth;
        Page.Blocks.Clear();
        foreach (var b in snap.Blocks) Page.Blocks.Add(b);
        if (Page.Blocks.Count == 0) Page.Blocks.Add(new NoteBlock());
        RefreshLinks();
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
        b.SetSpans(RichText.RemovePrefix(b.GetSpans(), b.Text.Length - hit.Text.Length));
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
        b.SetSpans([]);
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
        var (left, right) = RichText.Split(b.GetSpans(), caret);
        _converting = true;
        b.SetSpans(left);
        _converting = false;
        var restLength = RichText.PlainText(right).Length;
        var type = NoteMarkdown.IsListType(b.Type) || b.Type is BlockType.Quote or BlockType.Callout && restLength > 0 ? b.Type : BlockType.Paragraph;
        if (!NoteMarkdown.HasText(b.Type)) type = BlockType.Paragraph;
        var next = InsertAfter(b, type);
        _converting = true;
        next.SetSpans(right);
        _converting = false;
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
        prev.SetSpans(RichText.Concat(prev.GetSpans(), b.GetSpans()));
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
            b.SetSpans(RichText.Concat(b.GetSpans(), next.GetSpans()));
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
                var pick = await Main.PickLinkAsync("Link to…", LinkResolver.All);
                if (pick == null)
                {
                    Focus(b, -1);
                    return;
                }

                Checkpoint();
                var link = new NoteBlock { Type = BlockType.Link, LinkKind = pick.Value.Kind, LinkId = pick.Value.Id, Text = pick.Value.Title };
                link.Link = Preview(link);
                ReplaceOrInsert(b, link);
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

    // ================= outline, cover, width, templates =================

    /// <summary>Headings of the page (for the outline panel).</summary>
    public IEnumerable<NoteBlock> Outline => Page.Blocks.Where(b => b.Type is BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3 && b.Text.Length > 0).ToList();

    [ObservableProperty] private bool _showOutline;

    /// <summary>The block that last had the cursor (inserts from the toolbar go below it).</summary>
    public NoteBlock? LastFocused { get; set; }

    [RelayCommand]
    private void GoTo(NoteBlock b) => Focus(b, -1);

    [RelayCommand] private void ToggleFullWidth() => Page.FullWidth = !Page.FullWidth;

    [RelayCommand]
    private void AddCover()
    {
        try
        {
            var rel = MediaStore.PickImage(Page.Id);
            if (rel == null) return;
            Main.ShowDialog(new ImageCropViewModel(rel, Page.Id, 4, null, r =>
            {
                Checkpoint();
                Page.CoverPath = r.ImagePath;
            }, "Page cover"));
        }
        catch (Exception ex)
        {
            Main.ShowToast($"Couldn't add the cover: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void RemoveCover()
    {
        Checkpoint();
        Page.CoverPath = null;
    }

    /// <summary>Inserts a block of the given type below the block you were typing in (toolbar buttons).</summary>
    [RelayCommand]
    private async Task Insert(string type)
    {
        if (!Enum.TryParse<BlockType>(type, out var t)) return;
        var anchor = LastFocused != null && Page.Blocks.Contains(LastFocused) ? LastFocused : Page.Blocks.LastOrDefault();
        NoteBlock target;
        if (anchor is { Text: "" } && NoteMarkdown.HasText(anchor.Type)) target = anchor;
        else
        {
            Checkpoint();
            target = InsertAfter(anchor, BlockType.Paragraph);
        }

        await TurnInto(target, t);
    }

    public bool ShowTemplates => Page.Blocks.Count <= 1 && Page.Blocks.All(b => b.Text.Length == 0 && NoteMarkdown.HasText(b.Type));

    public static readonly string[] TemplateNames = ["Meeting notes", "Project brief", "Script / screenplay", "Shot list", "To-do list", "Journal"];
    public string[] Templates => TemplateNames;

    [RelayCommand]
    private void ApplyTemplate(string name)
    {
        Checkpoint();
        var blocks = PageTemplates.Build(name, DateTime.Now);
        Page.Blocks.Clear();
        foreach (var b in blocks) Page.Blocks.Add(b);
        if (Page.Title.StartsWith("Untitled page", StringComparison.Ordinal)) Page.Title = name == "Journal" ? $"Journal — {DateTime.Now:d MMM yyyy}" : name;
        Focus(Page.Blocks.FirstOrDefault(b => NoteMarkdown.HasText(b.Type) && b.Text.Length == 0) ?? Page.Blocks[0], -1);
    }

    /// <summary>Resolves what every link block points to (title, board colors, frame thumbnails…).</summary>
    public void RefreshLinks()
    {
        foreach (var b in Page.Blocks.Where(b => b.Type == BlockType.Link)) b.Link = Preview(b);
    }

    private LinkPreview Preview(NoteBlock b) =>
        b.LinkId is { } id ? LinkResolver.Describe(Main.Workspace, b.LinkKind, id, b.Text) : new LinkPreview { Title = b.Text, IsMissing = true };

    [RelayCommand]
    private void CropBlockImage(NoteBlock b)
    {
        if (b.ImagePath is not { } path) return;
        Main.ShowDialog(new ImageCropViewModel(path, Page.Id, 0, null, r =>
        {
            Checkpoint();
            b.ImagePath = r.ImagePath;
        }));
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

    private string? LinkTitle(LinkTarget kind, Guid id) => LinkResolver.TitleOf(Main.Workspace, kind, id);
}
