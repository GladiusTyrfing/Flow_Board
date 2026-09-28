# FlowBoard

A fast, good-looking **Kanban task manager and creative workspace for Windows**, in the spirit of Trello, Zenkit, Notion and Miro — written in C# / WPF with a Fluent look (Windows 10 and 11).
**Fully local:** no account, no cloud, no internet needed. Your boards live on your PC.

## Features

**Projects**
- Work in separate **project files** (`.flowboard`): each project has its own boards, storyboards, canvases and pages, with its images and recordings in the project folder
- FlowBoard always starts on **Home** with nothing open: create a new project (empty or a sample), open one (Ctrl+O), pick a recent one, or restore a backup zip as a new project
- Switch projects any time from the project name at the top of the sidebar; save a copy under a new name, back up a project to a zip, show it in Explorer, or open a `.flowboard` file from Explorer with "Open with → FlowBoard"

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
- **Voice notes:** record from your microphone with a live level meter, or import audio files (wav, mp3, m4a…); play back with seek
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
- **Crop** each frame (16:9, 4:3, 2.39:1, 1:1, 9:16 … or free) or show the whole image; choose the storyboard's frame shape; import a folder of images as shots; production status per shot; "frames only" overview
- Film: shot type, angle, movement, lens, location, a "shots required" checklist and an equipment list
- Animation: action, dialogue, timing per frame, transition (cut, fade, dissolve, wipe) and a **voice line** — record it or use an audio file from your PC
- **Animatic player**: plays the frames with their timing and voice lines; export the storyboard as PNG, print it, or turn every shot into a card

**Canvas & flowcharts**
- Infinite canvas with pan/zoom and a dot grid; boxes, rounded boxes, ellipses, decision diamonds, sticky notes, text, images and **live cards** from your boards
- Drag from a shape's dot to connect (drop on empty space to create the next shape); curved, straight or elbow connectors with arrows, labels, colors, dashes
- Mind-map keys (Tab = child, Enter = sibling), snap to grid, marquee select, copy/paste/duplicate, paste or drop images, **one-click tidy-up layout**, export PNG / print
- **Sections (frames)** hold their shapes: dragging anything inside a section moves the whole section; **Ctrl+drag** moves a single shape (a click still selects it); Ctrl+drag on a section's empty area box-selects inside it; double-click inside adds a shape. **Tidy up** lays out each section's inside, resizes the section to fit and arranges sections as blocks
- Draw **free lines and arrows** anywhere (ends stick to shapes) and **curve** them with a drag, **freehand pen**, circles, align & distribute, lock, Alt+drag copies, line thickness and double-headed arrows, crop images, right-click menu
- **Link nodes**: drop a live preview of any board, storyboard, single shot, canvas, page or card onto the canvas (double-click opens it)

