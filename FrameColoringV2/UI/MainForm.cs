using System.Diagnostics;
using FrameColoringV2.App;
using FrameColoringV2.Imaging;
using FrameColoringV2.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace FrameColoringV2.UI;

public enum EditorTool
{
    Fill,
    ReplaceFill,
    Brush,
    Eraser,
    Picker,
    Crop
}

public sealed partial class MainForm : Form
{
    private readonly AppSettings settings;
    private readonly FrameSession session = new();
    private readonly UndoHistory history = new();

    // Controls (built in MainForm.Layout.cs).
    private CanvasView canvas = null!;
    private ListView framesList = null!;
    private MenuStrip menuStrip = null!;
    private ToolStrip toolStrip = null!;
    private StatusStrip statusStrip = null!;
    private FlowLayoutPanel paletteFlow = null!;
    private Panel colorPreview = null!;
    private Label colorHexLabel = null!;
    private Label opacityLabel = null!;
    private TrackBar opacityTrack = null!;
    private NumericUpDown brushSizeUpDown = null!;
    private NumericUpDown fillToleranceUpDown = null!;
    private ToolStripButton fillToolButton = null!;
    private ToolStripButton replaceToolButton = null!;
    private ToolStripButton brushToolButton = null!;
    private ToolStripButton eraserToolButton = null!;
    private ToolStripButton pickerToolButton = null!;
    private ToolStripButton cropToolButton = null!;
    private ToolStripButton applyCropButton = null!;
    private ToolStripButton autoNextButton = null!;
    private ToolStripButton onionSkinButton = null!;
    private ToolStripLabel brushSizeLabel = null!;
    private ToolStripLabel fillBleedLabel = null!;
    private ToolStripControlHost brushSizeHost = null!;
    private ToolStripControlHost fillBleedHost = null!;
    private ToolStripSeparator toolOptionsSeparator = null!;
    private ToolStripMenuItem undoMenuItem = null!;
    private ToolStripMenuItem redoMenuItem = null!;
    private ToolStripMenuItem recentFoldersMenu = null!;
    private ToolStripMenuItem onionSkinMenuItem = null!;
    private ToolStripStatusLabel statusFile = null!;
    private ToolStripStatusLabel statusSize = null!;
    private ToolStripStatusLabel statusCursor = null!;
    private ToolStripStatusLabel statusZoom = null!;
    private ToolStripStatusLabel statusDirty = null!;

    // Editor state.
    private Rgba32 currentColor = new(0, 0, 0, 255);
    private EditorTool currentTool = EditorTool.Fill;
    private Rectangle cropRectangle = new(0, 0, 0, 0);
    private bool suppressSelectionEvents;
    private bool paintingStroke;

    // Rendering.
    private Action<Image<Rgba32>>? previewAdjustment;   // shown on the canvas while a filter dialog is open
    private Image<Rgba32>? composite;
    private Bitmap? displayBitmap;

    public MainForm(IEnumerable<string>? startupPaths = null)
    {
        settings = AppSettings.Load();

        BuildUi();
        ApplyCanvasSettings();
        RebuildPalette();
        RebuildRecentMenu();
        SetTool(EditorTool.Fill);
        SetCurrentColor(Color.Black);

        DragEnter += MainForm_DragEnter;
        DragDrop += MainForm_DragDrop;
        FormClosing += MainForm_FormClosing;

        var paths = startupPaths?.ToList() ?? new List<string>();
        if (paths.Count > 0)
        {
            LoadPaths(FrameSession.ExpandPaths(paths), replaceExisting: true);
        }
        else if (settings.ReopenLastFolder && !string.IsNullOrEmpty(settings.LastOpenFolder) &&
                 Directory.Exists(settings.LastOpenFolder))
        {
            OpenFolder(settings.LastOpenFolder!, replaceExisting: true);
        }

        UpdateStatus();
    }

    // ------------------------------------------------------------- open / save

