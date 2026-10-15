using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;

namespace FlowBoard.ViewModels;

public sealed partial class MainViewModel
{
    private bool _syncing;

    private void HookLinkSync()
    {
        ModelEvents.CardCompletionChanged += OnCardCompletionChanged;
        ModelEvents.ShotStatusChanged += OnShotStatusChanged;
        ModelEvents.TodoChanged += OnTodoChanged;
    }

    private void OnCardCompletionChanged(Card card)
    {
        if (_syncing || !HasProject || Workspace.FindCard(card.Id, out _, out _) != card) return;
        RunSync(() => PushCardState(card));
    }

    private void OnShotStatusChanged(Shot shot)
    {
        if (_syncing || !HasProject || !LinkSync.ContainsShot(Workspace, shot)) return;
        var done = LinkSync.IsDoneStatus(shot.Status);
        RunSync(() =>
        {
            foreach (var card in LinkSync.CardsLinkedTo(Workspace, LinkTarget.Shot, shot.Id).ToList())
            {
                if (card.IsCompleted == done) continue;
                SetCardCompleted(card, done, $"shot \"{shot.Title}\" is {(done ? shot.Status.ToLowerInvariant() : "no longer done")}");
                PushCardState(card);
            }
        });
    }

    private void OnTodoChanged(NoteBlock block)
    {
        if (_syncing || !HasProject || block.Type != BlockType.Todo || block.LinkKind != LinkTarget.Card || block.LinkId is not { } id) return;
        if (Workspace.FindCard(id, out _, out _) is not { } card || card.IsCompleted == block.IsChecked) return;
        if (!LinkSync.ContainsBlock(Workspace, block)) return;
        RunSync(() =>
        {
            SetCardCompleted(card, block.IsChecked, block.IsChecked ? "its to-do was ticked on a page" : "its to-do was unticked on a page");
            PushCardState(card);
        });
    }

    private void RunSync(Action action)
    {
        _syncing = true;
        try
        {
            action();
        }
        finally
        {
            _syncing = false;
        }

        RefreshBlocked();
        if (ActiveView == ActiveView.Board && ViewMode != BoardViewMode.Board) RefreshViews();
    }

    private void PushCardState(Card card)
    {
        foreach (var shot in LinkSync.ShotsOf(Workspace, card))
            if (LinkSync.ShotStatusFor(card.IsCompleted, shot.Status) is { } status) shot.Status = status;
        foreach (var todo in LinkSync.TodosFor(Workspace, card.Id))
            if (todo.IsChecked != card.IsCompleted) todo.IsChecked = card.IsCompleted;
    }

    private void SetCardCompleted(Card card, bool done, string why)
    {
        card.IsCompleted = done;
        card.AddActivity((done ? "marked this card as complete — " : "marked this card as incomplete — ") + why, Me);
        MoveForCompletion(card);
    }

    public static readonly LinkTarget[] CardLinkKinds = [LinkTarget.Board, LinkTarget.Storyboard, LinkTarget.Shot, LinkTarget.Canvas, LinkTarget.Note];

    public void AddCardLink(Card card, LinkTarget kind, Guid id)
    {
        if (card.Links.Any(l => l.Kind == kind && l.Id == id)) return;
        card.Links.Add(new ItemLink(kind, id));
        card.NotifyRelationsChanged();
        if (kind == LinkTarget.Shot && LinkResolver.FindShot(Workspace, id, out _) is { } shot)
        {
            if (LinkSync.IsDoneStatus(shot.Status) && !card.IsCompleted) SetCardCompleted(card, true, $"shot \"{shot.Title}\" is done");
            else RunSync(() => PushCardState(card));
        }
    }

    [RelayCommand]
    private async Task AddLinkedCard(BoardList? list)
    {
        if (list == null || CurrentBoard == null) return;
        var pick = await PickLinkAsync("Add a card for…", CardLinkKinds);
        if (pick == null) return;
        Undo.Checkpoint(Workspace, CurrentBoard, "Add linked card");
        var card = new Card { Title = pick.Value.Title, Board = CurrentBoard };
        list.Cards.Add(card);
        card.AddActivity($"created this card for {pick.Value.Kind.ToString().ToLowerInvariant()} \"{pick.Value.Title}\"", Me);
        AddCardLink(card, pick.Value.Kind, pick.Value.Id);
        list.IsAddingCard = false;
        list.RaiseCounts();
        ApplyFilter();
    }
}
