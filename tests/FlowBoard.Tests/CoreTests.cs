using System.Text.Json;
using FlowBoard.Models;
using FlowBoard.Services;
using FlowBoard.ViewModels;
using Xunit;

namespace FlowBoard.Tests;

public class CoreTests
{
    private static Board SampleBoard()
    {
        var board = TemplateService.Create(TemplateService.BuiltIn[0], "Trident");
        var card = new Card { Title = "Bugs", Board = board, Description = "Crash on start", Priority = Priority.High };
        card.LabelIds.Add(board.Labels[0].Id);
        var cl = new Checklist { Title = "Steps" };
        cl.Items.Add(new ChecklistItem { Text = "Reproduce", IsDone = true });
        cl.Items.Add(new ChecklistItem { Text = "Fix" });
        card.Checklists.Add(cl);
        card.Attachments.Add(new Attachment { Kind = AttachmentKind.Voice, Name = "note.wav", RelativePath = "attachments/x/note.wav", DurationSeconds = 3 });
        card.TimeEntries.Add(new TimeEntry { Start = DateTime.Now.AddMinutes(-30), End = DateTime.Now });
        board.Lists[0].Cards.Add(card);
        return board;
    }

    [Fact]
    public void BasicTemplate_HasThreeDefaultLists()
    {
        var board = TemplateService.Create(TemplateService.BuiltIn[0], "Trident");
        Assert.Equal(["To Do", "Doing", "Done"], board.Lists.Select(l => l.Name));
        Assert.True(board.Lists[2].IsDoneList);
        Assert.Equal(6, board.Labels.Count);
    }

    [Fact]
    public void Workspace_RoundTripsThroughJson()
    {
        var ws = new Workspace();
        ws.Boards.Add(SampleBoard());
        var json = JsonSerializer.Serialize(ws, Json.Options);
        var back = JsonSerializer.Deserialize<Workspace>(json, Json.Options)!;
        back.Hydrate();

        var card = back.Boards[0].Lists[0].Cards[0];
        Assert.Equal("Bugs", card.Title);
        Assert.Equal(Priority.High, card.Priority);
        Assert.Same(back.Boards[0], card.Board);
        Assert.Single(card.ResolvedLabels);
        Assert.Equal("1/2", card.ChecklistText);
        Assert.Equal(1, card.VoiceCount);
        Assert.True(card.HasTrackedTime);
        // Runtime-only state must not be persisted.
        Assert.DoesNotContain("IsFilteredOut", json);
        Assert.DoesNotContain("NewCardTitle", json);
    }

    [Fact]
    public void ChecklistProgress_UpdatesCardBadge()
    {
        var card = SampleBoard().Lists[0].Cards[0];
        var raised = new List<string?>();
        card.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        card.Checklists[0].Items[1].IsDone = true;
        Assert.True(card.IsChecklistComplete);
        Assert.Contains(nameof(Card.ChecklistText), raised);
    }

    [Fact]
    public void Undo_RestoresDeletedCard_AndRedoRemovesItAgain()
    {
        var ws = new Workspace();
        var board = SampleBoard();
        ws.Boards.Add(board);
        var undo = new UndoService();

        undo.Checkpoint(ws, board, "Delete card");
        board.Lists[0].Cards.Clear();

        var restored = undo.Undo(ws, out var desc)!;
        Assert.Equal("Delete card", desc);
        Assert.Single(restored.Lists[0].Cards);
        Assert.Same(restored, ws.Boards[0]);

        var redone = undo.Redo(ws, out _)!;
        Assert.Empty(redone.Lists[0].Cards);
    }

    [Fact]
    public void Undo_OfBoardCreation_RemovesBoard()
    {
        var ws = new Workspace();
        var undo = new UndoService();
        var board = SampleBoard();
        undo.CheckpointBoardCreated(board.Id, "Create board");
        ws.Boards.Add(board);

        undo.Undo(ws, out _);
        Assert.Empty(ws.Boards);
        undo.Redo(ws, out _);
        Assert.Single(ws.Boards);
    }

