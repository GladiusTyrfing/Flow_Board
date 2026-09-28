using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowBoard.Models;

namespace FlowBoard.ViewModels;

public partial class FilterLabelOption : ObservableObject
{
    public FilterLabelOption(Label label, bool selected)
    {
        Label = label;
        _isSelected = selected;
    }

    public Label Label { get; }
    [ObservableProperty] private bool _isSelected;
}

/// <summary>Board filter: text, labels, priority, due window and completion.</summary>
public partial class FilterState : ObservableObject
{
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private DueFilter _due = DueFilter.Any;
    [ObservableProperty] private Priority _minPriority = Priority.None;
    [ObservableProperty] private bool _hideCompleted;
    [ObservableProperty] private ObservableCollection<FilterLabelOption> _labelOptions = [];

    private bool _suppress;

    public event EventHandler? Changed;

    public bool IsActive =>
        !string.IsNullOrWhiteSpace(Text) || Due != DueFilter.Any || MinPriority != Priority.None || HideCompleted
        || LabelOptions.Any(o => o.IsSelected);

    public int ActiveCount =>
        (string.IsNullOrWhiteSpace(Text) ? 0 : 1) + (Due != DueFilter.Any ? 1 : 0) + (MinPriority != Priority.None ? 1 : 0)
        + (HideCompleted ? 1 : 0) + LabelOptions.Count(o => o.IsSelected);

    partial void OnTextChanged(string value) => Raise();
    partial void OnDueChanged(DueFilter value) => Raise();
    partial void OnMinPriorityChanged(Priority value) => Raise();
    partial void OnHideCompletedChanged(bool value) => Raise();

    public void SetBoard(Board? board)
    {
        var selected = LabelOptions.Where(o => o.IsSelected).Select(o => o.Label.Id).ToHashSet();
        foreach (var o in LabelOptions) o.PropertyChanged -= OnOptionChanged;
        var fresh = new ObservableCollection<FilterLabelOption>();
        if (board != null)
            foreach (var l in board.Labels)
                fresh.Add(new FilterLabelOption(l, selected.Contains(l.Id)));
        foreach (var o in fresh) o.PropertyChanged += OnOptionChanged;
        LabelOptions = fresh;
        Raise();
    }

    private void OnOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Raise();

    public void Clear()
    {
        _suppress = true;
        Text = string.Empty;
        Due = DueFilter.Any;
        MinPriority = Priority.None;
        HideCompleted = false;
        foreach (var o in LabelOptions) o.IsSelected = false;
        _suppress = false;
        Raise();
    }

    public bool Matches(Card card)
    {
        if (HideCompleted && card.IsCompleted) return false;
        if (MinPriority != Priority.None && card.Priority < MinPriority) return false;

        var labelIds = LabelOptions.Where(o => o.IsSelected).Select(o => o.Label.Id).ToList();
        if (labelIds.Count > 0 && !labelIds.Any(card.LabelIds.Contains)) return false;

        if (Due != DueFilter.Any)
        {
            var today = DateTime.Today;
            var due = card.DueDate;
            bool ok = Due switch
            {
                DueFilter.NoDate => due == null,
                DueFilter.HasDate => due != null,
                DueFilter.Overdue => due != null && !card.IsCompleted && Card.EffectiveDue(due.Value) < DateTime.Now,
                DueFilter.Today => due?.Date == today,
                DueFilter.ThisWeek => due != null && due.Value.Date >= today && due.Value.Date < today.AddDays(7),
                _ => true,
            };
            if (!ok) return false;
        }

        if (!string.IsNullOrWhiteSpace(Text))
        {
            var t = Text.Trim();
            bool hit = card.Title.Contains(t, StringComparison.OrdinalIgnoreCase)
                       || card.Description.Contains(t, StringComparison.OrdinalIgnoreCase)
                       || card.ResolvedLabels.Any(l => l.Name.Contains(t, StringComparison.OrdinalIgnoreCase))
                       || card.Checklists.Any(c => c.Items.Any(i => i.Text.Contains(t, StringComparison.OrdinalIgnoreCase)));
            if (!hit) return false;
        }

        return true;
    }

    private void Raise()
    {
        if (_suppress) return;
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(ActiveCount));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
