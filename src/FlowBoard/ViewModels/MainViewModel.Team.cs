using FlowBoard.Models;
using FlowBoard.Services;

namespace FlowBoard.ViewModels;

/// <summary>
/// Working together on a shared project: changes saved by others are merged in by the data store;
/// here the open views catch up and everyone can see who else is in the project.
/// </summary>
public sealed partial class MainViewModel
{
    private DateTime _lastTeamToast = DateTime.MinValue;

    public IReadOnlyList<string> Collaborators => _store.Collaborators;
    public bool HasCollaborators => _store.Collaborators.Count > 0;
    public string CollaboratorsText => _store.Collaborators.Count switch
    {
        0 => string.Empty,
        1 => $"{_store.Collaborators[0]} is here too",
        2 => $"{_store.Collaborators[0]} and {_store.Collaborators[1]} are here too",
        var n => $"{_store.Collaborators[0]} and {n - 1} others are here too",
    };

    private void HookTeamwork()
    {
        _store.ExternalChangeMerged += (_, c) => OnExternalChange(c);
        _store.CollaboratorsChanged += (_, _) => OnCollaboratorsChanged();
    }

    private IReadOnlyList<string> _knownCollaborators = [];

    private void OnCollaboratorsChanged()
    {
        var joined = _store.Collaborators.Except(_knownCollaborators).ToList();
        _knownCollaborators = _store.Collaborators;
        OnPropertyChanged(nameof(Collaborators));
        OnPropertyChanged(nameof(HasCollaborators));
        OnPropertyChanged(nameof(CollaboratorsText));
        if (joined.Count > 0) ShowToast(joined.Count == 1 ? $"{joined[0]} opened this project" : $"{string.Join(", ", joined)} opened this project");
    }

    private static Guid? IdOf(object? model) => model switch
    {
        Storyboard s => s.Id,
        CanvasDoc c => c.Id,
        NotePage n => n.Id,
        _ => null,
    };

    private void OnExternalChange(ExternalChange change)
    {
        // Page editors only reload text when told to.
        foreach (var o in change.ChangedObjects)
            if (o is NoteBlock b) b.ContentVersion++;

        // Undo would put back what the other person just changed: start fresh where their changes landed.
        if (CurrentBoard != null && change.ChangedItems.Contains(CurrentBoard.Id)) Undo.Clear();
        if (IdOf(ActiveDocument?.Model) is { } docId && change.ChangedItems.Contains(docId)) ActiveDocument!.ClearHistory();

        // Things that were deleted by someone else.
        foreach (var d in Dialogs.OfType<CardDetailViewModel>().ToList())
            if (Workspace.FindCard(d.Card.Id, out _, out _) == null) CloseDialog(d);
        if (CurrentBoard != null && !Workspace.Boards.Contains(CurrentBoard) && ActiveView == ActiveView.Board)
        {
            OpenDashboard();
            ShowToast("The board you had open was deleted by someone else.");
        }

        if (ActiveDocument?.Model is { } model && !StillExists(model))
        {
            OpenDashboard();
            ShowToast("The page you had open was deleted by someone else.");
        }
        else
        {
            ActiveDocument?.OnExternalChange();
        }

        RefreshSidebar();
        ApplyFilter();
        RefreshViews();

        // One quiet note at a time (others may save every few seconds while they work).
        if (DateTime.Now - _lastTeamToast > TimeSpan.FromSeconds(20) || change.Conflicts > 0 || change.FromConflictCopy)
        {
            _lastTeamToast = DateTime.Now;
            var who = string.IsNullOrWhiteSpace(change.Editor) || change.Editor == Settings.DisplayName ? "Someone" : change.Editor;
            var text = change.FromConflictCopy
                ? "Merged a conflicting copy made by your sync app"
                : $"{who} updated the project";
            if (change.Conflicts > 0)
                text += change.Conflicts == 1 ? " · 1 edit overlapped with yours (yours was kept)" : $" · {change.Conflicts} edits overlapped with yours (yours were kept)";
            ShowToast(text);
        }
    }

    private bool StillExists(object model) => model switch
    {
        Storyboard s => Workspace.Storyboards.Contains(s),
        CanvasDoc c => Workspace.Canvases.Contains(c),
        NotePage n => Workspace.Notes.Contains(n),
        _ => true,
    };
}