    [Fact]
    public void CloneWithNewIds_RemapsLabels()
    {
        var board = SampleBoard();
        var copy = Json.CloneWithNewIds(board);
        Assert.NotEqual(board.Id, copy.Id);
        var card = copy.Lists[0].Cards[0];
        Assert.NotEqual(board.Lists[0].Cards[0].Id, card.Id);
        Assert.Single(card.ResolvedLabels);
        Assert.Contains(card.LabelIds[0], copy.Labels.Select(l => l.Id));
        Assert.DoesNotContain(card.LabelIds[0], board.Labels.Select(l => l.Id));
    }

    [Fact]
    public void UserTemplate_CreatesIndependentBoard()
    {
        var board = SampleBoard();
        var template = TemplateService.SaveAsTemplate(board, "Mine", includeCards: true);
        var created = TemplateService.Create(template, "From template");
        Assert.Equal("From template", created.Name);
        var card = created.Lists[0].Cards[0];
        Assert.Equal("Bugs", card.Title);
        Assert.Empty(card.Attachments); // files belong to the original card only
        Assert.Single(card.ResolvedLabels);
    }

    [Fact]
    public void Filter_MatchesTextLabelsPriorityAndDue()
    {
        var board = SampleBoard();
        var card = board.Lists[0].Cards[0];
        var f = new FilterState();
        f.SetBoard(board);
        Assert.True(f.Matches(card));

        f.Text = "crash";
        Assert.True(f.Matches(card));
        f.Text = "nope";
        Assert.False(f.Matches(card));
        f.Clear();

        f.MinPriority = Priority.Urgent;
        Assert.False(f.Matches(card));
        f.Clear();

        f.LabelOptions[1].IsSelected = true;
        Assert.False(f.Matches(card));
        f.LabelOptions[0].IsSelected = true;
        Assert.True(f.Matches(card));
        f.Clear();

        f.Due = DueFilter.NoDate;
        Assert.True(f.Matches(card));
        card.DueDate = DateTime.Today.AddDays(-2);
        f.Due = DueFilter.Overdue;
        Assert.True(f.Matches(card));
        Assert.Equal(DueState.Overdue, card.DueState);
        Assert.False(f.IsActive == false);
    }

    [Fact]
    public void DueDate_WithoutTime_MeansEndOfDay()
    {
        var card = new Card { DueDate = DateTime.Today };
        Assert.NotEqual(DueState.Overdue, card.DueState);
        card.IsCompleted = true;
        Assert.Equal(DueState.Done, card.DueState);
        Assert.NotNull(card.CompletedAt);
    }

    [Fact]
    public void ChangingDueDate_ResetsReminder()
    {
        var card = new Card { DueDate = DateTime.Today, ReminderMinutes = 15, ReminderSent = true };
        card.DueDate = DateTime.Today.AddDays(1);
        Assert.False(card.ReminderSent);
        Assert.True(card.HasReminder);
    }

    [Fact]
    public void DefaultSettings_RoundTripThroughJson()
    {
        // Regression: the NaN window position used to make the first save throw, so the app never showed.
        var json = JsonSerializer.Serialize(new AppSettings(), Json.Options);
        var back = JsonSerializer.Deserialize<AppSettings>(json, Json.Options)!;
        Assert.True(double.IsNaN(back.WindowLeft));
        Assert.Equal("Ctrl+Alt+Space", back.QuickAddHotkey);
    }

    [Fact]
    public void FirstRunWorkspace_Serializes()
    {
        var ws = new Workspace();
        ws.Boards.Add(TemplateService.CreateWelcomeBoard("Tester"));
        ws.UserTemplates.Add(TemplateService.SaveAsTemplate(ws.Boards[0], "T", true));
        var json = JsonSerializer.Serialize(ws, Json.Options);
        Assert.NotNull(JsonSerializer.Deserialize<Workspace>(json, Json.Options));
    }

    [Fact]
    public void WelcomeBoard_IsValid()
    {
        var board = TemplateService.CreateWelcomeBoard("Tester");
        Assert.Equal(3, board.Lists.Count);
        Assert.All(board.AllActiveCards, c => Assert.Same(board, c.Board));
        Assert.True(board.ActiveCardCount >= 5);
    }
}
