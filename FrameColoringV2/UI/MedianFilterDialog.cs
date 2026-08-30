using FrameColoringV2.Imaging;

namespace FrameColoringV2.UI;

/// <summary>Radius and scope for the median filter.</summary>
public sealed class MedianFilterDialog : Form
{
    private readonly NumericUpDown radiusInput;
    private readonly TrackBar radiusSlider;
    private readonly CheckBox ignoreTransparentBox;
    private readonly RadioButton selectedScope;
    private readonly RadioButton allScope;

    private bool syncingRadius;

    public MedianFilterDialog(int radius, bool ignoreTransparent, int selectedCount, int totalCount)
    {
        radius = Math.Clamp(radius, MedianFilter.MinRadius, MedianFilter.MaxRadius);

        Text = "Median";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 278);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;

        Controls.Add(new Label
        {
            Location = new Point(16, 14),
            Size = new Size(388, 44),
            ForeColor = Theme.TextDim,
            Text = "Replaces every pixel with the median of its neighbours inside a disc, " +
                   "the same as Photoshop's Noise ▸ Median."
        });

        Controls.Add(new Label
        {
            Location = new Point(16, 70),
            Size = new Size(60, 24),
            Text = "Radius",
            TextAlign = ContentAlignment.MiddleLeft
        });

        radiusInput = new NumericUpDown
        {
            Location = new Point(80, 68),
            Size = new Size(60, 24),
            Minimum = MedianFilter.MinRadius,
            Maximum = MedianFilter.MaxRadius,
            Value = radius,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        radiusInput.ValueChanged += (_, _) => SyncRadius(fromSlider: false);
        Controls.Add(radiusInput);

        Controls.Add(new Label
        {
            Location = new Point(146, 70),
            Size = new Size(50, 24),
            Text = "pixels",
            ForeColor = Theme.TextDim,
            TextAlign = ContentAlignment.MiddleLeft
        });

        radiusSlider = new TrackBar
        {
            Location = new Point(196, 64),
            Size = new Size(208, 34),
            Minimum = MedianFilter.MinRadius,
            Maximum = MedianFilter.MaxRadius,
            Value = radius,
            TickStyle = TickStyle.None
        };
        radiusSlider.ValueChanged += (_, _) => SyncRadius(fromSlider: true);
        Controls.Add(radiusSlider);

        ignoreTransparentBox = new CheckBox
        {
            Location = new Point(16, 106),
            Size = new Size(388, 24),
            Text = "Keep transparent pixels out of the colour median",
            Checked = ignoreTransparent,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text
        };
        Controls.Add(ignoreTransparentBox);

        Controls.Add(new Label
        {
            Location = new Point(16, 140),
            Size = new Size(388, 20),
            Text = "APPLY TO",
            ForeColor = Theme.TextDim,
            Font = Theme.UiFontBold
        });

        selectedScope = new RadioButton
        {
            Location = new Point(16, 162),
            Size = new Size(388, 24),
            Text = selectedCount == 1 ? "The selected frame" : $"The {selectedCount} selected frames",
            Checked = selectedCount > 0,
            Enabled = selectedCount > 0,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text
        };
        Controls.Add(selectedScope);

        allScope = new RadioButton
        {
            Location = new Point(16, 188),
            Size = new Size(388, 24),
            Text = $"All {totalCount} open frames",
            Checked = selectedCount == 0,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text
        };
        Controls.Add(allScope);

        var ok = new Button
        {
            Location = new Point(242, 232),
            Size = new Size(76, 30),
            Text = "Apply",
            DialogResult = DialogResult.OK
        };
        Theme.StyleButton(ok, primary: true);
        Controls.Add(ok);

        var cancel = new Button
        {
            Location = new Point(328, 232),
            Size = new Size(76, 30),
            Text = "Cancel",
            DialogResult = DialogResult.Cancel
        };
        Theme.StyleButton(cancel);
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    public int Radius => (int)radiusInput.Value;

    public bool IgnoreTransparent => ignoreTransparentBox.Checked;

    public bool ApplyToAllFrames => allScope.Checked;

    private void SyncRadius(bool fromSlider)
    {
        if (syncingRadius) return;

        syncingRadius = true;
        if (fromSlider) radiusInput.Value = radiusSlider.Value;
        else radiusSlider.Value = (int)radiusInput.Value;
        syncingRadius = false;
    }
}
