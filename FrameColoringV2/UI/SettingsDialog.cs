using FrameColoringV2.App;

namespace FrameColoringV2.UI;

/// <summary>
/// Application settings. The left hand list holds one entry per section; today there is
/// "Canvas" and "Interface"; new sections are added by writing another BuildXxxPage method and
/// registering it in <see cref="pages"/>.
/// </summary>
public sealed class SettingsDialog : Form
{
    private readonly AppSettings settings;
    private readonly Dictionary<string, Func<Control>> pages;
    private readonly ListBox categoryList;
    private readonly Panel pageHost;

    // Working copy of the values, written back to the settings only on OK.
    private Color checkerLight;
    private Color checkerDark;
    private int checkerSquare;
    private int uiScalePercent;

    private Panel checkerPreview = null!;
    private Button checkerLightButton = null!;
    private Button checkerDarkButton = null!;
    private NumericUpDown checkerSizeInput = null!;

    /// <summary>Raised whenever a value changes so the editor can show it live.</summary>
    public event EventHandler? PreviewChanged;

    public SettingsDialog(AppSettings settings)
    {
        this.settings = settings;

        checkerLight = ParseColor(settings.CheckerLightColor, AppSettings.DefaultCheckerLight);
        checkerDark = ParseColor(settings.CheckerDarkColor, AppSettings.DefaultCheckerDark);
        checkerSquare = Math.Clamp(settings.CheckerSquareSize, 2, 64);
        uiScalePercent = Math.Clamp(settings.UiScalePercent, 100, 200);

        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(640, 380);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;

        pages = new Dictionary<string, Func<Control>>
        {
            ["Canvas"] = BuildCanvasPage,
            ["Interface"] = BuildInterfacePage
        };

        pageHost = new Panel
        {
            Location = new Point(176, 16),
            Size = new Size(448, 300),
            BackColor = Theme.Surface,
            Padding = new Padding(16)
        };
        Controls.Add(pageHost);

        categoryList = new ListBox
        {
            Location = new Point(16, 16),
            Size = new Size(148, 300),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            ItemHeight = 24
        };
        categoryList.Items.AddRange(pages.Keys.Cast<object>().ToArray());
        categoryList.SelectedIndexChanged += (_, _) => ShowPage(categoryList.SelectedItem as string);
        Controls.Add(categoryList);

        BuildButtonRow();

        Theme.ScaleForm(this);
        categoryList.SelectedIndex = 0;
    }

    private void BuildButtonRow()
    {
        var restoreDefaults = new Button
        {
            Location = new Point(16, 330),
            Size = new Size(140, 30),
            Text = "Restore defaults"
        };
        restoreDefaults.Click += (_, _) => RestoreDefaults();
        Theme.StyleButton(restoreDefaults);
        Controls.Add(restoreDefaults);

        var ok = new Button
        {
            Location = new Point(462, 330),
            Size = new Size(76, 30),
            Text = "OK",
            DialogResult = DialogResult.OK
        };
        Theme.StyleButton(ok, primary: true);
        Controls.Add(ok);

        var cancel = new Button
        {
            Location = new Point(548, 330),
            Size = new Size(76, 30),
            Text = "Cancel",
            DialogResult = DialogResult.Cancel
        };
        Theme.StyleButton(cancel);
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;

        FormClosing += (_, _) =>
        {
            if (DialogResult == DialogResult.OK) Apply();
            else PreviewChanged?.Invoke(this, EventArgs.Empty); // undo the live preview
        };
    }

    private void ShowPage(string? category)
    {
        pageHost.Controls.Clear();
        if (category == null || !pages.TryGetValue(category, out var factory)) return;

        // Pages are laid out at scale 1 and grown to match the rest of the dialog.
        var page = factory();
        if (Theme.Scale > 1f) page.Scale(new SizeF(Theme.Scale, Theme.Scale));
        page.Dock = DockStyle.Fill;
        pageHost.Controls.Add(page);
    }

    // ------------------------------------------------------------------ canvas

