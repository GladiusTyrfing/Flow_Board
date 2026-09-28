using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;
using GongSolutions.Wpf.DragDrop;

namespace FlowBoard.ViewModels;

/// <summary>
/// Root view model. Split into partial files: boards (this file), lists &amp; cards, and app-level features.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public static MainViewModel Instance { get; private set; } = null!;

    private readonly DataStore _store;
    private readonly DispatcherTimer _filterDebounce;

    public MainViewModel(DataStore store)
    {
        Instance = this;
        _store = store;
        Undo = new UndoService();
        Pomodoro = new PomodoroService(store.Settings);
        Timer = new TimeTracker();
        Player = new AudioPlayer();
        Recorder = new AudioRecorder();
        Calendar = new CalendarViewModel(this);
        Table = new TableViewModel(this);
        Timeline = new TimelineViewModel(this);
        CardDropHandler = new CardDropHandler();
        ListDropHandler = new ListDropHandler();

        _filterDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _filterDebounce.Tick += (_, _) =>
        {
            _filterDebounce.Stop();
            ApplyFilter();
        };
        Filter.Changed += (_, _) =>
        {
            _filterDebounce.Stop();
            _filterDebounce.Start();
        };

        Pomodoro.FocusLogged += OnFocusLogged;
        Pomodoro.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PomodoroService.IsRunning)) PomodoroStateChanged?.Invoke(this, EventArgs.Empty);
        };
        Workspace.Boards.CollectionChanged += (_, _) => RefreshSidebar();
        Workspace.Storyboards.CollectionChanged += (_, _) => RefreshDocSidebar();
        Workspace.Canvases.CollectionChanged += (_, _) => RefreshDocSidebar();
        Workspace.Notes.CollectionChanged += (_, _) => RefreshDocSidebar();
        Settings.PropertyChanged += OnSettingsChanged;
        Undo.PropertyChanged += (_, _) => RaiseUndoState();

        // The app always starts with no project open, on the home screen.
        OpenDashboard();
    }

    public DataStore Store => _store;
    public Workspace Workspace => _store.Workspace;
    public AppSettings Settings => _store.Settings;
    public UndoService Undo { get; }
    public PomodoroService Pomodoro { get; }
    public TimeTracker Timer { get; }
    public AudioPlayer Player { get; }
    public AudioRecorder Recorder { get; }
    public CalendarViewModel Calendar { get; }
    public TableViewModel Table { get; }
    public TimelineViewModel Timeline { get; }
    public FilterState Filter { get; } = new();
    public IDropTarget CardDropHandler { get; }
    public IDropTarget ListDropHandler { get; }

    /// <summary>Overlay dialog stack (card details, settings, prompts...). Last item is on top.</summary>
    public ObservableCollection<DialogViewModel> Dialogs { get; } = [];
    public ObservableCollection<ToastMessage> Toasts { get; } = [];

    public event EventHandler? PomodoroStateChanged;
    public event EventHandler? ViewChanged;
    public event EventHandler? FocusFilterRequested;

    [ObservableProperty] private Board? _currentBoard;
    [ObservableProperty] private BoardViewMode _viewMode = BoardViewMode.Board;
    [ObservableProperty] private bool _isFilterOpen;
    [ObservableProperty] private bool _isAddingList;
    [ObservableProperty] private string _newListName = string.Empty;
    [ObservableProperty] private string _sidebarQuery = string.Empty;

    /// <summary>Narrow window: header and title bar show icons only so nothing gets cut off.</summary>
    [ObservableProperty] private bool _isCompact;

    /// <summary>Window is too narrow for the sidebar: it hides automatically unless opened on purpose.</summary>
    [ObservableProperty] private bool _isNarrow;
    [ObservableProperty] private bool _narrowSidebarOpen;

    public bool IsSidebarShown => IsNarrow ? NarrowSidebarOpen : Settings.SidebarVisible;

    partial void OnIsNarrowChanged(bool value)
    {
        NarrowSidebarOpen = false;
        OnPropertyChanged(nameof(IsSidebarShown));
    }

    partial void OnNarrowSidebarOpenChanged(bool value) => OnPropertyChanged(nameof(IsSidebarShown));
    [ObservableProperty] private double _boardNameMaxWidth = 420;

    public bool HasDialog => Dialogs.Count > 0;
    public bool HasBoards => Workspace.Boards.Count > 0;
    public string DisplayName => Settings.DisplayName;

    public IEnumerable<Board> StarredBoards => FilterSidebar(Workspace.Boards.Where(b => b.IsStarred));
    public IEnumerable<Board> AllBoards => FilterSidebar(Workspace.Boards);

    private IEnumerable<Board> FilterSidebar(IEnumerable<Board> boards) =>
        string.IsNullOrWhiteSpace(SidebarQuery)
            ? boards
            : boards.Where(b => b.Name.Contains(SidebarQuery.Trim(), StringComparison.OrdinalIgnoreCase));

    partial void OnSidebarQueryChanged(string value) => RefreshSidebar();

    public void RefreshSidebar()
    {
        RefreshDocSidebar();
        OnPropertyChanged(nameof(StarredBoards));
        OnPropertyChanged(nameof(AllBoards));
        OnPropertyChanged(nameof(HasBoards));
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.Theme):
            case nameof(AppSettings.AccentColor):
            case nameof(AppSettings.Backdrop):
            case nameof(AppSettings.AccentPreset):
                ThemeService.Apply();
                break;
            case nameof(AppSettings.CornerRadius):
                ThemeService.QueueRadiusUpdate();
                break;
            case nameof(AppSettings.StartWithWindows):
                StartupService.Apply(Settings.StartWithWindows);
                break;
            case nameof(AppSettings.Animations):
                Helpers.Behaviors.AnimationsEnabled = Settings.Animations;
                break;
            case nameof(AppSettings.SidebarVisible):
                OnPropertyChanged(nameof(IsSidebarShown));
                break;
            case nameof(AppSettings.DisplayName):
                OnPropertyChanged(nameof(DisplayName));
                break;
            case nameof(AppSettings.ShowCompletedInCalendar):
                Calendar.Rebuild();
                break;
        }
    }

    // ================= Boards =================

    public void SelectBoard(Board? board)
    {
        LeaveDocument();
        if (Dialogs.OfType<CardDetailViewModel>().Any()) CloseAllDialogs();
        CurrentBoard = board;
        if (board != null)
        {
            board.LastOpened = DateTime.Now;
            Settings.LastBoardId = board.Id;
            ViewMode = board.ViewMode;
        }

        Filter.SetBoard(board);
        ApplyFilter();
        RefreshViews();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenBoard(Board board) => SelectBoard(board);

    partial void OnViewModeChanged(BoardViewMode value)
    {
        if (CurrentBoard != null) CurrentBoard.ViewMode = value;
        RefreshViews();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SetViewMode(BoardViewMode mode)
    {
        if (ActiveView != ActiveView.Board) SelectBoard(CurrentBoard);
        ViewMode = mode;
    }

    partial void OnCurrentBoardChanged(Board? value) => OnPropertyChanged(nameof(SelectedItem));

    public void RefreshViews()
    {
        if (ViewMode == BoardViewMode.Calendar) Calendar.Rebuild();
        else if (ViewMode == BoardViewMode.Table) Table.Rebuild();
        else if (ViewMode == BoardViewMode.Timeline) Timeline.Rebuild();
    }

    [RelayCommand]
    private void NewBoard()
    {
        if (!EnsureProject()) return;
        var templates = TemplateService.BuiltIn.Concat(Workspace.UserTemplates);
        ShowDialog(new NewBoardViewModel(templates));
    }

    public void CreateBoardFromTemplate(BoardTemplate template, string name, string background)
    {
        var board = TemplateService.Create(template, name, background);
        Undo.CheckpointBoardCreated(board.Id, $"Create board \"{board.Name}\"");
        Workspace.Boards.Add(board);
        SelectBoard(board);
        ShowToast($"Board \"{board.Name}\" created");
    }

    [RelayCommand]
    private void StartRenameBoard()
    {
        if (ActiveView != ActiveView.Board)
        {
            ActiveDocument?.BeginRename();
            return;
        }

        if (CurrentBoard != null) CurrentBoard.IsEditingName = true;
    }

    [RelayCommand]
    private void CommitBoardName()
    {
        if (CurrentBoard == null) return;
        CurrentBoard.IsEditingName = false;
        if (string.IsNullOrWhiteSpace(CurrentBoard.Name)) CurrentBoard.Name = "Untitled board";
        RefreshSidebar();
    }

    [RelayCommand]
    private void ToggleStar(Board? board)
    {
        board ??= CurrentBoard;
        if (board == null) return;
        board.IsStarred = !board.IsStarred;
        RefreshSidebar();
    }

    [RelayCommand]
    private void DuplicateBoard(Board? board)
    {
        board ??= CurrentBoard;
        if (board == null) return;
        var copy = Json.CloneWithNewIds(board, includeCards: true);
        foreach (var card in copy.AllActiveCards)
        {
            // Copies must not share attachment files with the original.
            card.Attachments.Clear();
            card.CoverAttachmentId = null;
        }

        copy.Name = board.Name + " (copy)";
        copy.IsStarred = false;
        Undo.CheckpointBoardCreated(copy.Id, "Duplicate board");
        Workspace.Boards.Insert(Workspace.Boards.IndexOf(board) + 1, copy);
        SelectBoard(copy);
        ShowToast("Board duplicated (attachments aren't copied)");
    }

    [RelayCommand]
    private async Task DeleteBoard(Board? board)
    {
        board ??= CurrentBoard;
        if (board == null) return;
        var ok = await ConfirmAsync(
            "Delete board?",
            $"\"{board.Name}\" with {board.ActiveCardCount} cards will be deleted. You can undo this with Ctrl+Z.",
            "Delete board", danger: true);
        if (!ok) return;

        Undo.Checkpoint(Workspace, board, $"Delete board \"{board.Name}\"");
        var idx = Workspace.Boards.IndexOf(board);
        Workspace.Boards.Remove(board);
        if (CurrentBoard == board)
            SelectBoard(Workspace.Boards.Count == 0 ? null : Workspace.Boards[Math.Clamp(idx, 0, Workspace.Boards.Count - 1)]);
        ShowToast($"Deleted \"{board.Name}\"", "Undo", () => UndoCommand.Execute(null));
    }

    [RelayCommand]
    private void OpenBoardSettings(string? tab)
    {
        if (CurrentBoard == null) return;
        if (Dialogs.OfType<BoardSettingsViewModel>().FirstOrDefault() is { } open) CloseDialog(open);
        ShowDialog(new BoardSettingsViewModel(this, CurrentBoard, tab ?? "style"));
    }

    [RelayCommand]
    private void OpenArchive()
    {
        if (CurrentBoard != null) ShowDialog(new ArchiveViewModel(CurrentBoard));
    }

    [RelayCommand]
    private async Task SaveAsTemplate()
    {
        if (CurrentBoard == null) return;
        var name = await PromptAsync("Save as template", "Lists, labels and cards are saved so you can create new boards from this one.", "Template name", "Save template", CurrentBoard.Name);
        if (name == null) return;
        Workspace.UserTemplates.Add(TemplateService.SaveAsTemplate(CurrentBoard, name, includeCards: true));
        ShowToast($"Template \"{name}\" saved — find it under New board");
    }

    // ================= Filter =================

    [RelayCommand]
    private void OpenFilter()
    {
        if (ActiveView != ActiveView.Board) return;
        IsFilterOpen = true;
        FocusFilterRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearFilter() => Filter.Clear();

    public void ApplyFilter()
    {
        if (CurrentBoard == null) return;
        RefreshBlocked();
        foreach (var list in CurrentBoard.Lists)
        {
            foreach (var card in list.Cards) card.IsFilteredOut = !Filter.Matches(card);
            list.RaiseCounts();
        }

        RefreshViews();
    }

    /// <summary>Marks cards on the current board that wait on unfinished "blocked by" cards (which may live on other boards).</summary>
    public void RefreshBlocked()
    {
        if (CurrentBoard == null) return;
        Dictionary<Guid, bool>? done = null;
        foreach (var card in CurrentBoard.AllActiveCards)
        {
            if (card.BlockedByIds.Count == 0)
            {
                card.IsBlocked = false;
                continue;
            }

            done ??= Workspace.EnumerateActiveCards().GroupBy(t => t.Card.Id).ToDictionary(g => g.Key, g => g.First().Card.IsCompleted);
            card.IsBlocked = !card.IsCompleted && card.BlockedByIds.Any(id => done.TryGetValue(id, out var d) && !d);
        }
    }
}
