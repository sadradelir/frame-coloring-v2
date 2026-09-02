using FrameColoringV2.App;
using FrameColoringV2.Imaging;
using SixLabors.ImageSharp.Processing;

namespace FrameColoringV2.UI;

public sealed partial class MainForm
{
    private void BuildUi()
    {
        SuspendLayout();

        Text = "Frame Coloring";
        MinimumSize = new Size(1000, 640);
        Size = new Size(1500, 940);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;
        KeyPreview = true;
        AllowDrop = true;

        Controls.Add(BuildWorkspace());

        toolStrip = BuildToolStrip();
        Controls.Add(toolStrip);

        menuStrip = BuildMenu();
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;

        statusStrip = BuildStatusStrip();
        Controls.Add(statusStrip);

        Theme.ApplyTo(this);
        ResumeLayout();
    }

    /// <summary>Canvas on the left, a resizable sidebar docked to the right.</summary>
    private Control BuildWorkspace()
    {
        var workspace = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };

        canvas = new CanvasView { Dock = DockStyle.Fill };
        canvas.PixelMouseDown += Canvas_PixelMouseDown;
        canvas.PixelMouseDrag += Canvas_PixelMouseDrag;
        canvas.PixelMouseMove += Canvas_PixelMouseMove;
        canvas.PixelMouseUp += (_, _) => EndStroke();
        canvas.FrameStepRequested += (_, delta) => StepFrame(delta);
        canvas.ViewChanged += (_, _) => UpdateStatus();

        var sidebar = BuildSidebar();
        sidebar.Dock = DockStyle.Right;
        sidebar.Width = Math.Clamp(settings.SidebarWidth, 300, 640);

        var splitter = new Splitter
        {
            Dock = DockStyle.Right,
            Width = 4,
            BackColor = Theme.Border,
            MinExtra = 320,
            MinSize = 300
        };
        splitter.SplitterMoved += (_, _) => settings.SidebarWidth = sidebar.Width;

        // Docking is applied in reverse z-order, so the sidebar (added last) claims the
        // right edge, the splitter sits next to it and the canvas fills what is left.
        workspace.Controls.Add(canvas);
        workspace.Controls.Add(splitter);
        workspace.Controls.Add(sidebar);