    private Control BuildCanvasPage()
    {
        var page = new Panel { BackColor = Theme.Surface };

        var title = new Label
        {
            Location = new Point(0, 0),
            Size = new Size(400, 20),
            Text = "TRANSPARENCY CHECKERBOARD",
            ForeColor = Theme.TextDim,
            Font = Theme.UiFontBold
        };
        page.Controls.Add(title);

        checkerPreview = new Panel
        {
            Location = new Point(0, 28),
            Size = new Size(416, 96),
            BorderStyle = BorderStyle.FixedSingle
        };
        checkerPreview.Paint += CheckerPreview_Paint;
        page.Controls.Add(checkerPreview);

        page.Controls.Add(new Label
        {
            Location = new Point(0, 140),
            Size = new Size(110, 24),
            Text = "Light squares",
            TextAlign = ContentAlignment.MiddleLeft
        });

        checkerLightButton = new Button { Location = new Point(116, 138), Size = new Size(120, 26) };
        checkerLightButton.Click += (_, _) => PickCheckerColor(isLight: true);
        page.Controls.Add(checkerLightButton);

        page.Controls.Add(new Label
        {
            Location = new Point(0, 176),
            Size = new Size(110, 24),
            Text = "Dark squares",
            TextAlign = ContentAlignment.MiddleLeft
        });

        checkerDarkButton = new Button { Location = new Point(116, 174), Size = new Size(120, 26) };
        checkerDarkButton.Click += (_, _) => PickCheckerColor(isLight: false);
        page.Controls.Add(checkerDarkButton);

        page.Controls.Add(new Label
        {
            Location = new Point(0, 212),
            Size = new Size(110, 24),
            Text = "Square size",
            TextAlign = ContentAlignment.MiddleLeft
        });

        checkerSizeInput = new NumericUpDown
        {
            Location = new Point(116, 210),
            Size = new Size(70, 26),
            Minimum = 2,
            Maximum = 64,
            Value = checkerSquare,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        checkerSizeInput.ValueChanged += (_, _) =>
        {
            checkerSquare = (int)checkerSizeInput.Value;
            RefreshCheckerControls();
        };
        page.Controls.Add(checkerSizeInput);

        page.Controls.Add(new Label
        {
            Location = new Point(192, 212),
            Size = new Size(60, 24),
            Text = "pixels",
            ForeColor = Theme.TextDim,
            TextAlign = ContentAlignment.MiddleLeft
        });

        RefreshCheckerControls();
        return page;
    }

    // --------------------------------------------------------------- interface

    private Control BuildInterfacePage()
    {
        var page = new Panel { BackColor = Theme.Surface };

        page.Controls.Add(new Label
        {
            Location = new Point(0, 0),
            Size = new Size(400, 20),
            Text = "INTERFACE SCALE",
            ForeColor = Theme.TextDim,
            Font = Theme.UiFontBold
        });

        page.Controls.Add(new Label
        {
            Location = new Point(0, 34),
            Size = new Size(110, 24),
            Text = "Scale",
            TextAlign = ContentAlignment.MiddleLeft
        });

        var scaleInput = new NumericUpDown
        {
            Location = new Point(116, 32),
            Size = new Size(70, 26),
            Minimum = 100,
            Maximum = 200,
            Increment = 10,
            Value = uiScalePercent,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        page.Controls.Add(scaleInput);

        page.Controls.Add(new Label
        {
            Location = new Point(192, 34),
            Size = new Size(60, 24),
            Text = "percent",
            ForeColor = Theme.TextDim,
            TextAlign = ContentAlignment.MiddleLeft
        });

        var scaleTrack = new TrackBar
        {
            Location = new Point(0, 68),
            Size = new Size(416, 40),
            Minimum = 100,
            Maximum = 200,
            SmallChange = 10,
            LargeChange = 20,
            TickFrequency = 10,
            Value = uiScalePercent,
            BackColor = Theme.Surface
        };
        page.Controls.Add(scaleTrack);

        // Both controls edit the same value, so keep them pointing at each other.
        scaleInput.ValueChanged += (_, _) =>
        {
            uiScalePercent = (int)scaleInput.Value;
            if (scaleTrack.Value != uiScalePercent) scaleTrack.Value = uiScalePercent;
        };
        scaleTrack.ValueChanged += (_, _) =>
        {
            uiScalePercent = scaleTrack.Value;
            if ((int)scaleInput.Value != uiScalePercent) scaleInput.Value = uiScalePercent;
        };

        page.Controls.Add(new Label
        {
            Location = new Point(0, 116),
            Size = new Size(416, 60),
            Text = "Makes the buttons, the frame list and the text bigger, which is easier to hit "
                 + "with the mouse. The frames themselves are not affected."
                 + Environment.NewLine + Environment.NewLine
                 + "The new size is applied when the app is restarted.",
            ForeColor = Theme.TextDim
        });

        return page;
    }

    private void CheckerPreview_Paint(object? sender, PaintEventArgs e)
    {
        using var brush = CanvasView.CreateCheckerBrush(checkerLight, checkerDark, checkerSquare);
        e.Graphics.FillRectangle(brush, checkerPreview.ClientRectangle);
    }

    private void PickCheckerColor(bool isLight)
    {
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            Color = isLight ? checkerLight : checkerDark
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        if (isLight) checkerLight = dialog.Color;
        else checkerDark = dialog.Color;

        RefreshCheckerControls();
    }

    private void RefreshCheckerControls()
    {
        checkerLightButton.BackColor = checkerLight;
        checkerLightButton.ForeColor = ContrastColor(checkerLight);
        checkerLightButton.Text = ToHex(checkerLight);
        checkerLightButton.FlatStyle = FlatStyle.Flat;
        checkerLightButton.FlatAppearance.BorderColor = Theme.Border;

        checkerDarkButton.BackColor = checkerDark;
        checkerDarkButton.ForeColor = ContrastColor(checkerDark);
        checkerDarkButton.Text = ToHex(checkerDark);
        checkerDarkButton.FlatStyle = FlatStyle.Flat;
        checkerDarkButton.FlatAppearance.BorderColor = Theme.Border;

        if (checkerSizeInput.Value != checkerSquare) checkerSizeInput.Value = checkerSquare;

        checkerPreview.Invalidate();
        PreviewSettings();
    }

    private void RestoreDefaults()
    {
        checkerLight = ParseColor(AppSettings.DefaultCheckerLight, AppSettings.DefaultCheckerLight);
        checkerDark = ParseColor(AppSettings.DefaultCheckerDark, AppSettings.DefaultCheckerDark);
        checkerSquare = AppSettings.DefaultCheckerSquareSize;
        uiScalePercent = 100;
        RefreshCheckerControls();
        ShowPage(categoryList.SelectedItem as string);
    }

    // ------------------------------------------------------------------ result

    /// <summary>The values as they are right now, so the editor can preview them.</summary>
    public (Color light, Color dark, int square) Checkerboard => (checkerLight, checkerDark, checkerSquare);

    /// <summary>The interface scale the user picked, which only takes effect on the next start.</summary>
    public int UiScalePercent => uiScalePercent;

    private void PreviewSettings() => PreviewChanged?.Invoke(this, EventArgs.Empty);

    private void Apply()
    {
        settings.CheckerLightColor = ToHex(checkerLight);
        settings.CheckerDarkColor = ToHex(checkerDark);
        settings.CheckerSquareSize = checkerSquare;
        settings.UiScalePercent = uiScalePercent;
    }

    public static Color ParseColor(string? hex, string fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex)) return ColorTranslator.FromHtml(hex);
        }
        catch (Exception)
        {
            // fall through to the default below
        }

        return ColorTranslator.FromHtml(fallback);
    }

    private static string ToHex(Color color) => $"#{color.R:x2}{color.G:x2}{color.B:x2}";

    private static Color ContrastColor(Color color)
    {
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        return luminance > 0.55 ? Color.FromArgb(20, 20, 20) : Color.White;
    }
}
