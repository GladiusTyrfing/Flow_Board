using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Models;
using FlowBoard.Services;
using Microsoft.Win32;

namespace FlowBoard.ViewModels;

public sealed partial class MainViewModel
{
    // ================= Dialog stack =================

    public void ShowDialog(DialogViewModel dialog)
    {
        Dialogs.Add(dialog);
        OnPropertyChanged(nameof(HasDialog));
    }

    public void CloseDialog(DialogViewModel dialog)
    {
        if (!Dialogs.Remove(dialog)) return;
        OnPropertyChanged(nameof(HasDialog));
        dialog.OnClosed();
    }

    public void CloseAllDialogs()
    {
        foreach (var d in Dialogs.Reverse().ToList()) CloseDialog(d);
    }

    [RelayCommand]
    private void CloseTopDialog()
    {
        if (Dialogs.Count > 0)
        {
            CloseDialog(Dialogs[^1]);
            return;
        }

        if (IsFilterOpen)
        {
            IsFilterOpen = false;
            return;
        }

        if (IsAddingList) CancelAddList();
        if (CurrentBoard != null)
            foreach (var l in CurrentBoard.Lists)
                l.IsAddingCard = false;
    }

    [RelayCommand]
    private void BackdropClicked(DialogViewModel dialog)
    {
        if (dialog.CloseOnBackdropClick) CloseDialog(dialog);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool danger = false)
    {
        var vm = new ConfirmDialogViewModel { Title = title, Message = message, ConfirmText = confirmText, IsDanger = danger };
        ShowDialog(vm);
        return await vm.Result;
    }

    public async Task<string?> PromptAsync(string title, string message, string placeholder, string confirmText, string initial = "")
    {
        var vm = new TextPromptViewModel { Title = title, Message = message, Placeholder = placeholder, ConfirmText = confirmText, Text = initial };
        ShowDialog(vm);
        return await vm.Result;
    }

    // ================= Toasts =================

    public void ShowToast(string text, string? actionText = null, Action? action = null, bool isError = false)
    {
        var toast = new ToastMessage { Text = text, ActionText = actionText, Action = action, IsError = isError };
        Toasts.Add(toast);
        while (Toasts.Count > 3) Toasts.RemoveAt(0);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(action != null ? 7 : 4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Toasts.Remove(toast);
        };
        timer.Start();
    }

    public void ShowToast(string text, bool isError) => ShowToast(text, null, null, isError);

    // ================= Undo / Redo =================

    [RelayCommand]
    private void DoUndo() => UndoCore(redo: false);

    [RelayCommand]
    private void DoRedo() => UndoCore(redo: true);

    // Friendlier names for bindings and the command palette.
    public IRelayCommand UndoCommand => DoUndoCommand;
    public IRelayCommand RedoCommand => DoRedoCommand;

    private void UndoCore(bool redo)
    {
        // Close editors first so their pending edits become their own undo step and nothing edits a stale board.
        CloseAllDialogs();
        var currentId = CurrentBoard?.Id;
        var restored = redo ? Undo.Redo(Workspace, out var desc) : Undo.Undo(Workspace, out desc);
        if (desc == null)
        {
            ShowToast(redo ? "Nothing to redo" : "Nothing to undo");
            return;
        }

        Timer.Rebind(Workspace);
        var target = restored
                     ?? Workspace.Boards.FirstOrDefault(b => b.Id == currentId)
                     ?? Workspace.Boards.FirstOrDefault();
        SelectBoard(target);
        RefreshSidebar();
        ShowToast($"{(redo ? "Redid" : "Undid")}: {desc}");
    }

    // ================= Appearance & layout =================

    [RelayCommand]
    private void ToggleTheme()
    {
        Settings.Theme = ThemeService.IsDark ? ThemeMode.Light : ThemeMode.Dark;
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        if (IsNarrow) NarrowSidebarOpen = !NarrowSidebarOpen;
        else Settings.SidebarVisible = !Settings.SidebarVisible;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        if (Dialogs.OfType<SettingsViewModel>().Any()) return;
        ShowDialog(new SettingsViewModel(this));
    }

    [RelayCommand]
    private void OpenShortcuts()
    {
        if (Dialogs.OfType<ShortcutsViewModel>().Any()) return;
        ShowDialog(new ShortcutsViewModel());
    }

    [RelayCommand]
    private void OpenCommandPalette()
    {
        if (Dialogs.OfType<CommandPaletteViewModel>().FirstOrDefault() is { } existing)
        {
            CloseDialog(existing);
            return;
        }

        ShowDialog(new CommandPaletteViewModel(this));
    }

    // ================= Pomodoro & timer =================

    [RelayCommand]
    private void TogglePomodoro() => Pomodoro.Toggle();

