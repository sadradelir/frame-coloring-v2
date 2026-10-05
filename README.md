# FrameColoring V2

**A fast Windows tool for coloring and recoloring 2D sprite animation frames, a whole animation at a time.**

You draw line art for a run cycle, an attack or an idle loop, and end up with 20–60 PNG frames
that all need the same flat colors. Doing that one frame at a time in Photoshop is slow and easy
to get wrong. FrameColoring opens a whole folder of frames, lets you fill a region on every
selected frame in one click, keeps the shading when you swap a key color, and saves everything
back where it came from.

- Fill, recolor, brush, erase and crop **every selected frame at once**
- **Onion skin** of the other selected frames while you work
- **Auto next**: fill, and the editor jumps to the next frame by itself
- **Star** frames as finished so you (and Auto next) can skip them
- Photoshop-style filters: **Curves**, **Gradient Map**, **Median**
- Bake the alpha channel into a **signed distance field** for shaders (outlines, glows, dissolves)
- Full **undo / redo** across all frames an operation touched
- Dark UI, scalable interface (100–200%), everything keyboard driven

---

## Getting started

### Requirements

- Windows 10 or 11
- [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0) or newer
  (check with `dotnet --list-sdks`)

### Build and run

```bash
git clone https://github.com/sadradelir/frame-coloring-v2.git
```

```bash
cd frame-coloring-v2
```

```bash
dotnet run --project FrameColoringV2
```

You can pass files or folders on the command line, and they open at start up:

```bash
dotnet run --project FrameColoringV2 -- "D:\sprites\hero\run"
```

### Make a standalone .exe

To get a single executable you can copy anywhere (no SDK needed on the target machine):

