using System.Drawing.Drawing2D;

namespace FrameColoringV2.UI;

/// <summary>Flat dark palette shared by every control in the app.</summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(30, 32, 36);
    public static readonly Color Surface = Color.FromArgb(38, 41, 46);
    public static readonly Color SurfaceAlt = Color.FromArgb(46, 50, 56);
    public static readonly Color Border = Color.FromArgb(60, 65, 72);
    public static readonly Color Text = Color.FromArgb(228, 231, 235);
    public static readonly Color TextDim = Color.FromArgb(150, 156, 165);
    public static readonly Color Accent = Color.FromArgb(0, 150, 199);
    public static readonly Color AccentHover = Color.FromArgb(0, 172, 227);
    public static readonly Color Danger = Color.FromArgb(200, 80, 80);
    public static readonly Color CanvasBack = Color.FromArgb(24, 26, 29);

    public static readonly Font UiFont = new("Segoe UI", 9f);
    public static readonly Font UiFontBold = new("Segoe UI", 9f, FontStyle.Bold);

    public static void ApplyTo(Control root)
    {
        root.BackColor = Background;
        root.ForeColor = Text;
        root.Font = UiFont;
        ApplyRecursive(root);
    }

    private static void ApplyRecursive(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            switch (control)
            {
                case Button button:
                    StyleButton(button);
                    break;
                case ListView listView:
                    listView.BackColor = Surface;
                    listView.ForeColor = Text;
                    listView.BorderStyle = BorderStyle.None;
                    break;
                case TextBox textBox:
                    textBox.BackColor = Surface;
                    textBox.ForeColor = Text;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case NumericUpDown numeric:
                    numeric.BackColor = Surface;
                    numeric.ForeColor = Text;
                    numeric.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ComboBox combo:
                    combo.BackColor = Surface;
                    combo.ForeColor = Text;
                    combo.FlatStyle = FlatStyle.Flat;
                    break;
                case GroupBox group:
                    group.ForeColor = TextDim;
                    group.BackColor = Color.Transparent;
                    break;
                case Panel panel:
                    if (panel.BackColor == SystemColors.Control) panel.BackColor = Background;
                    break;
                case Label label:
                    label.BackColor = Color.Transparent;
                    if (label.ForeColor == SystemColors.ControlText) label.ForeColor = Text;
                    break;
                case TrackBar trackBar:
                    trackBar.BackColor = Background;
                    break;
                case CheckBox checkBox:
                    checkBox.ForeColor = Text;
                    checkBox.BackColor = Color.Transparent;
                    checkBox.FlatStyle = FlatStyle.Flat;
                    break;
            }

            if (control.HasChildren) ApplyRecursive(control);
        }
    }

    public static void StyleButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : SurfaceAlt;
        button.BackColor = primary ? Accent : Surface;
        button.ForeColor = Text;
        button.UseVisualStyleBackColor = false;
        button.Font = UiFont;
        button.Cursor = Cursors.Hand;
    }

    /// <summary>Draws a rounded panel background, used for the sidebar cards.</summary>
    public static void FillRounded(Graphics g, Rectangle bounds, Color color, int radius = 6)
    {
        using var path = RoundedPath(bounds, radius);
        using var brush = new SolidBrush(color);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(brush, path);
    }

    public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d - 1, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d - 1, bounds.Bottom - d - 1, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d - 1, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>Dark renderer for the menu and tool strips.</summary>
public sealed class DarkStripRenderer : ToolStripProfessionalRenderer
{
    public DarkStripRenderer() : base(new DarkColorTable()) { }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextDim;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Text;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border);
        var bounds = e.Item.ContentRectangle;
        if (e.Vertical)
        {
            int x = bounds.Left + bounds.Width / 2;
            e.Graphics.DrawLine(pen, x, bounds.Top + 4, x, bounds.Bottom - 4);
        }
        else
        {
            int y = bounds.Top + bounds.Height / 2;
            e.Graphics.DrawLine(pen, bounds.Left, y, bounds.Right, y);
        }
    }
}

public sealed class DarkColorTable : ProfessionalColorTable
{
    public override Color MenuStripGradientBegin => Theme.Surface;
    public override Color MenuStripGradientEnd => Theme.Surface;
    public override Color MenuItemSelected => Theme.SurfaceAlt;
    public override Color MenuItemSelectedGradientBegin => Theme.SurfaceAlt;
    public override Color MenuItemSelectedGradientEnd => Theme.SurfaceAlt;
    public override Color MenuItemPressedGradientBegin => Theme.Surface;
    public override Color MenuItemPressedGradientEnd => Theme.Surface;
    public override Color MenuItemBorder => Theme.Accent;
    public override Color MenuBorder => Theme.Border;
    public override Color ToolStripDropDownBackground => Theme.Surface;
    public override Color ImageMarginGradientBegin => Theme.Surface;
    public override Color ImageMarginGradientMiddle => Theme.Surface;
    public override Color ImageMarginGradientEnd => Theme.Surface;
    public override Color ToolStripGradientBegin => Theme.Surface;
    public override Color ToolStripGradientMiddle => Theme.Surface;
    public override Color ToolStripGradientEnd => Theme.Surface;
    public override Color ButtonSelectedHighlight => Theme.SurfaceAlt;
    public override Color ButtonSelectedGradientBegin => Theme.SurfaceAlt;
    public override Color ButtonSelectedGradientEnd => Theme.SurfaceAlt;
    public override Color ButtonPressedGradientBegin => Theme.Accent;
    public override Color ButtonPressedGradientEnd => Theme.Accent;
    public override Color ButtonCheckedGradientBegin => Theme.Accent;
    public override Color ButtonCheckedGradientEnd => Theme.Accent;
    public override Color CheckBackground => Theme.Accent;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Border;
    public override Color StatusStripGradientBegin => Theme.Surface;
    public override Color StatusStripGradientEnd => Theme.Surface;
}
