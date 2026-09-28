using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FlowBoard.Models;

/// <summary>Everything that is persisted to data.json.</summary>
public partial class Workspace : ObservableObject
{
    [ObservableProperty] private int _version = 1;
    /// <summary>Name of the folder next to the project file that holds its media ("&lt;name&gt; files").</summary>
    public string? MediaFolder { get; set; }
    [ObservableProperty] private ObservableCollection<Board> _boards = [];
    [ObservableProperty] private ObservableCollection<BoardTemplate> _userTemplates = [];
    [ObservableProperty] private ObservableCollection<Storyboard> _storyboards = [];
    [ObservableProperty] private ObservableCollection<CanvasDoc> _canvases = [];
    [ObservableProperty] private ObservableCollection<NotePage> _notes = [];

    public void Hydrate()
    {
        foreach (var b in Boards) b.Hydrate();
    }

    public IEnumerable<(Board Board, BoardList List, Card Card)> EnumerateActiveCards()
    {
        foreach (var b in Boards)
            foreach (var l in b.Lists)
                foreach (var c in l.Cards)
                    yield return (b, l, c);
    }

    public Card? FindCard(Guid id, out Board? board, out BoardList? list)
    {
        foreach (var b in Boards)
        {
            foreach (var l in b.Lists)
            {
                var c = l.Cards.FirstOrDefault(x => x.Id == id);
                if (c != null) { board = b; list = l; return c; }
            }
            var archived = b.ArchivedCards.FirstOrDefault(x => x.Id == id);
            if (archived != null) { board = b; list = null; return archived; }
        }
        board = null; list = null;
        return null;
    }
}

public class BoardTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "Board24";
    public string Background { get; set; } = "gradient:#0C66E4,#9F6FEF";
    public List<string> Lists { get; set; } = [];
    public List<TemplateLabel> Labels { get; set; } = [];
    /// <summary>For user-saved templates: full board snapshot as JSON.</summary>
    public string? BoardJson { get; set; }
    public bool IsBuiltIn { get; set; }
}

public class TemplateLabel
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#4BCE97";

    public TemplateLabel() { }
    public TemplateLabel(string name, string color) { Name = name; Color = color; }
}
