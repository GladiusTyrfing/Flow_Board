using System.Text.Json;
using FlowBoard.Models;
using FlowBoard.Services;
using Xunit;

namespace FlowBoard.Tests;

public class MergeTests
{
    private static string S(Workspace ws) => JsonSerializer.Serialize(ws, Json.Options);
    private static Workspace W(string json) => JsonSerializer.Deserialize<Workspace>(json, Json.Options)!;

    private static Workspace Sample()
    {
        var ws = new Workspace();
        var board = new Board { Name = "Film" };
        var todo = new BoardList { Name = "To do" };
        var done = new BoardList { Name = "Done" };
        todo.Cards.Add(new Card { Title = "Script" });
        todo.Cards.Add(new Card { Title = "Casting" });
        board.Lists.Add(todo);
        board.Lists.Add(done);
        ws.Boards.Add(board);
        var sb = new Storyboard { Name = "Short" };
        sb.Shots.Add(new Shot { Title = "Open" });
        ws.Storyboards.Add(sb);
        return ws;
    }

    private static (Workspace Ours, Workspace Theirs, string Base) Fork()
    {
        var b = S(Sample());
        return (W(b), W(b), b);
    }

    private static Workspace Merge(string b, Workspace ours, Workspace theirs, MergeStats? stats = null) =>
        W(JsonMerge.Merge(b, S(ours), S(theirs), stats));

    [Fact]
    public void Edits_to_different_things_are_both_kept()
    {
        var (ours, theirs, b) = Fork();
        ours.Boards[0].Lists[0].Cards[0].Title = "Script v2";
        theirs.Storyboards[0].Shots[0].Status = "Done";
        theirs.Boards[0].Lists[0].Cards[1].Description = "Leads first";

        var m = Merge(b, ours, theirs);
        Assert.Equal("Script v2", m.Boards[0].Lists[0].Cards[0].Title);
        Assert.Equal("Leads first", m.Boards[0].Lists[0].Cards[1].Description);
        Assert.Equal("Done", m.Storyboards[0].Shots[0].Status);
    }

    [Fact]
    public void Additions_from_both_sides_are_kept()
    {
        var (ours, theirs, b) = Fork();
        ours.Boards[0].Lists[0].Cards.Add(new Card { Title = "Mine" });
        theirs.Boards[0].Lists[0].Cards.Add(new Card { Title = "Theirs" });
        theirs.Notes.Add(new NotePage { Title = "Their page" });

        var m = Merge(b, ours, theirs);
        var titles = m.Boards[0].Lists[0].Cards.Select(c => c.Title).ToList();
        Assert.Equal(["Script", "Casting", "Mine", "Theirs"], titles);
        Assert.Equal("Their page", m.Notes.Single().Title);
    }

    [Fact]
    public void A_card_moved_on_one_side_and_edited_on_the_other_moves_with_the_edit()
    {
        var (ours, theirs, b) = Fork();
        var card = theirs.Boards[0].Lists[0].Cards[0];
        theirs.Boards[0].Lists[0].Cards.Remove(card);
        theirs.Boards[0].Lists[1].Cards.Add(card);              // they moved "Script" to Done
        ours.Boards[0].Lists[0].Cards[0].Priority = Priority.High; // we changed its priority

        var m = Merge(b, ours, theirs);
        Assert.DoesNotContain(m.Boards[0].Lists[0].Cards, c => c.Title == "Script");
        var moved = Assert.Single(m.Boards[0].Lists[1].Cards);
        Assert.Equal(Priority.High, moved.Priority);
    }

    [Fact]
    public void Deletes_apply_unless_the_other_side_edited_the_item()
    {
        var (ours, theirs, b) = Fork();
        theirs.Boards[0].Lists[0].Cards.RemoveAt(1);           // they deleted Casting
        var m = Merge(b, ours, theirs);
        Assert.Single(m.Boards[0].Lists[0].Cards);

        (ours, theirs, b) = Fork();
        theirs.Boards[0].Lists[0].Cards.RemoveAt(1);           // they deleted Casting…
        ours.Boards[0].Lists[0].Cards[1].Title = "Casting call"; // …while we renamed it: keep it
        m = Merge(b, ours, theirs);
        Assert.Contains(m.Boards[0].Lists[0].Cards, c => c.Title == "Casting call");
    }

    [Fact]
    public void Same_field_changed_on_both_sides_keeps_ours_and_counts_a_conflict()
    {
        var (ours, theirs, b) = Fork();
        ours.Boards[0].Name = "Film (ours)";
        theirs.Boards[0].Name = "Film (theirs)";
        var stats = new MergeStats();
        var m = Merge(b, ours, theirs, stats);
        Assert.Equal("Film (ours)", m.Boards[0].Name);
        Assert.Equal(1, stats.Conflicts);
    }

