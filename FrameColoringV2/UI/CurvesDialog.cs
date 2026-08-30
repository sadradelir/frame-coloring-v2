using FrameColoringV2.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace FrameColoringV2.UI;

/// <summary>
/// Photoshop style Curves: pick a channel, bend the curve, watch the frame update behind the
/// dialog, then apply it to the selection or to every open frame.
/// </summary>
public sealed class CurvesDialog : Form
{
    private const string HintText = "Drag the line · click to add · right click to remove";

    private readonly CurveEditor editor;
    private readonly ComboBox channelBox;
    private readonly CheckBox previewBox;
    private readonly Label readoutLabel;
    private readonly RadioButton selectedScope;
    private readonly RadioButton allScope;
    private readonly int[][]? histograms;

    public CurvesDialog(Image<Rgba32>? histogramSource, int selectedCount, int totalCount)
    {
        Curves = new CurveSet();
        histograms = histogramSource == null ? null : CurveSet.BuildHistograms(histogramSource);

        Text = "Curves";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(438, 632);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;

        Controls.Add(new Label
        {
            Location = new Point(16, 16),
            Size = new Size(62, 24),
            Text = "Channel",
            TextAlign = ContentAlignment.MiddleLeft
        });

        channelBox = new ComboBox
        {
            Location = new Point(84, 14),
            Size = new Size(148, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat
        };
        channelBox.Items.AddRange(new object[] { "RGB", "Red", "Green", "Blue", "Alpha" });
        channelBox.SelectedIndex = 0;
        channelBox.SelectedIndexChanged += (_, _) => ShowChannel();
        Controls.Add(channelBox);

        var resetChannel = new Button
        {
            Location = new Point(244, 13),
            Size = new Size(86, 26),
            Text = "Reset"
        };
        resetChannel.Click += (_, _) =>
        {
            Curves[SelectedChannel].Reset();
            editor.RebuildLut();
            RaisePreview();
        };
        Theme.StyleButton(resetChannel);
        Controls.Add(resetChannel);

        var resetAll = new Button
        {
            Location = new Point(336, 13),
            Size = new Size(86, 26),
            Text = "Reset all"
        };
        resetAll.Click += (_, _) =>
        {
            Curves.Reset();
            editor.RebuildLut();
            RaisePreview();
        };
        Theme.StyleButton(resetAll);
        Controls.Add(resetAll);

        editor = new CurveEditor
        {
            Location = new Point(16, 50),
            Size = new Size(406, 406),
            Curve = Curves.Composite
        };
        editor.CurveChanged += (_, _) => UpdateReadout();
        editor.CurveCommitted += (_, _) => RaisePreview();
        Controls.Add(editor);

        readoutLabel = new Label
        {
            Location = new Point(16, 462),
            Size = new Size(406, 20),
            ForeColor = Theme.TextDim,
            Text = HintText
        };
        Controls.Add(readoutLabel);

        previewBox = new CheckBox
        {
            Location = new Point(16, 488),
            Size = new Size(406, 24),
            Text = "Preview on the canvas",
            Checked = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text
        };
        previewBox.CheckedChanged += (_, _) => RaisePreview();
        Controls.Add(previewBox);

        Controls.Add(new Label
        {
            Location = new Point(16, 518),
            Size = new Size(406, 20),
            Text = "APPLY TO",
            ForeColor = Theme.TextDim,
            Font = Theme.UiFontBold
        });

        selectedScope = new RadioButton
        {
            Location = new Point(16, 540),
            Size = new Size(406, 24),
            Text = selectedCount == 1 ? "The selected frame" : $"The {selectedCount} selected frames",
            Checked = selectedCount > 0,
            Enabled = selectedCount > 0,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text
        };
        Controls.Add(selectedScope);

        allScope = new RadioButton
        {
            Location = new Point(16, 566),
            Size = new Size(406, 24),
            Text = $"All {totalCount} open frames",
            Checked = selectedCount == 0,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.Text
        };
        Controls.Add(allScope);

        var ok = new Button
        {
            Location = new Point(260, 596),
            Size = new Size(76, 30),
            Text = "Apply",
            DialogResult = DialogResult.OK
        };
        Theme.StyleButton(ok, primary: true);
        Controls.Add(ok);

        var cancel = new Button
        {
            Location = new Point(346, 596),
            Size = new Size(76, 30),
            Text = "Cancel",
            DialogResult = DialogResult.Cancel
        };
        Theme.StyleButton(cancel);
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;

        ShowChannel();
    }

    /// <summary>Raised when the preview should be refreshed; read <see cref="PreviewCurves"/>.</summary>
    public event EventHandler? PreviewChanged;

    public CurveSet Curves { get; }

    public bool ApplyToAllFrames => allScope.Checked;

    /// <summary>The curves to show on the canvas right now, or null while the preview is off.</summary>
    public CurveSet? PreviewCurves => previewBox.Checked && !Curves.IsIdentity ? Curves : null;

    private CurveChannel SelectedChannel => (CurveChannel)channelBox.SelectedIndex;

    private void ShowChannel()
    {
        var channel = SelectedChannel;

        editor.Curve = Curves[channel];
        editor.CurveColor = channel switch
        {
            CurveChannel.Red => Color.FromArgb(230, 90, 90),
            CurveChannel.Green => Color.FromArgb(90, 210, 110),
            CurveChannel.Blue => Color.FromArgb(100, 150, 240),
            CurveChannel.Alpha => Color.FromArgb(200, 200, 200),
            _ => Theme.Accent
        };
        editor.SetHistogram(histograms?[(int)channel]);

        UpdateReadout();
    }

    private void UpdateReadout()
    {
        readoutLabel.Text = editor.HoverInput is { } input
            ? $"Input {input}   →   Output {editor.Output(input)}"
            : HintText;
    }

    private void RaisePreview() => PreviewChanged?.Invoke(this, EventArgs.Empty);
}