```bash
dotnet publish FrameColoringV2 -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The exe ends up in `FrameColoringV2/bin/Release/net7.0-windows/win-x64/publish/`.

---

## A typical session

1. **Open your frames.** *File ▸ Open Folder…* (Ctrl+O), or just drag a folder onto the window.
2. **Select the frames to work on.** The *Select every frame* button above the list selects them all; Shift/Ctrl click picks a range.
   The first selected frame is shown for editing, the others appear as onion skin behind it.
3. **Pick a color** from the palette (or with the picker, `I`).
4. **Fill** (`F`) a region: it is filled on *every selected frame* at that spot.
   Turn on **Auto next** if you'd rather color frame by frame without touching the keyboard.
5. **Fix the leftovers** with the brush (`B`). By default it paints *behind* the line art, so you
   can scribble freely without ruining the outline.
6. **Star** (`S`) the frames that are done. *Tools ▸ Analyze Uncolored Pixels* helps you spot
   holes you missed.
7. **Save All** (Ctrl+Shift+S) writes back to the original files, or
   **Export Selected To Folder…** (Ctrl+E) writes copies and leaves the originals untouched.

Made a mistake? Ctrl+Z undoes it on every frame it touched.

---

## Tools

| Key | Tool | What it does |
| --- | --- | --- |
| F | Fill | Flood fills the clicked region, bleeding a few pixels into the outline (*Fill bleed*) |
| R | Replace | Recolors the clicked key color region while keeping its shading |
| B | Brush | Paints with the current color and size, *behind* the line art or *over everything* |
| E | Eraser | Erases to transparent |
| I | Picker | Picks a color from the frame |
| C | Crop | Drag the corner handles, then press **Apply Crop** |

Each tool shows only its own options in the toolbar: **Fill bleed** and **Auto next** for the
fill tools, **Brush size** and **Brush mode** for the brush, **Apply Crop** for crop.

**Auto next** jumps to the next frame right after a fill, either immediately or after a short
delay so you can see the result first. It skips starred frames, and only works when a single
frame is selected.

**Stars.** Press `S` (or use the star button / right click menu) to mark the selected frames as
finished. Stars are remembered per file, so they come back when you reopen the folder.

### Whole-frame operations (Tools menu)

Empower Alpha, Drop Shadow (SDF), Trim To Content, Flip Horizontally, Analyze Uncolored Pixels,
Import Unique Frames (copies a folder of frames, skipping consecutive duplicates) and
Open In External Editor.

---

## Filters

All filters can run on the selected frames or on **all open frames**, run in the background with
a Cancel button, and are a single undo step.

### Curves (Ctrl+Shift+C)

Same as Photoshop's Curves. Drag the line to bend it, click to add a point, right click a point
to remove it. Switch between the RGB composite and the Red, Green, Blue and Alpha channels.
The frame's histogram is drawn behind the curve and the result is previewed live on the canvas.
Fully transparent pixels are left alone, so lifting the blacks never creates a halo.

### Gradient Map (Ctrl+G)

Recolors through a ramp: dark pixels take colors from the left end, light pixels from the right.
Click the strip to add a stop, drag to move, double click to pick its color, right click to
remove. Includes presets (sepia, fire, cold steel, toxic, violet dusk…), **Reverse**, **Swap
ends** and an **Amount** slider. Alpha is untouched.

### Median (Ctrl+M)

Like Photoshop's *Noise ▸ Median*: each pixel becomes the median of its neighbours within a
radius (1–24 px). Removes stray pixels and smooths jagged anti-aliasing while keeping hard color
borders sharp. Fast: about 40 ms per 1400×1550 frame at radius 2.

### Alpha → Distance Field

Rewrites the alpha channel as a signed distance field (RGB is untouched), ready to be used in a
shader for outlines, glows or dissolve effects:

```
alpha = saturate(0.5 + signedDistance / (2 * spread))
```

Alpha is 128 on the edge of the sprite, 0 one *spread* outside and 255 one *spread* inside.
**Sub-pixel edge fit** (on by default) finds where the edge really falls between pixels, for a
mean error around 0.05–0.13 px, below what 8-bit alpha can store. **Verify** checks a baked frame
and prints a report.

> Don't bake a frame twice — it would measure the field instead of the sprite. Undo and re-run.

---

## Files and saving

Frames can live anywhere on disk; nothing is tied to the project folder.

| Action | Shortcut |
| --- | --- |
| Open Folder… / Open Files… | Ctrl+O / Ctrl+Shift+O |
| Add Files… / Add Folder… | extend the current set instead of replacing it |
| Drag & drop files or folders | hold Ctrl while dropping to add |
| Recent Folders | last ten folders, under *File* |
| Save Selected / Save All | Ctrl+S / Ctrl+Shift+S |
| Save Selected As… | the frame follows its new path |
| Export Selected To Folder… | Ctrl+E — copies, originals untouched |
| Reload Selected | F5 — discards in-memory changes |

Frames with unsaved changes are marked with `*`. The app asks before closing or replacing frames
with unsaved work.

**Right click a frame in the list** for Save, Save As…, Export, Reload, Star As Finished,
Open In External Editor, Show In File Explorer, Copy Full Path and Remove From List.

---

## Canvas and keyboard

| Input | Action |
| --- | --- |
| Mouse wheel | Zoom at the cursor |
| Ctrl / Shift + wheel | Previous / next frame |
| ← / → | Previous / next frame |
| Middle drag, or Space + drag | Pan |
| Ctrl+0 / Ctrl+1 | Fit frame / 100% |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| S | Star / unstar selected frames |
| Ctrl+, | Settings |

Undo keeps up to 40 steps (trimmed past ~512 MB) and is cleared when you open a new set of frames.

---

## Settings

*File ▸ Settings…* (Ctrl+,):

- **Canvas**: transparency checkerboard colors and square size, with live preview.
- **Interface**: UI scale from 100% to 200% for high-DPI screens or bigger click targets
  (applied after a restart, which the app offers to do for you).

Settings, palette, recent folders and stars are stored in
`%AppData%\FrameColoringV2\settings.json`.

---

## Project layout

```
FrameColoringV2/
  App/       AppSettings       user settings + palette, stored in %AppData%
  Imaging/   FrameOps          pixel operations (fill, replace fill, brush, crop, trim, analyze…)
             ColorHelper       key color math for the shading-preserving recolor
             ToneCurve         curves
             Gradient          gradient map
             MedianFilter      median filter
             DistanceFieldBaker signed distance field bake
  Models/    FrameDocument     one frame: its file, its pixels, dirty / done flags
             FrameSession      the set of frames currently open
             UndoHistory       multi-frame undo / redo
  UI/        MainForm          actions and state
             MainForm.Layout   control construction
             CanvasView        zoom / pan / checkerboard / crop overlay
             Icons             toolbar icons drawn with GDI+
             Theme             dark palette, fonts, interface scale
             *Dialog           one file per filter / settings dialog
```

Built with WinForms on .NET 7 and [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp).

New settings pages are added by writing another `BuildXxxPage` method in `UI/SettingsDialog.cs`
and registering it in the `pages` dictionary.

## Contributing

Issues and pull requests are welcome. Please describe the sprite workflow you're trying to speed
up — that's what this tool is for.

## License

[MIT](LICENSE) — free to use, modify and share, including in commercial projects.
