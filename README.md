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

**Right click a frame in the list** for Save, Save As…, Export To Folder…, Reload From Disk,
Open In External Editor, Show In File Explorer, Copy Full Path and Remove From List. The menu
acts on the whole selection, and right clicking a frame outside the selection selects it first.

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

## Undo

**Edit ▸ Undo** (Ctrl+Z) and **Redo** (Ctrl+Y) cover every pixel operation: fill, replace fill,
brush and eraser strokes (one stroke is one step), crop, trim, empower, drop shadow, flip and
reload. A step covers all the frames the operation touched, so undoing a fill applied to a whole
animation puts every frame back at once. The history keeps up to 40 steps and drops the oldest
ones when it grows past ~512 MB, and it is cleared when the open frames are replaced.

## Tools

Each tool shows only its own options in the toolbar: **Fill bleed** and **Auto next** for the fill
tools, **Brush size** for brush and eraser, **Apply Crop** for the crop tool.

**Auto next** makes the editor jump to the next frame in the list right after a fill, so you can
color a whole animation without leaving the mouse. It only advances when a single frame is
selected, since with several selected there is no obvious next one.

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

## Filters

**Filters ▸ Median…** (Ctrl+M) is the same filter as Photoshop's Noise ▸ Median: every pixel
becomes the median of its neighbours inside a disc of the chosen radius. It kills stray pixels
and rounds off jagged anti-aliased edges while keeping hard colour borders where they are,
which a blur would not.

The dialog asks for the radius (1–24 px) and whether to apply it to the selected frames or to
**all open frames**, so a whole animation is one click. *Keep transparent pixels out of the
colour median* (on by default) stops the colour hiding inside fully transparent pixels from
bleeding into the outline; alpha itself is always filtered over the whole disc.

The run happens in the background with a progress bar and a Cancel button, and the whole batch
is a single undo step. On a 1401×1550 frame it takes about 40 ms at radius 2 and 120 ms at
radius 10, so 33 frames land in a couple of seconds.

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
