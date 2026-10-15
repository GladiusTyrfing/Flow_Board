using System.ComponentModel;
using System.Windows.Controls;
using FlowBoard.Models;
using FlowBoard.Services;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

public partial class BoardStyleEditor : UserControl
{
    private Board? _board;

    public BoardStyleEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
        Loaded += (_, _) =>
        {
            ThemeService.ThemeApplied += OnThemeApplied;
            Hook();
        };
        Unloaded += (_, _) =>
        {
            ThemeService.ThemeApplied -= OnThemeApplied;
            if (_board != null) _board.PropertyChanged -= OnBoardChanged;
            _board = null;
        };
    }

    private void Hook()
    {
        if (_board != null) _board.PropertyChanged -= OnBoardChanged;
        _board = (DataContext as BoardSettingsViewModel)?.Board;
        if (_board != null) _board.PropertyChanged += OnBoardChanged;
        RefreshPreview();
    }

    private void OnThemeApplied(object? sender, EventArgs e) => RefreshPreview();

    private void OnBoardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Board.Theme) or nameof(Board.ListOpacity) or nameof(Board.CardOpacity) or nameof(Board.CornerRadius))
            RefreshPreview();
    }

    private void RefreshPreview() => BoardThemeService.Apply(PreviewHost, _board);
}