    [Fact]
    public void Id_lists_merge_as_sets()
    {
        var (ours, theirs, b) = Fork();
        var l1 = Guid.NewGuid();
        var l2 = Guid.NewGuid();
        ours.Boards[0].Lists[0].Cards[0].LabelIds.Add(l1);
        theirs.Boards[0].Lists[0].Cards[0].LabelIds.Add(l2);
        var m = Merge(b, ours, theirs);
        Assert.Equal([l1, l2], m.Boards[0].Lists[0].Cards[0].LabelIds.ToList());
    }

    [Fact]
    public void Reordering_on_one_side_is_kept()
    {
        var (ours, theirs, b) = Fork();
        theirs.Boards[0].Lists[0].Cards.Move(1, 0);
        ours.Boards[0].Lists[0].Cards.Add(new Card { Title = "New" });
        var m = Merge(b, ours, theirs);
        Assert.Equal(["Casting", "Script", "New"], m.Boards[0].Lists[0].Cards.Select(c => c.Title).ToList());
    }

    [Fact]
    public void Nothing_changed_gives_the_same_project()
    {
        var (ours, theirs, b) = Fork();
        Assert.Equal(b, S(Merge(b, ours, theirs)));
    }
}

public class ReconcileTests
{
    [Fact]
    public void Updates_the_live_objects_instead_of_replacing_them()
    {
        var live = new Workspace();
        var board = new Board { Name = "Film" };
        var todo = new BoardList { Name = "To do" };
        var done = new BoardList { Name = "Done" };
        var script = new Card { Title = "Script" };
        var casting = new Card { Title = "Casting" };
        todo.Cards.Add(script);
        todo.Cards.Add(casting);
        board.Lists.Add(todo);
        board.Lists.Add(done);
        live.Boards.Add(board);

        // Another copy: Script renamed and moved to Done, Casting deleted, a new card and a page added.
        var other = JsonSerializer.Deserialize<Workspace>(JsonSerializer.Serialize(live, Json.Options), Json.Options)!;
        var s2 = other.Boards[0].Lists[0].Cards[0];
        s2.Title = "Script (final)";
        s2.LabelIds.Add(Guid.NewGuid());
        other.Boards[0].Lists[0].Cards.Clear();
        other.Boards[0].Lists[1].Cards.Add(s2);
        other.Boards[0].Lists[0].Cards.Add(new Card { Title = "Edit" });
        other.Notes.Add(new NotePage { Title = "Notes" });

        var events = 0;
        todo.Cards.CollectionChanged += (_, _) => events++;
        var changed = ModelReconciler.Apply(live, JsonSerializer.SerializeToNode(other, Json.Options)!.AsObject());

        Assert.Same(board, live.Boards.Single());
        Assert.Same(todo, board.Lists[0]);
        Assert.Equal("Edit", Assert.Single(todo.Cards).Title);
        var moved = Assert.Single(done.Cards);
        Assert.Equal("Script (final)", moved.Title);
        Assert.Single(moved.LabelIds);
        Assert.Equal("Notes", live.Notes.Single().Title);
        Assert.True(events > 0);
        Assert.Contains(moved, changed);
        // Same content as the other copy now.
        Assert.Equal(JsonSerializer.Serialize(other, Json.Options), JsonSerializer.Serialize(live, Json.Options));
    }

    [Fact]
    public void Reorders_with_moves_and_clears_removed_values()
    {
        var live = new Workspace();
        var sb = new Storyboard();
        var a = new Shot { Title = "A", Description = "x" };
        var b = new Shot { Title = "B" };
        sb.Shots.Add(a);
        sb.Shots.Add(b);
        live.Storyboards.Add(sb);
        var other = JsonSerializer.Deserialize<Workspace>(JsonSerializer.Serialize(live, Json.Options), Json.Options)!;
        other.Storyboards[0].Shots.Move(1, 0);
        other.Storyboards[0].Shots[1].Date = DateTime.Today;

        ModelReconciler.Apply(live, JsonSerializer.SerializeToNode(other, Json.Options)!.AsObject());
        Assert.Same(b, sb.Shots[0]);
        Assert.Same(a, sb.Shots[1]);
        Assert.Equal(DateTime.Today, a.Date);
    }
}

/// <summary>Two people sharing one project file, using the same merge-then-save steps as the app.</summary>
public class TeamworkSimulationTests
{
    private sealed class Disk
    {
        public string Content = "";
    }

    private sealed class Peer
    {
        public readonly Workspace Live;
        private string _base;
        private readonly Disk _disk;

        public Peer(Disk disk)
        {
            _disk = disk;
            _base = disk.Content;
            Live = JsonSerializer.Deserialize<Workspace>(disk.Content, Json.Options)!;
            Live.Hydrate();
        }