    [RelayCommand]
    private void ResetPomodoro() => Pomodoro.Reset();

    [RelayCommand]
    private void SkipPomodoro() => Pomodoro.Skip();

    [RelayCommand]
    private void UnlinkPomodoroCard() => Pomodoro.LinkCard(null);

    [RelayCommand]
    private void OpenPomodoroCard()
    {
        if (Pomodoro.LinkedCardId is { } id && Workspace.FindCard(id, out var board, out _) is { } card)
        {
            if (board != CurrentBoard) SelectBoard(board);
            OpenCard(card);
        }
    }

    [RelayCommand]
    private void StopTimer() => Timer.Stop();

    [RelayCommand]
    private void OpenTimerCard()
    {
        if (Timer.Card is { } card && card.Board is { } board)
        {
            if (board != CurrentBoard) SelectBoard(board);
            OpenCard(card);
        }
    }

    // ================= Data =================

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, isError: true);
        }
    }

    [RelayCommand]
    private void BackupNow()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Back up FlowBoard",
            FileName = $"FlowBoard backup {DateTime.Now:yyyy-MM-dd}.zip",
            Filter = "Zip archive|*.zip",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _store.ExportZip(dlg.FileName);
            ShowToast("Backup saved", "Show", () => Process.Start("explorer.exe", $"/select,\"{dlg.FileName}\""));
        }
        catch (Exception ex)
        {
            ShowToast($"Backup failed: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private async Task RestoreBackup()
    {
        var dlg = new OpenFileDialog { Title = "Restore FlowBoard backup", Filter = "Zip archive|*.zip" };
        if (dlg.ShowDialog() != true) return;
        var ok = await ConfirmAsync(
            "Restore backup?",
            "Your current boards will be replaced by the backup. A safety copy of the current data is saved to the backups folder first. FlowBoard will restart.",
            "Restore and restart", danger: true);
        if (!ok) return;
        try
        {
            _store.ImportZip(dlg.FileName);
            App.Restart();
        }
        catch (Exception ex)
        {
            ShowToast($"Restore failed: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void ExportBoard()
    {
        if (CurrentBoard == null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Export board",
            FileName = $"{string.Concat(CurrentBoard.Name.Split(Path.GetInvalidFileNameChars()))}.flowboard.json",
            Filter = "FlowBoard board|*.json",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(CurrentBoard, Json.Options));
            ShowToast("Board exported (attachments are not included — use Backup for everything)");
        }
        catch (Exception ex)
        {
            ShowToast($"Export failed: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void ImportBoard()
    {
        var dlg = new OpenFileDialog { Title = "Import board", Filter = "FlowBoard board|*.json|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var board = Json.DeserializeBoard(File.ReadAllText(dlg.FileName));
            var fresh = Json.CloneWithNewIds(board, includeCards: true);
            foreach (var c in fresh.AllActiveCards)
            {
                c.Attachments.Clear();
                c.CoverAttachmentId = null;
            }

            if (fresh.Background.StartsWith("image:") && !File.Exists(AppPaths.ToFull(fresh.Background[6..])))
                fresh.Background = Board.GradientPresets[1];
            Undo.CheckpointBoardCreated(fresh.Id, "Import board");
            Workspace.Boards.Add(fresh);
            SelectBoard(fresh);
            ShowToast($"Imported \"{fresh.Name}\"");
        }
        catch (Exception ex)
        {
            ShowToast($"Import failed: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void DeleteCommentFromOpenCard(Comment comment)
    {
        foreach (var d in Dialogs.OfType<CardDetailViewModel>())
            d.Card.Comments.Remove(comment);
    }

    [RelayCommand]
    private void OpenAttachmentExternally(Attachment? a)
    {
        var target = a?.Kind == AttachmentKind.Link ? a.Url : a?.FullPath;
        if (target == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowToast($"Couldn't open: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void SaveNow()
    {
        _store.SaveIfChanged();
        ShowToast("All changes saved");
    }

    public event EventHandler? HideWindowRequested;

    public void RequestHideWindow() => HideWindowRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Opens the quick-add box (used by the global hotkey and the tray menu).</summary>
    public void ShowQuickAdd(bool hideAfter)
    {
        if (Workspace.Boards.Count == 0)
        {
            NewBoard();
            return;
        }

        if (Dialogs.OfType<QuickAddViewModel>().Any()) return;
        ShowDialog(new QuickAddViewModel(this, hideAfter));
    }

    [RelayCommand]
    private void OpenQuickAdd() => ShowQuickAdd(hideAfter: false);

    public void ShowStartupWarning()
    {
        if (_store.LoadWarning is { } w) ShowToast(w, isError: true);
    }

    public static void RunOnUi(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);
}
