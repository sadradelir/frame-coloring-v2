using System.Drawing.Drawing2D;
using FrameColoringV2.Imaging;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;

namespace FrameColoringV2.UI;

/// <summary>
/// The gradient strip of the Gradient Map dialog: drag the handles to move colour stops,
/// click the empty strip to add one, double click a handle to recolour it, right click to
/// remove it.
/// </summary>
public sealed class GradientBar : Control
{
    private const int SidePadding = 10;
    private const int HandleHeight = 16;

    private Gradient gradient = new();
    private int selectedIndex;
    private bool dragging;
    private bool reverse;

    public GradientBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Background;
        Cursor = Cursors.Hand;
        Height = 74;
    }

    /// <summary>Raised while the gradient is being edited.</summary>
    public event EventHandler? GradientChanged;

    /// <summary>Raised when an edit finishes, which is when a preview is worth recomputing.</summary>
    public event EventHandler? GradientCommitted;

    public event EventHandler? SelectionChanged;

    public Gradient Gradient
    {
        get => gradient;
        set
        {
            gradient = value;
            selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, gradient.Stops.Count - 1));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            GradientChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    /// <summary>Draws the ramp the way it will be applied, dark end on the left.</summary>
    public bool Reverse
    {
        get => reverse;
        set
        {
            reverse = value;
            Invalidate();
        }
    }

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            selectedIndex = Math.Clamp(value, 0, Math.Max(0, gradient.Stops.Count - 1));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public GradientStop? SelectedStop =>
        selectedIndex >= 0 && selectedIndex < gradient.Stops.Count ? gradient.Stops[selectedIndex] : null;

    public void NotifyChanged()
    {
        GradientChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void Commit()
    {
        NotifyChanged();
        GradientCommitted?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ input

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        float position = ToPosition(e.X);
        int hit = gradient.IndexNear(position, ToPositionDistance(9));

        if (e.Button == MouseButtons.Right)
        {
            if (hit >= 0 && gradient.Stops.Count > 2)
            {
                gradient.RemoveAt(hit);
                SelectedIndex = Math.Min(selectedIndex, gradient.Stops.Count - 1);
                Commit();
            }

            return;
        }

        if (e.Button != MouseButtons.Left) return;

        if (hit < 0)
        {
            // A new stop takes the colour the ramp already has there.
            hit = gradient.Add(position, gradient.Sample(position));
            SelectedIndex = hit;
            Commit();
        }
        else
        {
            SelectedIndex = hit;
        }

        dragging = true;
        NotifyChanged();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;

        SelectedIndex = gradient.Move(selectedIndex, ToPosition(e.X));
        NotifyChanged();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging) return;

        dragging = false;
        Commit();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        int hit = gradient.IndexNear(ToPosition(e.X), ToPositionDistance(9));
        if (hit < 0) return;

        SelectedIndex = hit;
        EditSelectedColor();
    }

    /// <summary>Opens the colour picker for the selected stop.</summary>
    public void EditSelectedColor()
    {
        if (SelectedStop is not { } stop) return;

        using var dialog = new ColorDialog
        {
            FullOpen = true,
            Color = Color.FromArgb(stop.Color.R, stop.Color.G, stop.Color.B)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        gradient.SetColor(selectedIndex, new SixLabors.ImageSharp.PixelFormats.Rgba32(
            dialog.Color.R, dialog.Color.G, dialog.Color.B, 255));
        Commit();
    }

    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Delete || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode != Keys.Delete || gradient.Stops.Count <= 2) return;

        gradient.RemoveAt(selectedIndex);
        SelectedIndex = Math.Min(selectedIndex, gradient.Stops.Count - 1);
        Commit();
    }

    // ------------------------------------------------------------- coordinates

    private Rectangle Ramp => new(
        SidePadding, 4,
        Math.Max(1, ClientSize.Width - SidePadding * 2),
        Math.Max(1, ClientSize.Height - HandleHeight - 12));

    private float ToPosition(int x)
    {
        var ramp = Ramp;
        return Math.Clamp((x - ramp.X) / (float)ramp.Width, 0f, 1f);
    }

    private float ToPositionDistance(int pixels) => pixels / (float)Ramp.Width;

    private int ToX(float position) => Ramp.X + (int)MathF.Round(position * Ramp.Width);

    // ---------------------------------------------------------------- painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var background = new SolidBrush(BackColor))
        {
            g.FillRectangle(background, ClientRectangle);
        }

        var ramp = Ramp;

        // The ramp itself, sampled per pixel column so multi stop gradients stay accurate.
        for (int x = 0; x < ramp.Width; x++)
        {
            float position = x / (float)Math.Max(1, ramp.Width - 1);
            var color = gradient.Sample(reverse ? 1f - position : position);

            using var pen = new Pen(Color.FromArgb(255, color.R, color.G, color.B));
            g.DrawLine(pen, ramp.X + x, ramp.Y, ramp.X + x, ramp.Bottom);
        }

        using (var border = new Pen(Theme.Border))
        {
            g.DrawRectangle(border, ramp);
        }

        DrawHandles(g, ramp);
    }

    private void DrawHandles(Graphics g, Rectangle ramp)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        for (int i = 0; i < gradient.Stops.Count; i++)
        {
            var stop = gradient.Stops[i];
            int x = ToX(stop.Position);
            int top = ramp.Bottom + 4;

            var handle = new Point[]
            {
                new(x, top),
                new(x - 6, top + 7),
                new(x - 6, top + HandleHeight - 2),
                new(x + 6, top + HandleHeight - 2),
                new(x + 6, top + 7)
            };

            using var fill = new SolidBrush(Color.FromArgb(255, stop.Color.R, stop.Color.G, stop.Color.B));
            using var outline = new Pen(i == selectedIndex ? Theme.Accent : Theme.Border, i == selectedIndex ? 2f : 1f);

            g.FillPolygon(fill, handle);
            g.DrawPolygon(outline, handle);
        }
    }
}