**Notes / docs pages**
- Block editor: headings, to-dos, bulleted and numbered lists, quotes, callouts, code, dividers, images and links to cards, boards, storyboards, canvases or other pages
- Type `/` for the block menu or use Markdown shortcuts (`#`, `-`, `1.`, `[]`, `>`, ```` ``` ````, `---`); Tab to indent, Alt+↑/↓ to move blocks
- **Rich text**: bold, italic, underline, strikethrough, inline code, any text color and highlight — select text for the formatting bar
- Type `@` anywhere in a line to link **inline** to a card, board, storyboard, a specific **shot**, canvas or page (click to open); the Link button embeds a **preview card**
- Page icons and **cover images**, full-width or reading-width pages, an **outline** of headings, page templates (meeting notes, brief, script, shot list, to-dos, journal), word count and reading time, export/copy as Markdown, turn open to-dos into cards

**Home & stats** (Ctrl+H)
- With no project open: new / open / recent projects
- With a project open: numbers for **every section** — boards (lists, cards, % done), storyboards (shots, runtime, shots done, pictures, voice clips), canvases (shapes, sections, connections), pages (words, to-dos checked, edited this week) — plus open / done-this-week / overdue / due-soon / focus time / streak tiles, a 14-day "completed per day" chart, open cards by priority, time per board, an "up next" agenda, "jump back in" and a project switcher

**Quality of life**
- Command palette / global search (Ctrl+K) across every board, card, storyboard, canvas and note (including text inside pages)
- **Smart quick add**: type `Call Sam tomorrow 3pm #work !high` and the date, time, label and priority are filled in for you
- Filter by keyword, labels, due window, priority, completion
- Undo / redo for everything (Ctrl+Z / Ctrl+Y) + archive with restore
- Windows notifications for due-date reminders and Pomodoro phases, tray icon, "keep running in tray", start with Windows
- **Global hotkeys** that work from any app (quick add a card, show/hide) — customizable
- Dark / light / follow-Windows theme, accent colors, Mica / Acrylic backdrops, smooth animations
- A full **color picker** (any color, hex, recent colors) next to every palette: labels, lists, covers, board backgrounds, canvas, sketches, note text
- **Themes and wallpapers for storyboards, canvases and pages** too (Style button): theme presets, photos, gradients, colors, dim, blur, panel transparency
- **Minimap** in the corner of boards, storyboards, canvases, timelines and pages — drag the box to look around, click to jump (and wheel to zoom on the canvas); hide it with its × button, **Ctrl+M** or in Settings
- **High-res screenshots** (Ctrl+Shift+S or the camera button): the whole board, canvas, storyboard, timeline or page rendered sharply at up to 3× resolution, saved as PNG or copied
- Autosave every 2 seconds, rolling automatic backups per project, zip backup of a project and restore as a new project, portable mode

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
| App | Ctrl+O · Ctrl+H · Ctrl+M | Open project · Home · show/hide minimap |
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
| Canvas | Ctrl+drag | Move one shape inside a section |
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

- **Projects:** each project is a folder (by default in `Documents\FlowBoard Projects\<name>`) holding `<name>.flowboard` (boards, storyboards, canvases, pages as JSON), `attachments\` (card files; storyboard/canvas/page images, sketches and recordings under `attachments\docs\`), `backgrounds\` and `backups\` (rolling automatic copies). Copy or move the folder as a whole.
- **App settings** (theme, hotkeys, recent projects) live in `%AppData%\FlowBoard\settings.json`.
- Data from older versions (`%AppData%\FlowBoard\data.json`) shows up in the recent list as **My workspace** and opens like any other project.
- Create an empty `portable.txt` next to `FlowBoard.exe` to keep settings and new projects in a `Data` folder beside the exe instead (e.g. on a USB stick or a synced folder).

## Project layout

```
src/FlowBoard/
  Models/        Board, BoardList, Card, Label, Checklist, Attachment, Comment/TimeEntry, settings,
                 Documents (storyboards & shots, canvas nodes & edges, note pages & blocks)
  Services/      Project files, JSON storage & backups, undo, templates, audio record/playback, reminders,
                 Pomodoro, time tracking, tray, theme, global hotkeys, start-with-Windows,
                 smart quick-add parser, connector routing & auto-layout, Markdown, dashboard stats
  ViewModels/    MainViewModel (+ Cards, App, Hotkeys, Projects partials), card details, calendar, table,
                 timeline, command palette, quick add, settings, drag & drop handlers,
                 storyboard (+ sketch pad, animatic), canvas, notes, home/dashboard, link picker
  Views/         Board (Kanban), Table, Calendar, Timeline, card details, storyboard, canvas,
                 notes, dashboard, dialogs
  Themes/        Dark/light palettes and shared styles
tests/FlowBoard.Tests/   Tests for models, storage, undo, templates, filters, quick-add parsing, connector
                         routing, layout, Markdown, dashboard and project stats (run anywhere with `dotnet test`)
```

Built with [WPF-UI](https://github.com/lepoco/wpfui) (Fluent design), [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet), [gong-wpf-dragdrop](https://github.com/punker76/gong-wpf-dragdrop) and [NAudio](https://github.com/naudio/NAudio).
