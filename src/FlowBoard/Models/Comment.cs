using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FlowBoard.Models;

public partial class Comment : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _author = string.Empty;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private DateTime _createdAt = DateTime.Now;
    /// <summary>True for automatic activity entries ("moved this card from To Do to Done").</summary>
    [ObservableProperty] private bool _isActivity;
}

public partial class TimeEntry : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private DateTime _start;
    [ObservableProperty] private DateTime _end;
    [ObservableProperty] private string _source = "Timer";

    [JsonIgnore]
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;
}
