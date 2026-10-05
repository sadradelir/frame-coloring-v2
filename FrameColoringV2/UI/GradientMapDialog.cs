using FrameColoringV2.Imaging;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace FrameColoringV2.UI;

/// <summary>
/// Photoshop style Gradient Map: the brightness of every pixel picks a colour out of a ramp.
/// </summary>
public sealed class GradientMapDialog : Form
{
    private readonly GradientBar bar;
    private readonly ComboBox presetBox;
    private readonly Button stopColorButton;
    private readonly NumericUpDown stopPositionInput;
    private readonly CheckBox reverseBox;
    private readonly CheckBox previewBox;
    private readonly TrackBar amountSlider;
    private readonly Label amountLabel;
    private readonly RadioButton selectedScope;
    private readonly RadioButton allScope;

    private bool syncingStop;

    public GradientMapDialog(Gradient gradient, bool reverse, int amountPercent, int selectedCount, int totalCount)
    {
        Text = "Gradient Map";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(452, 458);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;

        Controls.Add(new Label
        {
            Location = new Point(16, 12),
            Size = new Size(420, 40),
            ForeColor = Theme.TextDim,
            Text = "Every pixel's brightness picks a colour from the ramp: darks take the left " +
                   "end, lights the right one."
        });

        Controls.Add(new Label
        {
            Location = new Point(16, 56),
            Size = new Size(56, 24),
            Text = "Preset",
            TextAlign = ContentAlignment.MiddleLeft
        });

        presetBox = new ComboBox
        {
            Location = new Point(76, 54),
            Size = new Size(166, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat
        };
        presetBox.Items.Add("Custom");
        foreach (var preset in Gradient.Presets) presetBox.Items.Add(preset.Name);
        presetBox.SelectedIndex = 0;
        presetBox.SelectedIndexChanged += (_, _) => ApplyPreset();
        Controls.Add(presetBox);

        reverseBox = new CheckBox
        {
            Location = new Point(256, 54),
            Size = new Size(90, 24),
            Text = "Reverse",
            Checked = reverse,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text
        };
        reverseBox.CheckedChanged += (_, _) =>
        {
            bar.Reverse = reverseBox.Checked;
            RaisePreview();
        };
        Controls.Add(reverseBox);

        var flipButton = new Button
        {
            Location = new Point(340, 53),
            Size = new Size(96, 26),
            Text = "Swap ends"
        };
        flipButton.Click += (_, _) => FlipStops();
        Theme.StyleButton(flipButton);
        Controls.Add(flipButton);

        bar = new GradientBar
        {
            Location = new Point(16, 88),
            Size = new Size(420, 74),
            Gradient = gradient,
            Reverse = reverse
        };
        bar.GradientChanged += (_, _) => UpdateStopControls();
        bar.GradientCommitted += (_, _) =>
        {
            MarkCustom();
            RaisePreview();
        };
        bar.SelectionChanged += (_, _) => UpdateStopControls();
        Controls.Add(bar);

        Controls.Add(new Label
        {
            Location = new Point(16, 170),
            Size = new Size(420, 40),
            Text = "Click the strip to add a stop · drag to move · double click to recolour · " +
                   "right click to remove",
            ForeColor = Theme.TextDim
        });

        Controls.Add(new Label
        {
            Location = new Point(16, 210),
            Size = new Size(40, 26),
            Text = "Stop",
            TextAlign = ContentAlignment.MiddleLeft
        });

        stopColorButton = new Button
        {
            Location = new Point(60, 209),
            Size = new Size(96, 26),
            Text = "#000000"
        };
        stopColorButton.Click += (_, _) => bar.EditSelectedColor();
        Theme.StyleButton(stopColorButton);
        Controls.Add(stopColorButton);

        Controls.Add(new Label
        {
            Location = new Point(170, 210),
            Size = new Size(70, 26),
            Text = "Position",
            TextAlign = ContentAlignment.MiddleLeft
        });

        stopPositionInput = new NumericUpDown
        {
            Location = new Point(244, 209),
            Size = new Size(60, 26),
            Minimum = 0,
            Maximum = 100,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        stopPositionInput.ValueChanged += (_, _) => MoveSelectedStop();
        Controls.Add(stopPositionInput);

        Controls.Add(new Label
        {
            Location = new Point(308, 210),
            Size = new Size(30, 26),
            Text = "%",
            ForeColor = Theme.TextDim,
            TextAlign = ContentAlignment.MiddleLeft
        });

        amountLabel = new Label
        {
            Location = new Point(16, 250),
            Size = new Size(130, 20),
            Text = $"Amount {amountPercent}%",
            ForeColor = Theme.TextDim
        };
        Controls.Add(amountLabel);

        amountSlider = new TrackBar
        {
            Location = new Point(150, 244),
            Size = new Size(286, 34),
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(amountPercent, 0, 100),
            TickStyle = TickStyle.None
        };
        amountSlider.ValueChanged += (_, _) =>
        {
            amountLabel.Text = $"Amount {amountSlider.Value}%";
            RaisePreview();
        };
        Controls.Add(amountSlider);

        previewBox = new CheckBox
        {
            Location = new Point(16, 284),
            Size = new Size(420, 24),
            Text = "Preview on the canvas",
            Checked = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text
        };
        previewBox.CheckedChanged += (_, _) => RaisePreview();
        Controls.Add(previewBox);

        Controls.Add(new Label
        {
            Location = new Point(16, 314),
            Size = new Size(420, 20),
            Text = "APPLY TO",
            ForeColor = Theme.TextDim,
            Font = Theme.UiFontBold
        });

        selectedScope = new RadioButton
        {
            Location = new Point(16, 336),
            Size = new Size(420, 24),
            Text = selectedCount == 1 ? "The selected frame" : $"The {selectedCount} selected frames",
            Checked = selectedCount > 0,
            Enabled = selectedCount > 0,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text
        };
        Controls.Add(selectedScope);

        allScope = new RadioButton
        {
            Location = new Point(16, 362),
            Size = new Size(420, 24),
            Text = $"All {totalCount} open frames",
            Checked = selectedCount == 0,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text
        };
        Controls.Add(allScope);

        var ok = new Button
        {
            Location = new Point(274, 410),
            Size = new Size(76, 30),
            Text = "Apply",
            DialogResult = DialogResult.OK
        };
        Theme.StyleButton(ok, primary: true);
        Controls.Add(ok);

        var cancel = new Button
        {
            Location = new Point(360, 410),
            Size = new Size(76, 30),
            Text = "Cancel",
            DialogResult = DialogResult.Cancel
        };
        Theme.StyleButton(cancel);
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;

        UpdateStopControls();
    }

    /// <summary>Raised when the preview should be refreshed.</summary>
    public event EventHandler? PreviewChanged;

    public Gradient Gradient => bar.Gradient;

    public bool Reverse => reverseBox.Checked;

    public int AmountPercent => amountSlider.Value;

    public float Amount => amountSlider.Value / 100f;

    public bool ApplyToAllFrames => allScope.Checked;

    public bool PreviewEnabled => previewBox.Checked && amountSlider.Value > 0;

    private void ApplyPreset()
    {
        if (presetBox.SelectedIndex <= 0) return;

        bar.Gradient = Gradient.Presets[presetBox.SelectedIndex - 1].Create();
        bar.SelectedIndex = 0;
        RaisePreview();
    }

    private void MarkCustom()
    {
        if (presetBox.SelectedIndex == 0) return;

        // Editing a preset turns it into a custom ramp, without wiping the stops.
        presetBox.SelectedIndexChanged -= PresetChanged;
        presetBox.SelectedIndex = 0;
        presetBox.SelectedIndexChanged += PresetChanged;
    }

    private void PresetChanged(object? sender, EventArgs e) => ApplyPreset();

    private void FlipStops()
    {
        var flipped = new Gradient(bar.Gradient.Stops
            .Select(stop => stop with { Position = 1f - stop.Position })
            .OrderBy(stop => stop.Position));

        bar.Gradient = flipped;
        MarkCustom();
        RaisePreview();
    }

    private void UpdateStopControls()
    {
        if (bar.SelectedStop is not { } stop) return;

        syncingStop = true;
        stopColorButton.BackColor = Color.FromArgb(stop.Color.R, stop.Color.G, stop.Color.B);
        stopColorButton.ForeColor = ContrastColor(stopColorButton.BackColor);
        stopColorButton.Text = $"#{stop.Color.R:x2}{stop.Color.G:x2}{stop.Color.B:x2}";
        stopPositionInput.Value = (decimal)Math.Clamp(MathF.Round(stop.Position * 100), 0, 100);
        syncingStop = false;
    }

    private void MoveSelectedStop()
    {
        if (syncingStop) return;

        bar.SelectedIndex = bar.Gradient.Move(bar.SelectedIndex, (float)stopPositionInput.Value / 100f);
        bar.Commit();
    }

    private static Color ContrastColor(Color color)
    {
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        return luminance > 0.55 ? Color.FromArgb(20, 20, 20) : Color.White;
    }

    private void RaisePreview() => PreviewChanged?.Invoke(this, EventArgs.Empty);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.ScaleForm(this);   // laid out at scale 1, grown to the user's interface scale
    }
}
