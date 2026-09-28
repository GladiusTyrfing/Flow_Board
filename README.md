# FlowBoard

A fast, good-looking **Kanban task manager for Windows**, in the spirit of Trello and Zenkit — written in C# / WPF with a Windows 11 Fluent (Mica) look.
**Fully local:** no account, no cloud, no internet needed. Your boards live on your PC.

## Features

**Boards, lists & cards**
- Unlimited boards, created from templates (Basic Kanban, Project, Bug tracker, Weekly planner, Content pipeline, Personal, Blank) or from your own saved templates
- Add as many lists as you like (not just To Do / Doing / Done) — rename, reorder by drag & drop, collapse, color, sort, copy, archive
- Work-in-progress limits per list, and "done" lists that auto-complete cards dropped into them
- Drag & drop cards between lists; drag the empty board background to scroll sideways
- Star boards, duplicate, export/import a board, per-board wallpaper (your own photo, gradients or colors) with dimming

**Cards (like Trello's card back)**
- Title, description, completion checkbox, labels (board-wide, colored, renamable), priority (Low → Urgent)
- Start / due dates with time, quick picks (Today, Tomorrow, Weekend, Next week) and reminders
- Multiple checklists with progress bars; paste many lines to add many items; convert an item into a card
- **Attachments:** any file, drag & drop from Explorer, or **Ctrl+V to paste screenshots**; image thumbnails, image preview, card covers
- **Voice notes:** record from your microphone with a live level meter, play back with seek
- Links, cover colors/images, comments and an automatic activity log
- **Time tracking** (start/stop timer, manual log) and a **Pomodoro focus timer** that logs time to the card

**Views**
- **Board** (Kanban), **Table** (sortable spreadsheet, all boards optionally) and **Calendar** (month grid; drag cards to reschedule, "no date" tray)

**Quality of life**
- Command palette / global search (Ctrl+K) across every board and card
- Filter by keyword, labels, due window, priority, completion
- Undo / redo for everything (Ctrl+Z / Ctrl+Y) + archive with restore
- Windows notifications for due-date reminders and Pomodoro phases, tray icon, "keep running in tray", start with Windows
- **Global hotkeys** that work from any app (quick add a card, show/hide) — customizable
- Dark / light / follow-Windows theme, accent colors, Mica / Acrylic backdrops, smooth animations
- Autosave every 2 seconds, rolling automatic backups, full zip backup & restore, portable mode

## Hotkeys

Press **F1** in the app for the full cheat sheet. Highlights:

| Where | Keys | Action |
|---|---|---|
| Anywhere in Windows | **Ctrl+Alt+Space** | Quick add a card (pops up over any app) |
| Anywhere in Windows | **Ctrl+Alt+F** | Show / hide FlowBoard |
| App | Ctrl+K or / | Command palette & search |
| App | Ctrl+F | Filter |
| App | Ctrl+N or N | New card · Ctrl+Shift+N new board · Ctrl+Shift+L new list |
| App | Ctrl+Z / Ctrl+Y | Undo / redo |
| App | Ctrl+Tab · Alt+1…9 | Next board · jump to board |
| App | Ctrl+1 / 2 / 3 | Board / Table / Calendar |
| App | Ctrl+B or [ · Ctrl+T · Ctrl+P | Sidebar · theme · focus timer |
| Hover a card | Enter · X · C · Delete · Ctrl+D | Open · complete · archive · delete · duplicate |
| Hover a card | 1…9 · P · T · F · D · L | Toggle label · cycle priority · timer · focus · dates · labels |
| Hover a card | Alt+← / → · Alt+↑ / ↓ | Move to previous/next list · move up/down |
| Open card | L D P B M K A R T F X | Labels, Dates, Priority, Cover, Move, Checklist, Attach, Record, Timer, Focus, Complete |
| Open card | Ctrl+V | Paste image / files as attachments |

Global hotkeys can be changed (or turned off) in **Settings → Hotkeys**.

## Getting the app

- **Download:** every push builds `FlowBoard.exe` on GitHub Actions (*Actions → Build FlowBoard → Artifacts*). It's a single self-contained file — no .NET install needed. Windows 10 (1809+) or 11, x64.
- **Build yourself:** install the [.NET 8 SDK](https://dotnet.microsoft.com/download) and run

  ```powershell
  dotnet run --project src/FlowBoard
  # or a single-file exe:
  dotnet publish src/FlowBoard -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
  ```

  You can also open `FlowBoard.sln` in Visual Studio 2022 and press F5.

## Where is my data?

`%AppData%\FlowBoard` — `data.json` (boards), `settings.json`, `attachments\`, `backgrounds\`, `backups\`.
Create an empty `portable.txt` next to `FlowBoard.exe` to keep everything in a `Data` folder beside the exe instead (e.g. on a USB stick or a synced folder).

## Project layout

```
src/FlowBoard/
  Models/        Board, BoardList, Card, Label, Checklist, Attachment, Comment/TimeEntry, settings
  Services/      JSON storage & backups, undo, templates, audio record/playback, reminders,
                 Pomodoro, time tracking, tray, theme, global hotkeys, start-with-Windows
  ViewModels/    MainViewModel (+ Cards, App, Hotkeys partials), card details, calendar, table,
                 command palette, quick add, settings, drag & drop handlers
  Views/         Board (Kanban), Table, Calendar, card details, dialogs
  Themes/        Dark/light palettes and shared styles
tests/FlowBoard.Tests/   Tests for models, storage, undo, templates and filters (run anywhere with `dotnet test`)
```

Built with [WPF-UI](https://github.com/lepoco/wpfui) (Fluent design), [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet), [gong-wpf-dragdrop](https://github.com/punker76/gong-wpf-dragdrop) and [NAudio](https://github.com/naudio/NAudio).