    private void OpenFolderDialog(bool replaceExisting)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = replaceExisting ? "Open a folder of frames" : "Add a folder of frames",
            UseDescriptionForTitle = true,
            SelectedPath = FirstExistingFolder(settings.LastOpenFolder, session.CommonFolder) ?? string.Empty
        };

        if (dialog.ShowDialog(this) == DialogResult.OK) OpenFolder(dialog.SelectedPath, replaceExisting);
    }

    private void OpenFolder(string folder, bool replaceExisting)
    {
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(this, $"Folder not found:\n{folder}", "Open Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            settings.RecentFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
            RebuildRecentMenu();
            return;
        }

        var images = FrameSession.EnumerateImages(folder).ToList();
        if (images.Count == 0)
        {
            MessageBox.Show(this, "No images found in that folder.", "Open Folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (replaceExisting && !ConfirmDiscardChanges()) return;

        LoadPaths(images, replaceExisting);
        settings.LastOpenFolder = folder;
        settings.PushRecentFolder(folder);
        RebuildRecentMenu();
    }

    private void OpenFilesDialog(bool replaceExisting)
    {
        using var dialog = new OpenFileDialog
        {
            Title = replaceExisting ? "Open frames" : "Add frames",
            Multiselect = true,
            Filter = "Images (*.png;*.bmp;*.jpg;*.jpeg;*.gif;*.webp;*.tga)|*.png;*.bmp;*.jpg;*.jpeg;*.gif;*.webp;*.tga|All files (*.*)|*.*",
            InitialDirectory = FirstExistingFolder(settings.LastOpenFolder, session.CommonFolder) ?? string.Empty
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (replaceExisting && !ConfirmDiscardChanges()) return;

        LoadPaths(dialog.FileNames, replaceExisting);

        string? folder = Path.GetDirectoryName(dialog.FileNames.FirstOrDefault() ?? string.Empty);
        if (!string.IsNullOrEmpty(folder))
        {
            settings.LastOpenFolder = folder;
            settings.PushRecentFolder(folder);
            RebuildRecentMenu();
        }
    }

    private void LoadPaths(IEnumerable<string> paths, bool replaceExisting)
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            if (replaceExisting) history.Clear();
            var failures = session.Open(paths, replaceExisting);
            RebuildFramesList(selectFirst: true);

            if (failures.Count > 0)
            {
                string message = string.Join(Environment.NewLine,
                    failures.Take(10).Select(f => $"{Path.GetFileName(f.path)}: {f.error}"));
                if (failures.Count > 10) message += $"{Environment.NewLine}… and {failures.Count - 10} more";
                MessageBox.Show(this, message, "Some files could not be opened", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void SaveSelected()
    {
        var documents = SelectedDocuments().Where(d => d.IsDirty).ToList();
        if (documents.Count == 0)
        {
            SetStatusMessage("Nothing to save in the selection.");
            return;
        }

        SaveDocuments(documents);
    }

    private void SaveAll()
    {
        var documents = session.Documents.Where(d => d.IsDirty).ToList();
        if (documents.Count == 0)
        {
            SetStatusMessage("No unsaved changes.");
            return;
        }

        SaveDocuments(documents);
    }

    private void SaveDocuments(IReadOnlyList<FrameDocument> documents)
    {
        var errors = new List<string>();
        foreach (var document in documents)
        {
            try
            {
                document.Save();
            }
            catch (Exception ex)
            {
                errors.Add($"{document.FileName}: {ex.Message}");
            }
        }

        RefreshFrameLabels();
        UpdateStatus();
        SetStatusMessage($"Saved {documents.Count - errors.Count} frame(s).");

        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Save failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveSelectedAs()
    {
        var documents = SelectedDocuments();
        if (documents.Count == 0) return;

        if (documents.Count == 1)
        {
            var document = documents[0];
            using var dialog = new SaveFileDialog
            {
                Title = "Save frame as",
                Filter = "PNG image (*.png)|*.png",
                FileName = Path.GetFileNameWithoutExtension(document.FileName) + ".png",
                InitialDirectory = FirstExistingFolder(settings.LastSaveFolder, document.FolderPath) ?? string.Empty
            };

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                document.SaveTo(dialog.FileName, rebind: true);
                settings.LastSaveFolder = Path.GetDirectoryName(dialog.FileName);
                RebuildFramesList(selectFirst: false);
                SetStatusMessage($"Saved as {Path.GetFileName(dialog.FileName)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return;
        }

        ExportSelectedToFolder(rebind: true);
    }

    private void ExportSelectedToFolder() => ExportSelectedToFolder(rebind: false);

    private void ExportSelectedToFolder(bool rebind)
    {
        var documents = SelectedDocuments();
        if (documents.Count == 0)
        {
            SetStatusMessage("Select the frames you want to export first.");
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = rebind ? "Save the selected frames into…" : "Export the selected frames into…",
            UseDescriptionForTitle = true,
            SelectedPath = FirstExistingFolder(settings.LastSaveFolder, session.CommonFolder) ?? string.Empty
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string target = dialog.SelectedPath;
        var overwrites = documents
            .Select(d => Path.Combine(target, Path.GetFileNameWithoutExtension(d.FileName) + ".png"))
            .Where(File.Exists)
            .ToList();

        if (overwrites.Count > 0)
        {
            var answer = MessageBox.Show(this,
                $"{overwrites.Count} file(s) already exist in that folder and will be overwritten. Continue?",
                "Overwrite files", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        var errors = new List<string>();
        foreach (var document in documents)
        {
            string destination = Path.Combine(target, Path.GetFileNameWithoutExtension(document.FileName) + ".png");
            try
            {
                document.SaveTo(destination, rebind);
            }
            catch (Exception ex)
            {
                errors.Add($"{document.FileName}: {ex.Message}");
            }
        }

        settings.LastSaveFolder = target;
        RebuildFramesList(selectFirst: false);
        SetStatusMessage($"{documents.Count - errors.Count} frame(s) written to {target}");

        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Export failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ReloadSelected()
    {
        var selected = SelectedDocuments();
        if (selected.Count == 0) return;

        history.BeginAndCapture("Reload", selected);

        foreach (var document in selected)
        {
            try
            {
                document.Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{document.FileName}: {ex.Message}", "Reload failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        history.Commit();
        RefreshFrameLabels();
        RefreshCanvas();
        UpdateStatus();
    }

    private void RemoveSelectedFromList()
    {
        var documents = SelectedDocuments();
        if (documents.Count == 0) return;

        if (documents.Any(d => d.IsDirty))
        {
            var answer = MessageBox.Show(this, "Some of those frames have unsaved changes. Remove anyway?",
                "Remove frames", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        history.Forget(documents);
        session.Remove(documents);
        RebuildFramesList(selectFirst: true);
    }

    private void CloseAllFrames(bool askToSave)
    {
        if (askToSave && !ConfirmDiscardChanges()) return;

        history.Clear();
        session.CloseAll();
        RebuildFramesList(selectFirst: false);
    }

    private bool ConfirmDiscardChanges()
    {
        if (!session.HasUnsavedChanges) return true;

        var answer = MessageBox.Show(this,
            $"{session.UnsavedCount} frame(s) have unsaved changes.\n\nSave them before continuing?",
            "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

        switch (answer)
        {
            case DialogResult.Yes:
                SaveAll();
                return !session.HasUnsavedChanges;
            case DialogResult.No:
                return true;
            default:
                return false;
        }
    }

    private static string ToolLabel(EditorTool tool) => tool switch
    {
        EditorTool.Fill => "Fill",
        EditorTool.ReplaceFill => "Replace fill",
        EditorTool.Brush => "Brush",
        EditorTool.Eraser => "Eraser",
        EditorTool.Crop => "Crop",
        _ => "Edit"
    };

    /// <summary>Auto next: after a fill, move on to the next frame in the list.</summary>
    private void AutoAdvanceFrame()
    {
        if (!settings.AutoNextFrame) return;
        if (framesList.SelectedIndices.Count != 1) return; // ambiguous with a multi selection

        StepFrame(1);
    }

    private static string? FirstExistingFolder(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && Directory.Exists(c));

    // ------------------------------------------------------------------- tools

    private void SetTool(EditorTool tool)
    {
        currentTool = tool;

        foreach (var button in new[] { fillToolButton, replaceToolButton, brushToolButton, eraserToolButton, pickerToolButton, cropToolButton })
        {
            bool active = (EditorTool)button.Tag! == tool;
            button.Checked = active;
            button.BackColor = active ? Theme.Accent : Color.Transparent;
        }

        UpdateToolOptions();
        canvas.CropOverlay = tool == EditorTool.Crop ? EnsureCropRectangle() : null;
        canvas.Cursor = tool == EditorTool.Picker ? Cursors.Hand : Cursors.Cross;
        canvas.Invalidate();
        UpdateStatus();
    }

    /// <summary>Shows the options that belong to the active tool and hides the rest.</summary>
    private void UpdateToolOptions()
    {
        bool isFill = currentTool == EditorTool.Fill;
        bool isReplaceFill = currentTool == EditorTool.ReplaceFill;
        bool isBrush = currentTool is EditorTool.Brush or EditorTool.Eraser;
        bool isCrop = currentTool == EditorTool.Crop;

        fillBleedLabel.Visible = isFill;
        fillBleedHost.Visible = isFill;
        autoNextButton.Visible = isFill || isReplaceFill;
        brushSizeLabel.Visible = isBrush;
        brushSizeHost.Visible = isBrush;
        applyCropButton.Visible = isCrop;

        toolOptionsSeparator.Visible = isFill || isReplaceFill || isBrush || isCrop;
    }

    private Rectangle EnsureCropRectangle()
    {
        var primary = PrimaryDocument();
        if (primary == null) return cropRectangle;

        if (cropRectangle.Width <= 0 || cropRectangle.Height <= 0 ||
            cropRectangle.Right > primary.Image.Width || cropRectangle.Bottom > primary.Image.Height)
        {
            int inset = Math.Max(1, Math.Min(primary.Image.Width, primary.Image.Height) / 8);
            cropRectangle = new Rectangle(inset, inset,
                Math.Max(1, primary.Image.Width - inset * 2),
                Math.Max(1, primary.Image.Height - inset * 2));
        }

        return cropRectangle;
    }

    private void Canvas_PixelMouseDown(object? sender, PixelMouseEventArgs e)
    {
        if (!e.InsideImage || session.Count == 0) return;

        switch (currentTool)
        {
            case EditorTool.Picker:
                PickColorAt(e.Pixel);
                return;
            case EditorTool.Crop:
                paintingStroke = true;
                DragCropCorner(e.Pixel);
                return;
            default:
                // A brush stroke is one undo step from mouse down to mouse up;
                // a fill is a step on its own.
                history.BeginAndCapture(ToolLabel(currentTool), SelectedDocuments());
                paintingStroke = true;
                ApplyToolAt(e.Pixel);

                if (currentTool is EditorTool.Fill or EditorTool.ReplaceFill)
                {
                    history.Commit();
                    AutoAdvanceFrame();
                }

                return;
        }
    }

    private void Canvas_PixelMouseDrag(object? sender, PixelMouseEventArgs e)
    {
        if (!paintingStroke || !e.InsideImage) return;

        switch (currentTool)
        {
            case EditorTool.Brush:
            case EditorTool.Eraser:
                ApplyToolAt(e.Pixel);
                break;
            case EditorTool.Crop:
                DragCropCorner(e.Pixel);
                break;
        }
    }

    private void EndStroke()
    {
        if (!paintingStroke) return;

        paintingStroke = false;

        // Fills already committed themselves; brush strokes close here.
        if (currentTool is EditorTool.Brush or EditorTool.Eraser) history.Commit();
        else history.Cancel();

        UpdateStatus();
    }

    private void Canvas_PixelMouseMove(object? sender, PixelMouseEventArgs e)
    {
        statusCursor.Text = e.InsideImage ? $"{e.Pixel.X}, {e.Pixel.Y}" : "—";
    }

    private void ApplyToolAt(Point pixel)
    {
        var documents = SelectedDocuments();
        if (documents.Count == 0) return;

        Cursor = currentTool is EditorTool.Fill or EditorTool.ReplaceFill && documents.Count > 4
            ? Cursors.WaitCursor
            : Cursor;

        foreach (var document in documents)
        {
            var image = document.Image;
            if (pixel.X < 0 || pixel.Y < 0 || pixel.X >= image.Width || pixel.Y >= image.Height) continue;

            switch (currentTool)
            {
                case EditorTool.Fill:
                    FrameOps.Fill(image, pixel.X, pixel.Y, currentColor, (int)fillToleranceUpDown.Value, new HashSet<(int, int)>());
                    break;
                case EditorTool.ReplaceFill:
                    FrameOps.ReplaceFill(image, pixel.X, pixel.Y, currentColor, new HashSet<(int, int)>());
                    break;
                case EditorTool.Brush:
                    FrameOps.Brush(image, pixel.X, pixel.Y, currentColor, (int)brushSizeUpDown.Value);
                    break;
                case EditorTool.Eraser:
                    FrameOps.Brush(image, pixel.X, pixel.Y, new Rgba32(0, 0, 0, 0), (int)brushSizeUpDown.Value);
                    break;
            }

            document.MarkDirty();
        }

        Cursor = Cursors.Default;
        RefreshFrameLabels();
        RefreshCanvas();
        UpdateStatus();
    }

    private void PickColorAt(Point pixel)
    {
        var primary = PrimaryDocument();
        if (primary == null) return;
        if (pixel.X < 0 || pixel.Y < 0 || pixel.X >= primary.Image.Width || pixel.Y >= primary.Image.Height) return;

        var picked = primary.Image[pixel.X, pixel.Y];
        SetCurrentColor(Color.FromArgb(picked.A, picked.R, picked.G, picked.B));
    }

    private void DragCropCorner(Point pixel)
    {
        var rectangle = EnsureCropRectangle();

        var topLeft = new Point(rectangle.Left, rectangle.Top);
        var bottomRight = new Point(rectangle.Right, rectangle.Bottom);

        int distanceToTopLeft = Distance2(topLeft, pixel);
        int distanceToBottomRight = Distance2(bottomRight, pixel);

        if (distanceToTopLeft <= distanceToBottomRight) topLeft = pixel;
        else bottomRight = pixel;

        cropRectangle = new Rectangle(
            Math.Min(topLeft.X, bottomRight.X),
            Math.Min(topLeft.Y, bottomRight.Y),
            Math.Max(1, Math.Abs(bottomRight.X - topLeft.X)),
            Math.Max(1, Math.Abs(bottomRight.Y - topLeft.Y)));

        canvas.CropOverlay = cropRectangle;
        canvas.Invalidate();
        UpdateStatus();
    }

    private static int Distance2(Point a, Point b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    private void ApplyCrop()
    {
        var documents = SelectedDocuments();
        if (documents.Count == 0) return;

        var rectangle = EnsureCropRectangle();
        history.BeginAndCapture("Crop", documents);

        foreach (var document in documents)
        {
            document.ReplaceImage(FrameOps.Crop(document.Image, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
        }

        history.Commit();

        cropRectangle = new Rectangle(0, 0, 0, 0);
        SetTool(EditorTool.Fill);
        RefreshFrameLabels();
        RefreshCanvas(resetView: true);
        UpdateStatus();
        SetStatusMessage($"Cropped {documents.Count} frame(s) to {rectangle.Width}×{rectangle.Height}.");
    }

    private void TrimSelected()
    {
        var documents = SelectedDocuments();
        if (documents.Count == 0) return;

        history.BeginAndCapture("Trim", documents);

        int trimmed = 0;
        foreach (var document in documents)
        {
            var bounds = FrameOps.GetTrimBounds(document.Image);
            if (bounds.Width <= 0 || bounds.Height <= 0) continue;

            document.ReplaceImage(FrameOps.Crop(document.Image, bounds.X, bounds.Y, bounds.Width, bounds.Height));
            trimmed++;
        }

        history.Commit();

        RefreshFrameLabels();
        RefreshCanvas(resetView: true);
        UpdateStatus();
        SetStatusMessage($"Trimmed {trimmed} frame(s).");
    }

    private void ApplyToSelected(string label, Action<Image<Rgba32>> action)
    {
        var documents = SelectedDocuments();
        if (documents.Count == 0)
        {
            SetStatusMessage("Select at least one frame first.");
            return;
        }

        history.BeginAndCapture(label, documents);

        Cursor = Cursors.WaitCursor;
        try
        {
            foreach (var document in documents)
            {
                action(document.Image);
                document.MarkDirty();
            }
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        history.Commit();

        RefreshFrameLabels();
        RefreshCanvas();
        UpdateStatus();
        SetStatusMessage($"{label} applied to {documents.Count} frame(s).");
    }

    private async void ApplyCurves()
    {
        if (session.Count == 0)
        {
            SetStatusMessage("Open some frames first.");
            return;
        }

        var selected = SelectedDocuments();
        using var dialog = new CurvesDialog(PrimaryDocument()?.Image, selected.Count, session.Count);

        dialog.PreviewChanged += (_, _) =>
        {
            var preview = dialog.PreviewCurves;
            previewAdjustment = preview == null ? null : image => preview.Apply(image);
            RefreshCanvas();
        };

        var result = dialog.ShowDialog(this);

        previewAdjustment = null;
        RefreshCanvas();

        if (result != DialogResult.OK) return;

        if (dialog.Curves.IsIdentity)
        {
            SetStatusMessage("Curves left unchanged.");
            return;
        }

        var targets = dialog.ApplyToAllFrames ? session.Documents.ToList() : selected;
        if (targets.Count == 0)
        {
            SetStatusMessage("Select at least one frame first.");
            return;
        }

        // Snapshot the curves so later edits of the dialog object cannot change the batch.
        var curves = dialog.Curves.Clone();
        await RunOnFrames("Curves", targets, (document, token) => curves.Apply(document.Image, token));
    }

    private async void ApplyGradientMap()
    {
        if (session.Count == 0)
        {
            SetStatusMessage("Open some frames first.");
            return;
        }

        var selected = SelectedDocuments();
        using var dialog = new GradientMapDialog(LoadGradientFromSettings(), settings.GradientMapReverse,
            settings.GradientMapAmount, selected.Count, session.Count);

        dialog.PreviewChanged += (_, _) =>
        {
            if (!dialog.PreviewEnabled)
            {
                previewAdjustment = null;
            }
            else
            {
                var gradient = dialog.Gradient.Clone();
                bool reverse = dialog.Reverse;
                float amount = dialog.Amount;
                previewAdjustment = image => gradient.ApplyMap(image, reverse, amount);
            }

            RefreshCanvas();
        };

        var result = dialog.ShowDialog(this);

        previewAdjustment = null;
        RefreshCanvas();

        if (result != DialogResult.OK) return;

        settings.GradientMapStops = dialog.Gradient.Stops
            .Select(stop => new GradientStopSetting
            {
                Position = stop.Position,
                Hex = $"#{stop.Color.R:x2}{stop.Color.G:x2}{stop.Color.B:x2}"
            })
            .ToList();
        settings.GradientMapReverse = dialog.Reverse;
        settings.GradientMapAmount = dialog.AmountPercent;

        if (dialog.AmountPercent == 0)
        {
            SetStatusMessage("Gradient map amount was 0%, nothing changed.");
            return;
        }

        var targets = dialog.ApplyToAllFrames ? session.Documents.ToList() : selected;
        if (targets.Count == 0)
        {
            SetStatusMessage("Select at least one frame first.");
            return;
        }

        var mapped = dialog.Gradient.Clone();
        bool reversed = dialog.Reverse;
        float strength = dialog.Amount;

        await RunOnFrames("Gradient map", targets,
            (document, token) => mapped.ApplyMap(document.Image, reversed, strength, token));
    }

    private Gradient LoadGradientFromSettings()
    {
        if (settings.GradientMapStops.Count < 2) return new Gradient();

        try
        {
            return new Gradient(settings.GradientMapStops
                .Select(stop => new GradientStop(stop.Position, Rgba32.ParseHex(stop.Hex))));
        }
        catch (Exception)
        {
            return new Gradient();
        }
    }

    private async void ApplyMedianFilter()
    {
        if (session.Count == 0)
        {
            SetStatusMessage("Open some frames first.");
            return;
        }

        var selected = SelectedDocuments();
        using var dialog = new MedianFilterDialog(settings.MedianRadius, settings.MedianIgnoreTransparent,
            selected.Count, session.Count);

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        settings.MedianRadius = dialog.Radius;
        settings.MedianIgnoreTransparent = dialog.IgnoreTransparent;

        var targets = dialog.ApplyToAllFrames ? session.Documents.ToList() : selected;
        if (targets.Count == 0)
        {
            SetStatusMessage("Select at least one frame first.");
            return;
        }

        int radius = dialog.Radius;
        bool ignoreTransparent = dialog.IgnoreTransparent;

        await RunOnFrames($"Median {radius}px", targets,
            (document, token) => MedianFilter.Apply(document.Image, radius, ignoreTransparent, token));
    }

    /// <summary>
    /// Runs a slow per frame operation in the background with a progress dialog, recording one
    /// undo step for the whole batch.
    /// </summary>
    private async Task RunOnFrames(string label, IReadOnlyList<FrameDocument> targets,
        Action<FrameDocument, CancellationToken> action)
    {
        history.BeginAndCapture(label, targets);

        using var cancellation = new CancellationTokenSource();
        using var dialog = new ProgressDialog(label, cancellable: true);
        dialog.Cancelled += (_, _) => cancellation.Cancel();

        var progress = new Progress<(int current, int total, string message)>(p =>
            dialog.Report(p.current, p.total, p.message));

        int done = 0;
        Exception? failure = null;

        dialog.Show(this);
        try
        {
            await Task.Run(() =>
            {
                var reporter = (IProgress<(int, int, string)>)progress;
                for (int i = 0; i < targets.Count; i++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();

                    reporter.Report((i, targets.Count, $"{targets[i].FileName}  ({i + 1}/{targets.Count})"));
                    action(targets[i], cancellation.Token);
                    targets[i].MarkDirty();
                    done++;
                }
            }, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Frames already processed keep their result; one Ctrl+Z undoes the whole batch.
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            dialog.Close();
        }

        history.Commit();
        RefreshFrameLabels();
        RefreshCanvas();
        UpdateStatus();

        if (failure != null)
        {
            MessageBox.Show(this, failure.Message, label, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        SetStatusMessage(done == targets.Count
            ? $"{label} applied to {done} frame(s)."
            : $"{label} cancelled after {done} of {targets.Count} frame(s).");
    }

    private void AnalyzeSelected()
    {
        var documents = SelectedDocuments();
        if (documents.Count == 0) return;

        Cursor = Cursors.WaitCursor;
        var lines = new List<string>();
        int total = 0;
        try
        {
            foreach (var document in documents)
            {
                int count = FrameOps.Analyze(document.Image);
                total += count;
                lines.Add($"{document.FileName}: {count}");
            }
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        string message = documents.Count == 1
            ? $"{total} pixels are not colored."
            : string.Join(Environment.NewLine, lines.Take(30)) +
              (lines.Count > 30 ? $"{Environment.NewLine}…" : string.Empty) +
              $"{Environment.NewLine}{Environment.NewLine}Total: {total} pixels are not colored.";

        MessageBox.Show(this, message, "Analyze", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void ImportUniqueFrames()
    {
        using var sourceDialog = new FolderBrowserDialog
        {
            Description = "Source folder with all frames",
            UseDescriptionForTitle = true,
            SelectedPath = FirstExistingFolder(settings.LastOpenFolder) ?? string.Empty
        };
        if (sourceDialog.ShowDialog(this) != DialogResult.OK) return;

        using var targetDialog = new FolderBrowserDialog
        {
            Description = "Target folder for the unique frames",
            UseDescriptionForTitle = true,
            SelectedPath = FirstExistingFolder(settings.LastSaveFolder, sourceDialog.SelectedPath) ?? string.Empty
        };
        if (targetDialog.ShowDialog(this) != DialogResult.OK) return;

        string source = sourceDialog.SelectedPath;
        string target = targetDialog.SelectedPath;

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Source and target folders must be different.", "Import Unique Frames",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new ProgressDialog("Importing unique frames");
        var progress = new Progress<(int current, int total, string message)>(p => dialog.Report(p.current, p.total, p.message));

        dialog.Show(this);
        List<string> copied;
        try
        {
            copied = await Task.Run(() => FrameOps.CopyNonDuplicatesTo(source, target, progress));
        }
        catch (Exception ex)
        {
            dialog.Close();
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            dialog.Close();
        }

        settings.LastSaveFolder = target;

        var answer = MessageBox.Show(this,
            $"{copied.Count} unique frame(s) copied to:\n{target}\n\nOpen them now?",
            "Import Unique Frames", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

        if (answer == DialogResult.Yes && ConfirmDiscardChanges()) OpenFolder(target, replaceExisting: true);
    }

    private void OpenInExternalEditor()
    {
        var documents = SelectedDocuments();
        if (documents.Count == 0) return;

        if (!File.Exists(settings.ExternalEditorPath))
        {
            var answer = MessageBox.Show(this,
                "The external editor was not found. Pick the application to use?",
                "External editor", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            SetExternalEditor();
            if (!File.Exists(settings.ExternalEditorPath)) return;
        }

        var unsaved = documents.Where(d => d.IsDirty).ToList();
        if (unsaved.Count > 0)
        {
            var answer = MessageBox.Show(this,
                $"{unsaved.Count} of the selected frame(s) have unsaved changes.\nSave them before opening?",
                "External editor", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes) SaveDocuments(unsaved);
        }

        foreach (var document in documents)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = settings.ExternalEditorPath,
                    Arguments = $"\"{document.FilePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not open the external editor",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
    }

    private void SetExternalEditor()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the external image editor",
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            FileName = settings.ExternalEditorPath
        };

        if (dialog.ShowDialog(this) == DialogResult.OK) settings.ExternalEditorPath = dialog.FileName;
    }

    private void Undo()
    {
        string? label = history.Undo();
        if (label == null)
        {
            SetStatusMessage("Nothing to undo.");
            return;
        }

        AfterHistoryChange($"Undid {label}.");
    }

    private void Redo()
    {
        string? label = history.Redo();
        if (label == null)
        {
            SetStatusMessage("Nothing to redo.");
            return;
        }

        AfterHistoryChange($"Redid {label}.");
    }

    private void AfterHistoryChange(string message)
    {
        RefreshFrameLabels();
        RefreshCanvas();
        UpdateStatus();
        SetStatusMessage(message);
    }

    private void RevealInFileExplorer()
    {
        var documents = SelectedDocuments();
        if (documents.Count == 0) return;

        // One window per folder, and never more than a handful of them.
        var perFolder = documents
            .GroupBy(document => document.FolderPath, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(group => group.First());

        foreach (var document in perFolder)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{document.FilePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not open File Explorer",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
    }

    private void CopySelectedPaths()
    {
        var documents = SelectedDocuments();
        if (documents.Count == 0) return;

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, documents.Select(document => document.FilePath)));
            SetStatusMessage(documents.Count == 1
                ? "Path copied to the clipboard."
                : $"{documents.Count} paths copied to the clipboard.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowSettings()
    {
        using var dialog = new SettingsDialog(settings);

        // Show every change on the canvas straight away; Cancel puts the old values back.
        dialog.PreviewChanged += (_, _) =>
        {
            var (light, dark, square) = dialog.DialogResult == DialogResult.Cancel
                ? CanvasSettings()
                : dialog.Checkerboard;
            canvas.SetCheckerboard(light, dark, square);
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ApplyCanvasSettings();
            settings.Save();
        }
        else
        {
            ApplyCanvasSettings();
        }
    }

    private (Color light, Color dark, int square) CanvasSettings() => (
        SettingsDialog.ParseColor(settings.CheckerLightColor, AppSettings.DefaultCheckerLight),
        SettingsDialog.ParseColor(settings.CheckerDarkColor, AppSettings.DefaultCheckerDark),
        Math.Clamp(settings.CheckerSquareSize, 2, 64));

    private void ApplyCanvasSettings()
    {
        var (light, dark, square) = CanvasSettings();
        canvas.SetCheckerboard(light, dark, square);
    }

    private void ShowShortcuts()
    {
        const string text = """
                            Tools
                              F  Fill                 R  Replace fill
                              B  Brush                E  Eraser
                              I  Color picker         C  Crop

                            Canvas
                              Wheel                   zoom at the cursor
                              Ctrl/Shift + wheel      previous / next frame
                              Middle drag             pan
                              Ctrl+0 / Ctrl+1         fit / actual size

                            Frames
                              ← →                     previous / next frame
                              Ctrl+S                  save selected
                              Ctrl+Shift+S            save all
                              Ctrl+E                  export selection to a folder
                              F5                      reload selected from disk

                            Files can be opened from anywhere and saved anywhere:
                            File ▸ Open Folder / Open Files, or drag & drop onto the window.
                            """;

        MessageBox.Show(this, text, "Shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ------------------------------------------------------------------ colors

    private void SetCurrentColor(Color color)
    {
        currentColor = new Rgba32(color.R, color.G, color.B, (byte)opacityTrack.Value);
        UpdateColorPreview();
    }

    private void UpdateColorPreview()
    {
        colorPreview.BackColor = Color.FromArgb(255, currentColor.R, currentColor.G, currentColor.B);
        colorHexLabel.Text = $"#{currentColor.R:x2}{currentColor.G:x2}{currentColor.B:x2}";
    }

    private void PickCustomColor()
    {
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            Color = Color.FromArgb(currentColor.R, currentColor.G, currentColor.B)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK) SetCurrentColor(dialog.Color);
    }

    private void AddCurrentColorToPalette()
    {
        string hex = $"#{currentColor.R:x2}{currentColor.G:x2}{currentColor.B:x2}";
        string? name = Prompt("Name for this color:", "Add to palette", hex);
        if (name == null) return;

        settings.Palette.Add(new PaletteEntry { Hex = hex, Name = string.IsNullOrWhiteSpace(name) ? hex : name });
        RebuildPalette();
    }

    private void RenamePaletteEntry(PaletteEntry entry)
    {
        string? name = Prompt("New name:", "Rename color", entry.Name);
        if (string.IsNullOrWhiteSpace(name)) return;

        entry.Name = name;
        RebuildPalette();
    }

    private void RemovePaletteEntry(PaletteEntry entry)
    {
        settings.Palette.Remove(entry);
        RebuildPalette();
    }

    private string? Prompt(string message, string title, string initialValue)
    {
        using var dialog = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(340, 120),
            MinimizeBox = false,
            MaximizeBox = false,
            BackColor = Theme.Background,
            ForeColor = Theme.Text,
            Font = Theme.UiFont
        };

        var label = new Label { Location = new Point(12, 12), Size = new Size(316, 20), Text = message };
        var input = new TextBox
        {
            Location = new Point(12, 36),
            Size = new Size(316, 24),
            Text = initialValue,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        var ok = new Button { Location = new Point(172, 76), Size = new Size(74, 28), Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Location = new Point(254, 76), Size = new Size(74, 28), Text = "Cancel", DialogResult = DialogResult.Cancel };
        Theme.StyleButton(ok, primary: true);
        Theme.StyleButton(cancel);

        dialog.Controls.AddRange(new Control[] { label, input, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
    }

    // ---------------------------------------------------------------- frames UI

    private List<FrameDocument> SelectedDocuments() =>
        framesList.SelectedIndices.Cast<int>()
            .Where(i => i >= 0 && i < session.Count)
            .Select(i => session[i])
            .ToList();

    private FrameDocument? PrimaryDocument()
    {
        var indices = framesList.SelectedIndices;
        if (indices.Count == 0) return null;

        int index = indices[0];
        return index >= 0 && index < session.Count ? session[index] : null;
    }

    private void RebuildFramesList(bool selectFirst)
    {
        suppressSelectionEvents = true;
        framesList.BeginUpdate();
        framesList.Items.Clear();

        foreach (var document in session.Documents)
        {
            framesList.Items.Add(new ListViewItem(document.DisplayName) { ToolTipText = document.FilePath });
        }

        framesList.EndUpdate();
        suppressSelectionEvents = false;

        if (selectFirst && framesList.Items.Count > 0) SetSelection(new[] { 0 });
        else
        {
            RefreshCanvas(resetView: true);
            UpdateStatus();
        }

        framesList.ShowItemToolTips = true;
    }

    private void RefreshFrameLabels()
    {
        for (int i = 0; i < framesList.Items.Count && i < session.Count; i++)
        {
            string label = session[i].DisplayName;
            if (framesList.Items[i].Text != label) framesList.Items[i].Text = label;
            framesList.Items[i].ForeColor = session[i].IsDirty ? Theme.Accent : Theme.Text;
        }
    }

    private void SetSelection(IEnumerable<int> indices)
    {
        var wanted = indices.ToHashSet();

        suppressSelectionEvents = true;
        framesList.BeginUpdate();
        foreach (ListViewItem item in framesList.Items) item.Selected = wanted.Contains(item.Index);
        framesList.EndUpdate();
        suppressSelectionEvents = false;

        if (wanted.Count > 0)
        {
            int first = wanted.Min();
            if (first < framesList.Items.Count) framesList.Items[first].EnsureVisible();
        }

        // Switching frames keeps whatever zoom and pan the user set up.
        RefreshCanvas();
        UpdateStatus();
    }

    private void StepFrame(int delta)
    {
        if (framesList.Items.Count == 0) return;

        int current = framesList.SelectedIndices.Count > 0 ? framesList.SelectedIndices[0] : 0;
        int next = Math.Clamp(current + delta, 0, framesList.Items.Count - 1);
        if (next == current && framesList.SelectedIndices.Count == 1) return;

        suppressSelectionEvents = true;
        framesList.BeginUpdate();
        foreach (ListViewItem item in framesList.Items) item.Selected = item.Index == next;
        framesList.EndUpdate();
        suppressSelectionEvents = false;

        framesList.Items[next].EnsureVisible();
        RefreshCanvas();
        UpdateStatus();
    }

    // --------------------------------------------------------------- rendering

    private void RefreshCanvas(bool resetView = false)
    {
        var documents = SelectedDocuments();
        if (documents.Count == 0)
        {
            canvas.SetSurface(null, resetView);
            return;
        }

        var primary = documents[0];
        int width = primary.Image.Width;
        int height = primary.Image.Height;

        if (composite == null || composite.Width != width || composite.Height != height)
        {
            composite?.Dispose();
            composite = new Image<Rgba32>(width, height);
            resetView = true;
        }
        else
        {
            FrameOps.ClearTransparent(composite);
        }

        // While a filter dialog is open the frame is shown adjusted without touching the document.
        Image<Rgba32>? preview = null;
        if (previewAdjustment != null)
        {
            preview = primary.Image.Clone();
            previewAdjustment(preview);
        }

        composite.Mutate(context =>
        {
            if (settings.OnionSkin && documents.Count > 1)
            {
                foreach (var document in documents.Skip(1))
                {
                    context.DrawImage(document.Image, settings.OnionSkinOpacity);
                }
            }

            context.DrawImage(preview ?? primary.Image, 1f);
        });

        preview?.Dispose();

        displayBitmap = BitmapBridge.ToBitmap(composite, displayBitmap);
        canvas.SetSurface(displayBitmap, resetView);
    }

    private void UpdateStatus()
    {
        var documents = SelectedDocuments();
        var primary = documents.FirstOrDefault();

        statusFile.Text = primary == null
            ? session.Count == 0 ? "No frames open" : $"{session.Count} frames · nothing selected"
            : documents.Count == 1
                ? primary.FilePath
                : $"{primary.FileName} (+{documents.Count - 1} more selected)";

        statusSize.Text = primary == null ? "—" : $"{primary.Image.Width}×{primary.Image.Height}";
        statusZoom.Text = $"{canvas.Zoom * 100:0}%";

        undoMenuItem.Enabled = history.CanUndo;
        undoMenuItem.Text = history.CanUndo ? $"Undo {history.UndoLabel}" : "Undo";
        redoMenuItem.Enabled = history.CanRedo;
        redoMenuItem.Text = history.CanRedo ? $"Redo {history.RedoLabel}" : "Redo";
        statusDirty.Text = session.UnsavedCount > 0 ? $"● {session.UnsavedCount} unsaved" : string.Empty;

        string folder = session.CommonFolder ?? (session.Count > 0 ? "multiple folders" : string.Empty);
        Text = session.Count == 0
            ? "Frame Coloring"
            : $"Frame Coloring — {folder} ({session.Count} frames){(session.HasUnsavedChanges ? " *" : string.Empty)}";

        if (currentTool == EditorTool.Crop && canvas.CropOverlay is { } crop)
        {
            statusFile.Text = $"Crop {crop.Width}×{crop.Height} at {crop.X},{crop.Y} — drag the corners, then Apply Crop";
        }
    }

    private void SetStatusMessage(string message) => statusFile.Text = message;

    // ------------------------------------------------------------ form plumbing

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F: SetTool(EditorTool.Fill); return true;
            case Keys.R: SetTool(EditorTool.ReplaceFill); return true;
            case Keys.B: SetTool(EditorTool.Brush); return true;
            case Keys.E: SetTool(EditorTool.Eraser); return true;
            case Keys.I: SetTool(EditorTool.Picker); return true;
            case Keys.C: SetTool(EditorTool.Crop); return true;
            case Keys.Left: StepFrame(-1); return true;
            case Keys.Right: StepFrame(1); return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    private void MainForm_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void MainForm_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] dropped) return;

        var images = FrameSession.ExpandPaths(dropped).ToList();
        if (images.Count == 0) return;

        bool replace = (e.KeyState & 8) == 0; // Ctrl held -> add to the current set
        if (replace && !ConfirmDiscardChanges()) return;

        LoadPaths(images, replace);

        string? folder = Path.GetDirectoryName(images[0]);
        if (!string.IsNullOrEmpty(folder))
        {
            settings.LastOpenFolder = folder;
            settings.PushRecentFolder(folder);
            RebuildRecentMenu();
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }

        settings.Zoom = canvas.Zoom;
        settings.Save();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            composite?.Dispose();
            displayBitmap?.Dispose();
            history.Dispose();
            session.Dispose();
        }

        base.Dispose(disposing);
    }
}
