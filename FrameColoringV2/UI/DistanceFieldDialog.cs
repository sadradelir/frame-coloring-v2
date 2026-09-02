using FrameColoringV2.Imaging;

namespace FrameColoringV2.UI;

/// <summary>Options for baking the alpha channel into a signed distance field.</summary>
public sealed class DistanceFieldDialog : Form
{
    private readonly NumericUpDown spreadInput;
    private readonly NumericUpDown thresholdInput;
    private readonly ComboBox supersampleBox;
    private readonly CheckBox subPixelBox;
    private readonly CheckBox verifyBox;
    private readonly RadioButton selectedScope;
    private readonly RadioButton allScope;

    public DistanceFieldDialog(float spread, byte solidThreshold, int supersample, bool subPixelEdge,
        bool verify, int selectedCount, int totalCount)
    {
        Text = "Alpha to Distance Field";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(468, 394);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;

        Controls.Add(new Label
        {
            Location = new Point(16, 12),
            Size = new Size(436, 56),
            ForeColor = Theme.TextDim,
            Text = "Rewrites alpha as a signed distance field: 0.5 on the silhouette, 0 at one spread " +
                   "outside, 1 at one spread inside, linear in between. RGB is left exactly as it is."
        });

        Controls.Add(new Label
        {
            Location = new Point(16, 76),
            Size = new Size(120, 26),
            Text = "Spread",
            TextAlign = ContentAlignment.MiddleLeft
        });

        spreadInput = Number(new Point(142, 75), 1, 128, (decimal)spread);
        Controls.Add(spreadInput);

        Controls.Add(new Label
        {
            Location = new Point(210, 76),
            Size = new Size(242, 26),
            Text = "pixels each way",
            ForeColor = Theme.TextDim,
            TextAlign = ContentAlignment.MiddleLeft
        });

        Controls.Add(new Label
        {
            Location = new Point(16, 110),
            Size = new Size(120, 26),
            Text = "Solid threshold",
            TextAlign = ContentAlignment.MiddleLeft
        });

        thresholdInput = Number(new Point(142, 109), 1, 255, solidThreshold);
        Controls.Add(thresholdInput);

        Controls.Add(new Label
        {
            Location = new Point(210, 110),
            Size = new Size(242, 26),
            Text = "alpha ≥ this is inside",
            ForeColor = Theme.TextDim,
            TextAlign = ContentAlignment.MiddleLeft
        });

        Controls.Add(new Label
        {
            Location = new Point(16, 144),
            Size = new Size(120, 26),
            Text = "Supersample",
            TextAlign = ContentAlignment.MiddleLeft
        });

        supersampleBox = new ComboBox
        {
            Location = new Point(142, 143),
            Size = new Size(62, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat
        };
        supersampleBox.Items.AddRange(new object[] { "1×", "2×", "4×" });
        supersampleBox.SelectedIndex = supersample switch { 1 => 0, 2 => 1, _ => 2 };
        Controls.Add(supersampleBox);

        Controls.Add(new Label
        {
            Location = new Point(210, 144),
            Size = new Size(242, 26),
            Text = "only used without the fit",
            ForeColor = Theme.TextDim,
            TextAlign = ContentAlignment.MiddleLeft
        });

        subPixelBox = new CheckBox
        {
            Location = new Point(16, 178),
            Size = new Size(436, 24),
            Text = "Sub-pixel edge fit (2-3× more accurate, recommended)",
            Checked = subPixelEdge,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text
        };
        subPixelBox.CheckedChanged += (_, _) => supersampleBox.Enabled = !subPixelBox.Checked;
        supersampleBox.Enabled = !subPixelEdge;
        Controls.Add(subPixelBox);

        verifyBox = new CheckBox
        {
            Location = new Point(16, 208),
            Size = new Size(436, 24),
            Text = "Print a verification report for the first frame",
            Checked = verify,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text
        };
        Controls.Add(verifyBox);

        Controls.Add(new Label
        {
            Location = new Point(16, 238),
            Size = new Size(436, 20),
            Text = "APPLY TO",
            ForeColor = Theme.TextDim,
            Font = Theme.UiFontBold
        });

        selectedScope = new RadioButton
        {
            Location = new Point(16, 260),
            Size = new Size(436, 24),
            Text = selectedCount == 1 ? "The selected frame" : $"The {selectedCount} selected frames",
            Checked = selectedCount > 0,
            Enabled = selectedCount > 0,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text
        };
        Controls.Add(selectedScope);

        allScope = new RadioButton
        {
            Location = new Point(16, 286),
            Size = new Size(436, 24),
            Text = $"All {totalCount} open frames",
            Checked = selectedCount == 0,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text
        };
        Controls.Add(allScope);

        Controls.Add(new Label
        {
            Location = new Point(16, 316),
            Size = new Size(436, 20),
            Text = "Baking twice measures the field, not the sprite. Ctrl+Z undoes it.",
            ForeColor = Theme.TextDim
        });

        var ok = new Button
        {
            Location = new Point(290, 346),
            Size = new Size(76, 30),
            Text = "Bake",
            DialogResult = DialogResult.OK
        };
        Theme.StyleButton(ok, primary: true);
        Controls.Add(ok);

        var cancel = new Button
        {
            Location = new Point(376, 346),
            Size = new Size(76, 30),
            Text = "Cancel",
            DialogResult = DialogResult.Cancel
        };
        Theme.StyleButton(cancel);
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    public float Spread => (float)spreadInput.Value;

    public byte SolidThreshold => (byte)thresholdInput.Value;

    public int Supersample => supersampleBox.SelectedIndex switch { 0 => 1, 1 => 2, _ => 4 };

    public bool SubPixelEdge => subPixelBox.Checked;

    public bool Verify => verifyBox.Checked;

    public bool ApplyToAllFrames => allScope.Checked;

    private static NumericUpDown Number(Point location, decimal minimum, decimal maximum, decimal value) => new()
    {
        Location = location,
        Size = new Size(62, 26),
        Minimum = minimum,
        Maximum = maximum,
        Value = Math.Clamp(value, minimum, maximum),
        BackColor = Theme.SurfaceAlt,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle
    };
}
