using System.Collections;
using System.Windows;
using FlowBoard.Models;
using GongSolutions.Wpf.DragDrop;

namespace FlowBoard.ViewModels;

/// <summary>Moves cards between lists (records undo + activity, auto-completes in "done" lists).</summary>
public sealed class CardDropHandler : DefaultDropHandler
{
    public override void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is BoardList)
        {
            // Let the enclosing lists panel handle list reordering.
            dropInfo.NotHandled = true;
            return;
        }

        if (dropInfo.Data is not Card)
        {
            dropInfo.Effects = DragDropEffects.None;
            return;
        }

        base.DragOver(dropInfo);
        if (dropInfo.Effects != DragDropEffects.None) dropInfo.Effects = DragDropEffects.Move;
    }

    public override void Drop(IDropInfo dropInfo)
    {
        var main = MainViewModel.Instance;
        var board = main.CurrentBoard;
        if (board == null || dropInfo.Data is not Card card) return;

        var source = FindList(board, dropInfo.DragInfo?.SourceCollection);
        var target = FindList(board, dropInfo.TargetCollection);
        main.Undo.Checkpoint(main.Workspace, board, "Move card");
        base.Drop(dropInfo);
        main.AfterCardMoved(card, source, target);
    }

    private static BoardList? FindList(Board board, IEnumerable? collection) =>
        collection == null ? null : board.Lists.FirstOrDefault(l => ReferenceEquals(l.Cards, collection));
}

/// <summary>Reorders lists; dropping a card on a list header/collapsed list appends it to that list.</summary>
public sealed class ListDropHandler : DefaultDropHandler
{
    public override void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is Card)
        {
            if (dropInfo.TargetItem is BoardList)
            {
                dropInfo.Effects = DragDropEffects.Move;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
            }
            else
            {
                dropInfo.Effects = DragDropEffects.None;
            }

            return;
        }

        if (dropInfo.Data is not BoardList)
        {
            dropInfo.Effects = DragDropEffects.None;
            return;
        }

        base.DragOver(dropInfo);
        if (dropInfo.Effects != DragDropEffects.None) dropInfo.Effects = DragDropEffects.Move;
    }

    public override void Drop(IDropInfo dropInfo)
    {
        var main = MainViewModel.Instance;
        var board = main.CurrentBoard;
        if (board == null) return;

        if (dropInfo.Data is Card card && dropInfo.TargetItem is BoardList targetList)
        {
            var source = board.FindListOf(card);
            if (source == targetList) return;
            main.Undo.Checkpoint(main.Workspace, board, "Move card");
            source?.Cards.Remove(card);
            targetList.Cards.Add(card);
            main.AfterCardMoved(card, source, targetList);
            return;
        }

        if (dropInfo.Data is BoardList)
        {
            main.Undo.Checkpoint(main.Workspace, board, "Move list");
            base.Drop(dropInfo);
        }
    }
}

/// <summary>Reschedules cards by dragging them onto calendar days (or the "no date" tray).</summary>
public sealed class CalendarDropHandler : IDropTarget
{
    private readonly CalendarViewModel _calendar;

    public CalendarDropHandler(CalendarViewModel calendar) => _calendar = calendar;

    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is Card)
        {
            dropInfo.Effects = DragDropEffects.Move;
            dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
        }
        else
        {
            dropInfo.Effects = DragDropEffects.None;
        }
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is not Card card) return;
        var target = dropInfo.TargetCollection;
        var day = _calendar.Days.FirstOrDefault(d => ReferenceEquals(d.Cards, target));
        if (day == null && dropInfo.TargetItem is Card other)
            day = _calendar.Days.FirstOrDefault(d => d.Cards.Contains(other));

        if (day != null) _calendar.Reschedule(card, day.Date);
        else if (ReferenceEquals(target, _calendar.Unscheduled)) _calendar.Reschedule(card, null);
    }
}