        return workspace;
    }

    private TableLayoutPanel BuildSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Theme.Background,
            Padding = new Padding(10, 8, 10, 8)
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        sidebar.Controls.Add(BuildColorCard(), 0, 0);
        sidebar.Controls.Add(BuildPalettePanel(), 0, 1);
        sidebar.Controls.Add(BuildFramesHeader(), 0, 2);
        sidebar.Controls.Add(BuildFramesList(), 0, 3);

        return sidebar;
    }

    private Control BuildColorCard()
    {
        var card = new Panel
        {
            Dock = DockStyle.Top,
            Height = 130,
            BackColor = Theme.Surface,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 0, 8)
        };

        // Opacity sits at the bottom, the color row fills whatever is left.
        var opacityPanel = new Panel { Dock = DockStyle.Bottom, Height = 58 };

        opacityLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 18,
            Text = "Opacity 255",
            ForeColor = Theme.TextDim
        };
        opacityPanel.Controls.Add(opacityLabel);

        opacityTrack = new TrackBar
        {
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 255,
            Value = 255,
            TickStyle = TickStyle.None
        };
        opacityTrack.ValueChanged += (_, _) =>
        {
            currentColor.A = (byte)opacityTrack.Value;
            opacityLabel.Text = $"Opacity {opacityTrack.Value}";
            UpdateColorPreview();
        };
        opacityPanel.Controls.Add(opacityTrack);
        opacityTrack.BringToFront();

        var colorRow = new Panel { Dock = DockStyle.Fill };

        colorPreview = new Panel
        {
            Dock = DockStyle.Left,
            Width = 56,
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 8, 0)
        };
        colorRow.Controls.Add(colorPreview);

        var details = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 0, 0) };

        colorHexLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 20,
            Text = "#000000",
            Font = Theme.UiFontBold
        };
        details.Controls.Add(colorHexLabel);

        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 28,
            ColumnCount = 2,
            RowCount = 1
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));

        var customColorButton = new Button { Dock = DockStyle.Fill, Text = "Pick…", Margin = new Padding(0, 0, 4, 0) };
        customColorButton.Click += (_, _) => PickCustomColor();

        var addToPaletteButton = new Button { Dock = DockStyle.Fill, Text = "Add to palette", Margin = new Padding(0) };
        addToPaletteButton.Click += (_, _) => AddCurrentColorToPalette();

        buttonRow.Controls.Add(customColorButton, 0, 0);
        buttonRow.Controls.Add(addToPaletteButton, 1, 0);
        details.Controls.Add(buttonRow);

        // Docking is applied in reverse z-order: the hex label takes the top row,
        // the buttons the one under it.
        colorHexLabel.BringToFront();
        buttonRow.BringToFront();

        colorRow.Controls.Add(details);
        details.BringToFront();

        card.Controls.Add(colorRow);
        card.Controls.Add(opacityPanel);

        return card;
    }

    private Control BuildPalettePanel()
    {
        var container = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            Padding = new Padding(8),
            Margin = new Padding(0, 0, 0, 8)
        };

        paletteFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Surface,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        container.Controls.Add(paletteFlow);

        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = "PALETTE",
            ForeColor = Theme.TextDim,
            Font = Theme.UiFontBold
        };
        container.Controls.Add(header);
        header.SendToBack();
        paletteFlow.BringToFront();

        return container;
    }

    private Control BuildFramesHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 30,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Theme.Background,
            Margin = new Padding(0, 0, 0, 4)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "FRAMES",
            ForeColor = Theme.TextDim,
            Font = Theme.UiFontBold,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var selectAll = new Button { Dock = DockStyle.Fill, Text = "All", Margin = new Padding(2, 1, 2, 1) };
        selectAll.Click += (_, _) => SetSelection(Enumerable.Range(0, framesList.Items.Count));

        var selectNone = new Button { Dock = DockStyle.Fill, Text = "None", Margin = new Padding(2, 1, 2, 1) };
        selectNone.Click += (_, _) => SetSelection(Enumerable.Empty<int>());

        var invert = new Button { Dock = DockStyle.Fill, Text = "Invert", Margin = new Padding(2, 1, 0, 1) };
        invert.Click += (_, _) =>
        {
            var selected = framesList.SelectedIndices.Cast<int>().ToHashSet();
            SetSelection(Enumerable.Range(0, framesList.Items.Count).Where(i => !selected.Contains(i)));
        };

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(selectAll, 1, 0);
        header.Controls.Add(selectNone, 2, 0);
        header.Controls.Add(invert, 3, 0);

        return header;
    }

    private Control BuildFramesList()
    {
        framesList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            HeaderStyle = ColumnHeaderStyle.None,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None
        };
        framesList.Columns.Add("Frame", -2);
        framesList.SelectedIndexChanged += (_, _) =>
        {
            if (suppressSelectionEvents) return;
            RefreshCanvas();
            UpdateStatus();
        };
        framesList.Resize += (_, _) =>
        {
            if (framesList.Columns.Count > 0) framesList.Columns[0].Width = framesList.ClientSize.Width - 4;
        };

        // Right clicking a frame that is not part of the selection selects it first,
        // so the menu always acts on what the user pointed at.
        framesList.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;

            var item = framesList.GetItemAt(e.X, e.Y);
            if (item != null && !item.Selected) SetSelection(new[] { item.Index });
        };
        framesList.ContextMenuStrip = BuildFramesContextMenu();

        return framesList;
    }

    private ContextMenuStrip BuildFramesContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkStripRenderer(),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            ShowImageMargin = false
        };

        var save = new ToolStripMenuItem("Save", null, (_, _) => SaveSelected());
        var saveAs = new ToolStripMenuItem("Save As…", null, (_, _) => SaveSelectedAs());
        var export = new ToolStripMenuItem("Export To Folder…", null, (_, _) => ExportSelectedToFolder());
        var reload = new ToolStripMenuItem("Reload From Disk", null, (_, _) => ReloadSelected());
        var openExternal = new ToolStripMenuItem("Open In External Editor", null, (_, _) => OpenInExternalEditor());
        var reveal = new ToolStripMenuItem("Show In File Explorer", null, (_, _) => RevealInFileExplorer());
        var copyPath = new ToolStripMenuItem("Copy Full Path", null, (_, _) => CopySelectedPaths());
        var remove = new ToolStripMenuItem("Remove From List", null, (_, _) => RemoveSelectedFromList());

        menu.Items.AddRange(new ToolStripItem[]
        {
            save, saveAs, export,
            new ToolStripSeparator(),
            reload,
            new ToolStripSeparator(),
            openExternal, reveal, copyPath,
            new ToolStripSeparator(),
            remove
        });

        menu.Opening += (_, e) =>
        {
            int count = framesList.SelectedIndices.Count;
            if (count == 0)
            {
                e.Cancel = true;
                return;
            }

            save.Enabled = SelectedDocuments().Any(document => document.IsDirty);
            saveAs.Text = count == 1 ? "Save As…" : $"Save {count} Frames As…";
            copyPath.Text = count == 1 ? "Copy Full Path" : $"Copy {count} Full Paths";
            remove.Text = count == 1 ? "Remove From List" : $"Remove {count} From List";
        };

        return menu;
    }

    private ToolStrip BuildToolStrip()
    {
        toolStrip = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(6, 4, 6, 4),
            Renderer = new DarkStripRenderer(),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            ImageScalingSize = new Size(16, 16)
        };

        fillToolButton = CreateToolButton("Fill", EditorTool.Fill, "Flood fill the clicked area (F)");
        replaceToolButton = CreateToolButton("Replace", EditorTool.ReplaceFill, "Recolor the clicked key color, keeping shading (R)");
        brushToolButton = CreateToolButton("Brush", EditorTool.Brush, "Paint with the current color (B)");
        eraserToolButton = CreateToolButton("Eraser", EditorTool.Eraser, "Erase to transparent (E)");
        pickerToolButton = CreateToolButton("Picker", EditorTool.Picker, "Pick a color from the frame (I)");
        cropToolButton = CreateToolButton("Crop", EditorTool.Crop, "Drag the corners, then Apply Crop (C)");

        toolStrip.Items.AddRange(new ToolStripItem[]
        {
            fillToolButton, replaceToolButton, brushToolButton, eraserToolButton, pickerToolButton, cropToolButton,
            new ToolStripSeparator()
        });

        // Tool options. Only the ones that belong to the active tool are shown,
        // see UpdateToolOptions().
        toolOptionsSeparator = new ToolStripSeparator();
        toolStrip.Items.Add(toolOptionsSeparator);

        brushSizeUpDown = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 64,
            Value = settings.BrushSize,
            Width = 50,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None
        };
        brushSizeUpDown.ValueChanged += (_, _) => settings.BrushSize = (int)brushSizeUpDown.Value;

        brushSizeLabel = new ToolStripLabel("Brush size") { ForeColor = Theme.TextDim };
        brushSizeHost = new ToolStripControlHost(brushSizeUpDown);
        toolStrip.Items.Add(brushSizeLabel);
        toolStrip.Items.Add(brushSizeHost);

        fillToleranceUpDown = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 20,
            Value = settings.FillTolerance,
            Width = 50,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None
        };
        fillToleranceUpDown.ValueChanged += (_, _) => settings.FillTolerance = (int)fillToleranceUpDown.Value;

        fillBleedLabel = new ToolStripLabel("Fill bleed") { ForeColor = Theme.TextDim };
        fillBleedHost = new ToolStripControlHost(fillToleranceUpDown);
        toolStrip.Items.Add(fillBleedLabel);
        toolStrip.Items.Add(fillBleedHost);

        autoNextButton = new ToolStripButton("Auto next")
        {
            CheckOnClick = true,
            Checked = settings.AutoNextFrame,
            ForeColor = Theme.Text,
            ToolTipText = "After a fill, jump to the next frame in the list"
        };
        autoNextButton.CheckedChanged += (_, _) => settings.AutoNextFrame = autoNextButton.Checked;
        toolStrip.Items.Add(autoNextButton);

        applyCropButton = new ToolStripButton("Apply Crop") { ForeColor = Theme.Text };
        applyCropButton.Click += (_, _) => ApplyCrop();
        toolStrip.Items.Add(applyCropButton);

        toolStrip.Items.Add(new ToolStripSeparator());

        onionSkinButton = new ToolStripButton("Onion skin")
        {
            CheckOnClick = true,
            Checked = settings.OnionSkin,
            ForeColor = Theme.Text
        };
        onionSkinButton.CheckedChanged += (_, _) =>
        {
            settings.OnionSkin = onionSkinButton.Checked;
            if (onionSkinMenuItem != null) onionSkinMenuItem.Checked = onionSkinButton.Checked;
            RefreshCanvas();
        };
        toolStrip.Items.Add(onionSkinButton);

        var zoomOut = new ToolStripButton("Zoom −") { ForeColor = Theme.Text };
        zoomOut.Click += (_, _) => canvas.ZoomTo(canvas.Zoom / 1.25);
        var zoomIn = new ToolStripButton("Zoom +") { ForeColor = Theme.Text };
        zoomIn.Click += (_, _) => canvas.ZoomTo(canvas.Zoom * 1.25);
        var zoomFit = new ToolStripButton("Fit") { ForeColor = Theme.Text };
        zoomFit.Click += (_, _) => canvas.FitToWindow();
        var zoom100 = new ToolStripButton("100%") { ForeColor = Theme.Text };
        zoom100.Click += (_, _) => canvas.ZoomTo(1.0);

        toolStrip.Items.AddRange(new ToolStripItem[] { new ToolStripSeparator(), zoomOut, zoomIn, zoomFit, zoom100 });

        return toolStrip;
    }

    private ToolStripButton CreateToolButton(string text, EditorTool tool, string tooltip)
    {
        var button = new ToolStripButton(text)
        {
            ToolTipText = tooltip,
            ForeColor = Theme.Text,
            Tag = tool
        };
        button.Click += (_, _) => SetTool(tool);
        return button;
    }

    private MenuStrip BuildMenu()
    {
        var strip = new MenuStrip
        {
            Dock = DockStyle.Top,
            Renderer = new DarkStripRenderer(),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Padding = new Padding(6, 2, 0, 2)
        };

        recentFoldersMenu = new ToolStripMenuItem("Recent Folders");

        var fileMenu = new ToolStripMenuItem("File");
        fileMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            Menu("Open Folder…", Keys.Control | Keys.O, () => OpenFolderDialog(replaceExisting: true)),
            Menu("Open Files…", Keys.Control | Keys.Shift | Keys.O, () => OpenFilesDialog(replaceExisting: true)),
            Menu("Add Files…", Keys.None, () => OpenFilesDialog(replaceExisting: false)),
            Menu("Add Folder…", Keys.None, () => OpenFolderDialog(replaceExisting: false)),
            recentFoldersMenu,
            new ToolStripSeparator(),
            Menu("Save Selected", Keys.Control | Keys.S, SaveSelected),
            Menu("Save All", Keys.Control | Keys.Shift | Keys.S, SaveAll),
            Menu("Save Selected As…", Keys.None, SaveSelectedAs),
            Menu("Export Selected To Folder…", Keys.Control | Keys.E, ExportSelectedToFolder),
            new ToolStripSeparator(),
            Menu("Reload Selected", Keys.F5, ReloadSelected),
            Menu("Remove Selected From List", Keys.None, RemoveSelectedFromList),
            Menu("Close All", Keys.None, () => CloseAllFrames(askToSave: true)),
            new ToolStripSeparator(),
            Menu("Settings…", Keys.Control | Keys.Oemcomma, ShowSettings),
            Menu("Exit", Keys.Alt | Keys.F4, Close)
        });

        undoMenuItem = Menu("Undo", Keys.Control | Keys.Z, Undo);
        redoMenuItem = Menu("Redo", Keys.Control | Keys.Y, Redo);

        var editMenu = new ToolStripMenuItem("Edit");
        editMenu.DropDownItems.AddRange(new ToolStripItem[] { undoMenuItem, redoMenuItem });

        var viewMenu = new ToolStripMenuItem("View");
        onionSkinMenuItem = new ToolStripMenuItem("Onion Skin", null, (_, _) =>
        {
            onionSkinButton.Checked = !onionSkinButton.Checked;
        })
        { Checked = settings.OnionSkin, ShortcutKeys = Keys.Control | Keys.K };

        viewMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            Menu("Zoom In", Keys.Control | Keys.Oemplus, () => canvas.ZoomTo(canvas.Zoom * 1.25)),
            Menu("Zoom Out", Keys.Control | Keys.OemMinus, () => canvas.ZoomTo(canvas.Zoom / 1.25)),
            Menu("Fit To Window", Keys.Control | Keys.D0, () => canvas.FitToWindow()),
            Menu("Actual Size", Keys.Control | Keys.D1, () => canvas.ZoomTo(1.0)),
            new ToolStripSeparator(),
            onionSkinMenuItem,
            Menu("Next Frame", Keys.None, () => StepFrame(1)),
            Menu("Previous Frame", Keys.None, () => StepFrame(-1))
        });

        var toolsMenu = new ToolStripMenuItem("Tools");
        toolsMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            Menu("Empower Alpha", Keys.None, () => ApplyToSelected("Empower", image => FrameOps.Empower(image))),
            Menu("Drop Shadow", Keys.None, () => ApplyToSelected("SDF", image => FrameOps.SDF(image))),
            Menu("Trim To Content", Keys.None, TrimSelected),
            Menu("Flip Horizontally", Keys.None, () => ApplyToSelected("Flip", image =>
                image.Mutate(context => context.Flip(FlipMode.Horizontal)))),
            new ToolStripSeparator(),
            Menu("Analyze Uncolored Pixels", Keys.None, AnalyzeSelected),
            new ToolStripSeparator(),
            Menu("Import Unique Frames…", Keys.None, ImportUniqueFrames),
            Menu("Open In External Editor", Keys.None, OpenInExternalEditor),
            Menu("Set External Editor…", Keys.None, SetExternalEditor)
        });

        var filtersMenu = new ToolStripMenuItem("Filters");
        filtersMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            Menu("Curves…", Keys.Control | Keys.Shift | Keys.C, ApplyCurves),
            Menu("Gradient Map…", Keys.Control | Keys.G, ApplyGradientMap),
            Menu("Median…", Keys.Control | Keys.M, ApplyMedianFilter),
            new ToolStripSeparator(),
            Menu("Alpha → Distance Field…", Keys.None, BakeDistanceField),
            Menu("Verify Distance Field", Keys.None, VerifyDistanceField)
        });

        var helpMenu = new ToolStripMenuItem("Help");
        helpMenu.DropDownItems.Add(Menu("Shortcuts", Keys.F1, ShowShortcuts));

        strip.Items.AddRange(new ToolStripItem[] { fileMenu, editMenu, viewMenu, toolsMenu, filtersMenu, helpMenu });
        return strip;
    }

    private static ToolStripMenuItem Menu(string text, Keys shortcut, Action action)
    {
        var item = new ToolStripMenuItem(text, null, (_, _) => action());
        if (shortcut != Keys.None && shortcut != (Keys.Alt | Keys.F4)) item.ShortcutKeys = shortcut;
        return item;
    }

    private StatusStrip BuildStatusStrip()
    {
        var strip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            Renderer = new DarkStripRenderer(),
            BackColor = Theme.Surface,
            SizingGrip = false
        };

        statusFile = new ToolStripStatusLabel("No frames open") { ForeColor = Theme.Text, Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        statusSize = new ToolStripStatusLabel("—") { ForeColor = Theme.TextDim };
        statusCursor = new ToolStripStatusLabel("—") { ForeColor = Theme.TextDim, AutoSize = false, Width = 110, TextAlign = ContentAlignment.MiddleRight };
        statusZoom = new ToolStripStatusLabel("100%") { ForeColor = Theme.TextDim, AutoSize = false, Width = 70, TextAlign = ContentAlignment.MiddleRight };
        statusDirty = new ToolStripStatusLabel(string.Empty) { ForeColor = Theme.Accent };

        strip.Items.AddRange(new ToolStripItem[] { statusFile, statusDirty, statusSize, statusCursor, statusZoom });
        return strip;
    }

    private void RebuildPalette()
    {
        paletteFlow.Controls.Clear();

        foreach (PaletteEntry entry in settings.Palette)
        {
            Color color;
            try
            {
                color = ColorTranslator.FromHtml(entry.Hex);
            }
            catch (Exception)
            {
                continue;
            }

            var swatch = new Button
            {
                Size = new Size(84, 30),
                Margin = new Padding(3),
                Text = entry.Name,
                BackColor = color,
                ForeColor = ContrastColor(color),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            swatch.FlatAppearance.BorderColor = Theme.Border;
            swatch.FlatAppearance.BorderSize = 1;
            swatch.Tag = entry;
            swatch.Click += (_, _) => SetCurrentColor(color);

            var contextMenu = new ContextMenuStrip { Renderer = new DarkStripRenderer(), BackColor = Theme.Surface };
            contextMenu.Items.Add(new ToolStripMenuItem("Rename…", null, (_, _) => RenamePaletteEntry(entry)));
            contextMenu.Items.Add(new ToolStripMenuItem("Remove", null, (_, _) => RemovePaletteEntry(entry)));
            swatch.ContextMenuStrip = contextMenu;

            var tooltip = new ToolTip();
            tooltip.SetToolTip(swatch, $"{entry.Name} · {entry.Hex}");

            paletteFlow.Controls.Add(swatch);
        }
    }

    private static Color ContrastColor(Color color)
    {
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        return luminance > 0.55 ? Color.FromArgb(20, 20, 20) : Color.White;
    }

    private void RebuildRecentMenu()
    {
        recentFoldersMenu.DropDownItems.Clear();

        if (settings.RecentFolders.Count == 0)
        {
            recentFoldersMenu.DropDownItems.Add(new ToolStripMenuItem("(empty)") { Enabled = false });
            return;
        }

        foreach (string folder in settings.RecentFolders.ToList())
        {
            string captured = folder;
            recentFoldersMenu.DropDownItems.Add(new ToolStripMenuItem(captured, null, (_, _) => OpenFolder(captured, true)));
        }

        recentFoldersMenu.DropDownItems.Add(new ToolStripSeparator());
        recentFoldersMenu.DropDownItems.Add(new ToolStripMenuItem("Clear list", null, (_, _) =>
        {
            settings.RecentFolders.Clear();
            RebuildRecentMenu();
        }));
    }
}
