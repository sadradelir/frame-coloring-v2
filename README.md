# FrameColoring V2

A small Windows desktop tool for recoloring sprite animation frames. Open frames from
anywhere on disk, paint the key colors, and save them back in place or into any folder
you choose.

## Requirements

- Windows
- .NET 7 SDK (`dotnet --list-sdks` should show a 7.x entry)

## Run

```bash
dotnet run --project FrameColoringV2
```

You can also pass files or folders on the command line, and they are opened at start up:

```bash
dotnet run --project FrameColoringV2 -- "D:\sprites\hero\run"
```

## Working with files

Nothing is tied to folders inside the project any more.

- **File ▸ Open Folder…** (Ctrl+O) / **Open Files…** (Ctrl+Shift+O) — load frames from anywhere
- **Add Files… / Add Folder…** — extend the current set instead of replacing it
- **Drag & drop** files or folders onto the window (hold Ctrl while dropping to add)
- **File ▸ Recent Folders** — the last ten folders you opened
- **Save Selected** (Ctrl+S) / **Save All** (Ctrl+Shift+S) — write back to the original paths
- **Save Selected As…** — pick a new name and path; the document follows the new file
- **Export Selected To Folder…** (Ctrl+E) — write copies into any folder, originals untouched
- **Reload Selected** (F5) — throw away in memory changes for those frames

Frames with unsaved changes are marked with `*` in the list and counted in the status bar;
closing the app or replacing the open set asks before losing them.

Settings (recent folders, palette, brush size, external editor, sidebar width) are stored in
`%AppData%\FrameColoringV2\settings.json`.

## Settings

**File ▸ Settings…** (Ctrl+,) opens the settings window. It has a category list on the left,
so far with a single **Canvas** page where the transparency checkerboard is configured:
the color of the light and dark squares and the square size, with a live preview both in
the dialog and on the canvas behind it. **Restore defaults** puts the original colors back,
**Cancel** undoes the preview.

New sections are added by writing another `BuildXxxPage` method in `UI/SettingsDialog.cs`
and registering it in the `pages` dictionary.

## Tools

| Key | Tool | What it does |
| --- | --- | --- |
| F | Fill | Flood fills the clicked region, bleeding a few pixels into the outline (Fill bleed) |
| R | Replace | Recolors the clicked key color region while keeping its shading |
| B | Brush | Paints with the current color and brush size |
| E | Eraser | Erases to transparent |
| I | Picker | Picks a color from the frame |
| C | Crop | Drag the corner handles, then press Apply Crop |

Whole frame operations live in the **Tools** menu: Empower Alpha, Drop Shadow (SDF),
Trim To Content, Flip Horizontally, Analyze Uncolored Pixels, Import Unique Frames
(copies a folder of frames while skipping consecutive duplicates), and Open In External Editor.

Every tool applies to **all selected frames**, so you can recolor an entire animation in one click.
The first selected frame is the one being edited on screen; the rest are shown as onion skin.

## Canvas

- Mouse wheel zooms at the cursor, Ctrl/Shift + wheel steps through frames
- Middle drag (or space + drag) pans
- Ctrl+0 fits the frame, Ctrl+1 shows it at 100%
- ← / → move to the previous / next frame

## Layout

```
FrameColoringV2/
  App/       AppSettings      user settings + palette, stored in %AppData%
  Imaging/   FrameOps         pixel operations (fill, replace fill, brush, crop, trim, analyze…)
             ColorHelper      key color math for the shading preserving recolor
  Models/    FrameDocument    one frame: its file, its pixels, its dirty flag
             FrameSession     the set of frames currently open
  UI/        MainForm         actions and state
             MainForm.Layout  control construction
             CanvasView       zoom / pan / checkerboard / crop overlay
             BitmapBridge     ImageSharp -> GDI+ conversion
             Theme            dark palette and strip renderers
             ProgressDialog   progress for long imports
```
