using CommunityToolkit.Mvvm.ComponentModel;

namespace FlowBoard.Models;

/// <summary>A board-level label. Cards reference labels by id so renaming or recoloring updates every card.</summary>
public partial class Label : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _color = "#4BCE97";

    public static readonly string[] Palette =
    [
        "#4BCE97", "#F5CD47", "#FEA362", "#F87168", "#9F8FEF", "#579DFF",
        "#6CC3E0", "#94C748", "#E774BB", "#8590A2", "#1F845A", "#946F00",
        "#C25100", "#C9372C", "#6E5DC6", "#0C66E4", "#227D9B", "#5B7F24",
    ];
}
