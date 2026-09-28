# FlowBoard

A fast, good-looking **Kanban task manager and creative workspace for Windows**, in the spirit of Trello, Zenkit, Notion and Miro — written in C# / WPF with a Fluent look (Windows 10 and 11).
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
- **Board** (Kanban), **Table** (sortable spreadsheet, all boards optionally), **Calendar** (month grid; drag cards to reschedule, "no date" tray)
- **Timeline / Gantt**: cards as bars from start to due date, grouped by list — drag a bar to move it, drag its edges to change dates, zoom, "today" line, dependency arrows (red when a card starts before its blocker ends)

**Linked cards & dependencies**
- Link related cards on any board, and mark cards as *blocked by* / *blocks* other cards (loops are refused)
- Blocked cards show a lock badge on the board and in the timeline; links open the other card in one click

**Storyboards** (switch between **Film** and **Animation**)
- A row of shot columns joined by connectors: frame image (pick, paste, drop, or **sketch** with pen/tablet — also over a photo), scene, date, description, tags
- Film: shot type, angle, movement, lens, location, a "shots required" checklist and an equipment list
- Animation: action, dialogue, timing per frame, transition (cut, fade, dissolve, wipe) and a **recorded voice line**
- **Animatic player**: plays the frames with their timing and voice lines; export the storyboard as PNG, print it, or turn every shot into a card

**Canvas & flowcharts**
- Infinite canvas with pan/zoom and a dot grid; boxes, rounded boxes, ellipses, decision diamonds, sticky notes, text, images and **live cards** from your boards
- Drag from a shape's dot to connect (drop on empty space to create the next shape); curved, straight or elbow connectors with arrows, labels, colors, dashes
- Mind-map keys (Tab = child, Enter = sibling), snap to grid, marquee select, copy/paste/duplicate, paste or drop images, **one-click tidy-up layout**, export PNG / print

**Notes / docs pages**
- Block editor: headings, to-dos, bulleted and numbered lists, quotes, callouts, code, dividers, images and links to cards, boards, storyboards, canvases or other pages
- Type `/` for the block menu or use Markdown shortcuts (`#`, `-`, `1.`, `[]`, `>`, ```` ``` ````, `---`); Tab to indent, Alt+↑/↓ to move blocks
- Page icons, word count and reading time, export/copy as Markdown, turn open to-dos into cards

**Dashboard**
- Open / done-this-week / overdue / due-soon / focus time / streak tiles, a 14-day "completed per day" chart, open cards by priority, time per board, an "up next" agenda and "jump back in"

**Quality of life**
- Command palette / global search (Ctrl+K) across every board, card, storyboard, canvas and note (including text inside pages)
- **Smart quick add**: type `Call Sam tomorrow 3pm #work !high` and the date, time, label and priority are filled in for you
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
| App | Ctrl+1 / 2 / 3 / 4 | Board / Table / Calendar / Timeline |
| App | Ctrl+B or [ · Ctrl+T · Ctrl+P | Sidebar · theme · focus timer |
| Hover a card | Enter · X · C · Delete · Ctrl+D | Open · complete · archive · delete · duplicate |
| Hover a card | 1…9 · P · T · F · D · L | Toggle label · cycle priority · timer · focus · dates · labels |
| Hover a card | Alt+← / → · Alt+↑ / ↓ | Move to previous/next list · move up/down |
| Open card | L D P B M K A R T F X | Labels, Dates, Priority, Cover, Move, Checklist, Attach, Record, Timer, Focus, Complete |
| Open card | Ctrl+V | Paste image / files as attachments |

| Canvas | Double-click · drag a dot · Tab / Enter | Add shape · connect · child / sibling |
| Canvas | V R B O D S T C I · Space+drag · Ctrl+wheel | Tools · pan · zoom |
| Notes | / · # - 1. [] > | Block menu · Markdown shortcuts |
| Animatic | Space · ← → · Esc | Play/pause · step · close |

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

`%AppData%\FlowBoard` — `data.json` (boards, storyboards, canvases, notes), `settings.json`, `attachments\` (card files; storyboard/canvas/note images and sketches under `attachments\docs\`), `backgrounds\`, `backups\`.
Create an empty `portable.txt` next to `FlowBoard.exe` to keep everything in a `Data` folder beside the exe instead (e.g. on a USB stick or a synced folder).

## Project layout

```
src/FlowBoard/
  Models/        Board, BoardList, Card, Label, Checklist, Attachment, Comment/TimeEntry, settings,
                 Documents (storyboards & shots, canvas nodes & edges, note pages & blocks)
  Services/      JSON storage & backups, undo, templates, audio record/playback, reminders,
                 Pomodoro, time tracking, tray, theme, global hotkeys, start-with-Windows,
                 smart quick-add parser, connector routing & auto-layout, Markdown, dashboard stats
  ViewModels/    MainViewModel (+ Cards, App, Hotkeys partials), card details, calendar, table,
                 timeline, command palette, quick add, settings, drag & drop handlers,
                 storyboard (+ sketch pad, animatic), canvas, notes, dashboard, link picker
  Views/         Board (Kanban), Table, Calendar, Timeline, card details, storyboard, canvas,
                 notes, dashboard, dialogs
  Themes/        Dark/light palettes and shared styles
tests/FlowBoard.Tests/   Tests for models, storage, undo, templates, filters, quick-add parsing, connector
                         routing, layout, Markdown and dashboard stats (run anywhere with `dotnet test`)
```

Built with [WPF-UI](https://github.com/lepoco/wpfui) (Fluent design), [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet), [gong-wpf-dragdrop](https://github.com/punker76/gong-wpf-dragdrop) and [NAudio](https://github.com/naudio/NAudio).
