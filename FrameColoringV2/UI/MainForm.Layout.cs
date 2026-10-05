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
        // Scaled up, the design size can outgrow the screen, so keep the window on it.
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        MinimumSize = new Size(
            Math.Min(Theme.Px(1000), workingArea.Width),
            Math.Min(Theme.Px(640), workingArea.Height));
        Size = new Size(
            Math.Min(Theme.Px(1500), workingArea.Width),
            Math.Min(Theme.Px(940), workingArea.Height));
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
        sidebar.Width = Math.Clamp(settings.SidebarWidth, Theme.Px(300), Theme.Px(700));

        var splitter = new Splitter
        {
            Dock = DockStyle.Right,
            Width = Theme.Px(4),
            BackColor = Theme.Border,
            MinExtra = Theme.Px(320),
            MinSize = Theme.Px(300)
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
            RowCount = 5,
            BackColor = Theme.Background,
            Padding = new Padding(Theme.Px(10), Theme.Px(8), Theme.Px(10), Theme.Px(8))
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Px(230)));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        sidebar.Controls.Add(BuildColorCard(), 0, 0);
        sidebar.Controls.Add(BuildPalettePanel(), 0, 1);
        sidebar.Controls.Add(BuildFramesHeader(), 0, 2);
        sidebar.Controls.Add(BuildFramesList(), 0, 3);
        sidebar.Controls.Add(BuildFramesActionBar(), 0, 4);

        return sidebar;
    }

    private Control BuildColorCard()
    {
        var card = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.Px(130),
            BackColor = Theme.Surface,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 0, 8)
        };

        // Opacity sits at the bottom, the color row fills whatever is left.
        var opacityPanel = new Panel { Dock = DockStyle.Bottom, Height = Theme.Px(58) };

        opacityLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Px(18),
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
            Width = Theme.Px(56),
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 8, 0)
        };
        colorRow.Controls.Add(colorPreview);

        var details = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 0, 0) };

        colorHexLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Px(20),
            Text = "#000000",
            Font = Theme.UiFontBold
        };
        details.Controls.Add(colorHexLabel);

        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = Theme.Px(28),
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
            Height = Theme.Px(22),
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
            Height = Theme.Px(30),
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Theme.Background,
            Margin = new Padding(0, 0, 0, 4)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Px(34)));

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "FRAMES",
            ForeColor = Theme.TextDim,
            Font = Theme.UiFontBold,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var selectAll = IconButton("select-all", "Select every frame", () => SetSelection(Enumerable.Range(0, framesList.Items.Count)));
        selectAll.Dock = DockStyle.Fill;
        selectAll.Margin = new Padding(2, 1, 2, 1);

        var selectNone = IconButton("select-none", "Clear the selection", () => SetSelection(Enumerable.Empty<int>()));
        selectNone.Dock = DockStyle.Fill;
        selectNone.Margin = new Padding(2, 1, 2, 1);

        var invert = IconButton("select-invert", "Invert the selection", () =>
        {
            var selected = framesList.SelectedIndices.Cast<int>().ToHashSet();
            SetSelection(Enumerable.Range(0, framesList.Items.Count).Where(i => !selected.Contains(i)));
        });
        invert.Dock = DockStyle.Fill;
        invert.Margin = new Padding(2, 1, 0, 1);

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
        framesIcons = new ImageList
        {
            ImageSize = new Size(Icons.Small, Icons.Small),
            ColorDepth = ColorDepth.Depth32Bit
        };
        framesIcons.Images.Add("none", new Bitmap(Icons.Small, Icons.Small));       // keeps unstarred rows aligned
        framesIcons.Images.Add("done", Icons.Get("star", Icons.Small, Icons.Star));
        framesList.SmallImageList = framesIcons;

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
        framesList.DoubleClick += (_, _) => ToggleDoneOnSelection();
        framesList.ContextMenuStrip = BuildFramesContextMenu();

        return framesList;
    }

    /// <summary>The row of icon buttons under the frame list: star what is finished, then move on.</summary>
    private Control BuildFramesActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Surface,
            Padding = new Padding(4),
            Margin = new Padding(0, 4, 0, 0)
        };

        starButton = IconButton("star-outline", "Star the selected frames as finished (S)", ToggleDoneOnSelection);

        bar.Controls.Add(starButton);
        bar.Controls.Add(IconButton("next", "Go to the next frame that is not starred yet", JumpToNextPendingFrame));
        bar.Controls.Add(IconButton("star-clear", "Clear every star in this folder", ClearAllDone));
        bar.Controls.Add(new Label { Width = Theme.Px(10), Height = 1, Margin = new Padding(0) });
        bar.Controls.Add(IconButton("save", "Save the selected frames", SaveSelected));
        bar.Controls.Add(IconButton("reload", "Reload the selected frames from disk", ReloadSelected));
        bar.Controls.Add(IconButton("trash", "Remove the selected frames from the list", RemoveSelectedFromList));

        return bar;
    }

    /// <summary>A square, flat button that shows an icon and explains itself through its tooltip.</summary>
    private Button IconButton(string icon, string tooltip, Action onClick)
    {
        var button = new Button
        {
            Width = Theme.Px(30),
            Height = Theme.Px(26),
            Text = string.Empty,
            Image = Icons.Get(icon, Icons.Small),
            Margin = new Padding(0, 0, 4, 0)
        };

        Theme.StyleButton(button);
        toolTips.SetToolTip(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
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
        var toggleDone = new ToolStripMenuItem("Star As Finished", null, (_, _) => ToggleDoneOnSelection())
        {
            ShortcutKeyDisplayString = "S"
        };

        menu.Items.AddRange(new ToolStripItem[]
        {
            toggleDone,
            new ToolStripSeparator(),
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

            var documents = SelectedDocuments();
            bool allDone = documents.Count > 0 && documents.All(document => document.IsDone);
            toggleDone.Text = allDone
                ? count == 1 ? "Remove Star" : $"Remove {count} Stars"
                : count == 1 ? "Star As Finished" : $"Star {count} Frames As Finished";

            save.Enabled = documents.Any(document => document.IsDirty);
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
            Padding = new Padding(Theme.Px(6), Theme.Px(4), Theme.Px(6), Theme.Px(4)),
            Renderer = new DarkStripRenderer(),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            ImageScalingSize = new Size(Icons.Toolbar, Icons.Toolbar)
        };

        fillToolButton = CreateToolButton("Fill", "fill", EditorTool.Fill, "Fill — flood fill the clicked area (F)");
        replaceToolButton = CreateToolButton("Replace", "replace", EditorTool.ReplaceFill, "Replace fill — recolor the clicked key color, keeping shading (R)");
        brushToolButton = CreateToolButton("Brush", "brush", EditorTool.Brush, "Brush — paint with the current color, behind the line art by default (B)");
        eraserToolButton = CreateToolButton("Eraser", "eraser", EditorTool.Eraser, "Eraser — erase to transparent (E)");
        pickerToolButton = CreateToolButton("Picker", "picker", EditorTool.Picker, "Picker — pick a color from the frame (I)");
        cropToolButton = CreateToolButton("Crop", "crop", EditorTool.Crop, "Crop — drag the corners, then apply (C)");

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
            Width = Theme.Px(50),
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None
        };
        brushSizeUpDown.ValueChanged += (_, _) => settings.BrushSize = (int)brushSizeUpDown.Value;

        brushSizeLabel = new ToolStripLabel("Brush size") { ForeColor = Theme.TextDim };
        brushSizeHost = new ToolStripControlHost(brushSizeUpDown);
        toolStrip.Items.Add(brushSizeLabel);
        toolStrip.Items.Add(brushSizeHost);

        brushBehindItem = new ToolStripMenuItem("Behind — transparent pixels only")
        {
            ToolTipText = "Lays the color under what is already drawn, so the line art is safe"
        };
        brushBehindItem.Click += (_, _) => SetBrushMode(BrushMode.Behind);

        brushOverItem = new ToolStripMenuItem("Over everything")
        {
            ToolTipText = "Paints on top of every pixel, like an ordinary brush"
        };
        brushOverItem.Click += (_, _) => SetBrushMode(BrushMode.Over);

        brushModeButton = new ToolStripDropDownButton { ForeColor = Theme.Text };
        brushModeButton.DropDownItems.Add(brushBehindItem);
        brushModeButton.DropDownItems.Add(brushOverItem);
        toolStrip.Items.Add(brushModeButton);

        RefreshBrushMode();

        fillToleranceUpDown = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 20,
            Value = settings.FillTolerance,
            Width = Theme.Px(50),
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None
        };
        fillToleranceUpDown.ValueChanged += (_, _) => settings.FillTolerance = (int)fillToleranceUpDown.Value;

        fillBleedLabel = new ToolStripLabel("Fill bleed") { ForeColor = Theme.TextDim };
        fillBleedHost = new ToolStripControlHost(fillToleranceUpDown);
        toolStrip.Items.Add(fillBleedLabel);
        toolStrip.Items.Add(fillBleedHost);

        autoNextButton = StripIconButton("Auto next", "next", "Auto next — after a fill, jump to the next unstarred frame");
        autoNextButton.CheckOnClick = true;
        autoNextButton.Checked = settings.AutoNextFrame;
        autoNextButton.CheckedChanged += (_, _) =>
        {
            settings.AutoNextFrame = autoNextButton.Checked;
            UpdateToolOptions();
        };
        toolStrip.Items.Add(autoNextButton);

        autoNextImmediateItem = new ToolStripMenuItem("Immediate")
        {
            ToolTipText = "Jump to the next frame as soon as the fill is applied"
        };
        autoNextImmediateItem.Click += (_, _) => SetAutoNextMode(AutoNextMode.Immediate);

        autoNextDelayedItem = new ToolStripMenuItem($"Delayed ({AppSettings.DefaultAutoNextDelayMs} ms)")
        {
            ToolTipText = "Stay on the frame for a moment first, so the fill can be checked"
        };
        autoNextDelayedItem.Click += (_, _) => SetAutoNextMode(AutoNextMode.Delayed);

        autoNextModeButton = new ToolStripDropDownButton
        {
            ForeColor = Theme.Text,
            ToolTipText = "When Auto next moves on"
        };
        autoNextModeButton.DropDownItems.Add(autoNextImmediateItem);
        autoNextModeButton.DropDownItems.Add(autoNextDelayedItem);
        toolStrip.Items.Add(autoNextModeButton);

        RefreshAutoNextMode();

        applyCropButton = StripIconButton("Apply Crop", "check", "Apply the crop to the selected frames");
        applyCropButton.Click += (_, _) => ApplyCrop();
        toolStrip.Items.Add(applyCropButton);

        toolStrip.Items.Add(new ToolStripSeparator());

        onionSkinButton = StripIconButton("Onion skin", "onion", "Onion skin — show the other selected frames underneath");
        onionSkinButton.CheckOnClick = true;
        onionSkinButton.Checked = settings.OnionSkin;
        onionSkinButton.CheckedChanged += (_, _) =>
        {
            settings.OnionSkin = onionSkinButton.Checked;
            if (onionSkinMenuItem != null) onionSkinMenuItem.Checked = onionSkinButton.Checked;
            RefreshCanvas();
        };
        toolStrip.Items.Add(onionSkinButton);

        var zoomOut = StripIconButton("Zoom out", "zoom-out", "Zoom out");
        zoomOut.Click += (_, _) => canvas.ZoomTo(canvas.Zoom / 1.25);
        var zoomIn = StripIconButton("Zoom in", "zoom-in", "Zoom in");
        zoomIn.Click += (_, _) => canvas.ZoomTo(canvas.Zoom * 1.25);
        var zoomFit = StripIconButton("Fit", "fit", "Fit the frame to the window");
        zoomFit.Click += (_, _) => canvas.FitToWindow();
        var zoom100 = StripIconButton("100%", "actual-size", "Zoom to 100%");
        zoom100.Click += (_, _) => canvas.ZoomTo(1.0);

        toolStrip.Items.AddRange(new ToolStripItem[] { new ToolStripSeparator(), zoomOut, zoomIn, zoomFit, zoom100 });

        return toolStrip;
    }

    private ToolStripButton CreateToolButton(string text, string icon, EditorTool tool, string tooltip)
    {
        var button = new ToolStripButton(text, Icons.Get(icon))
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            AutoToolTip = false,
            ToolTipText = tooltip,
            ForeColor = Theme.Text,
            Tag = tool
        };
        button.Click += (_, _) => SetTool(tool);
        return button;
    }

    /// <summary>Icon-only strip button. The label stays on the item so the tooltip can spell it out.</summary>
    private static ToolStripButton StripIconButton(string text, string icon, string tooltip)
    {
        return new ToolStripButton(text, Icons.Get(icon))
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            AutoToolTip = false,
            ToolTipText = tooltip,
            ForeColor = Theme.Text
        };
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
        statusCursor = new ToolStripStatusLabel("—") { ForeColor = Theme.TextDim, AutoSize = false, Width = Theme.Px(110), TextAlign = ContentAlignment.MiddleRight };
        statusZoom = new ToolStripStatusLabel("100%") { ForeColor = Theme.TextDim, AutoSize = false, Width = Theme.Px(70), TextAlign = ContentAlignment.MiddleRight };
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
                Size = new Size(Theme.Px(84), Theme.Px(30)),
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