        /// <summary>What DataStore.CheckForExternalChanges does.</summary>
        public void Pull()
        {
            if (_disk.Content == _base) return;
            var ours = JsonSerializer.SerializeToNode(Live, Json.Options);
            var merged = JsonMerge.Merge(System.Text.Json.Nodes.JsonNode.Parse(_base), ours, System.Text.Json.Nodes.JsonNode.Parse(_disk.Content));
            ModelReconciler.Apply(Live, merged!.AsObject());
            Live.Hydrate();
            _base = _disk.Content;
        }

        /// <summary>What DataStore.SaveIfChanged does.</summary>
        public void Save()
        {
            Pull();
            _disk.Content = JsonSerializer.Serialize(Live, Json.Options);
            _base = _disk.Content;
        }
    }

    [Fact]
    public void Two_people_editing_and_saving_in_turn_end_up_with_the_same_project_and_both_edits()
    {
        var start = new Workspace();
        var board = new Board { Name = "Film" };
        var todo = new BoardList { Name = "To do" };
        var done = new BoardList { Name = "Done" };
        todo.Cards.Add(new Card { Title = "Script" });
        board.Lists.Add(todo);
        board.Lists.Add(done);
        start.Boards.Add(board);
        var sb = new Storyboard { Name = "Short" };
        sb.Shots.Add(new Shot { Title = "Open" });
        start.Storyboards.Add(sb);
        var disk = new Disk { Content = JsonSerializer.Serialize(start, Json.Options) };

        var me = new Peer(disk);
        var sam = new Peer(disk);
        var myScript = me.Live.Boards[0].Lists[0].Cards[0];

        // Both work at the same time without seeing each other yet.
        myScript.Description = "Draft 2";
        me.Live.Boards[0].Lists[0].Cards.Add(new Card { Title = "Casting" });
        sam.Live.Storyboards[0].Shots.Add(new Shot { Title = "Close-up" });
        sam.Live.Storyboards[0].Shots[0].Status = "Done";
        sam.Live.Notes.Add(new NotePage { Title = "Call sheet" });

        me.Save();
        sam.Save();   // merges my save first
        me.Pull();

        // Sam moves Script to Done while I rename it.
        var samScript = sam.Live.Boards[0].Lists[0].Cards.First(c => c.Title == "Script");
        sam.Live.Boards[0].Lists[0].Cards.Remove(samScript);
        sam.Live.Boards[0].Lists[1].Cards.Add(samScript);
        myScript.Title = "Script (final)";
        sam.Save();
        me.Save();
        sam.Pull();

        var mine = JsonSerializer.Serialize(me.Live, Json.Options);
        var his = JsonSerializer.Serialize(sam.Live, Json.Options);
        Assert.Equal(mine, his);
        Assert.Equal(mine, disk.Content);

        var w = me.Live;
        Assert.Equal(["Casting"], w.Boards[0].Lists[0].Cards.Select(c => c.Title).ToList());
        var moved = Assert.Single(w.Boards[0].Lists[1].Cards);
        Assert.Equal("Script (final)", moved.Title);
        Assert.Equal("Draft 2", moved.Description);
        Assert.Same(myScript, moved); // my live card object was moved, not replaced
        Assert.Equal(["Open", "Close-up"], w.Storyboards[0].Shots.Select(s => s.Title).ToList());
        Assert.Equal("Done", w.Storyboards[0].Shots[0].Status);
        Assert.Equal("Call sheet", w.Notes.Single().Title);
    }
}

public class UnionMergeTests
{
    [Fact]
    public void Without_a_base_nothing_is_deleted_and_ours_wins_differences()
    {
        var ours = new Workspace();
        var theirs = new Workspace();
        var shared = new Board { Name = "Ours" };
        ours.Boards.Add(shared);
        var copy = JsonSerializer.Deserialize<Board>(JsonSerializer.Serialize(shared, Json.Options), Json.Options)!;
        copy.Name = "Theirs";
        theirs.Boards.Add(copy);
        theirs.Notes.Add(new NotePage { Title = "Only in the conflict copy" });
        ours.Canvases.Add(new CanvasDoc { Name = "Only in ours" });

        var m = JsonSerializer.Deserialize<Workspace>(
            JsonMerge.Merge(null, JsonSerializer.SerializeToNode(ours, Json.Options), JsonSerializer.SerializeToNode(theirs, Json.Options))!.ToJsonString(), Json.Options)!;
        Assert.Equal("Ours", m.Boards.Single().Name);
        Assert.Equal("Only in the conflict copy", m.Notes.Single().Title);
        Assert.Equal("Only in ours", m.Canvases.Single().Name);
    }
}
