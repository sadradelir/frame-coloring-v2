using System.Drawing.Drawing2D;
using FrameColoringV2.Imaging;

namespace FrameColoringV2.UI;

/// <summary>
/// The curve grid of the Curves dialog: drag a point to bend the curve, click an empty spot to
/// add one, right click a point to remove it. Input runs left to right, output bottom to top,
/// so lifting the left end brightens the blacks.
/// </summary>
public sealed class CurveEditor : Control
{
    private const int Padding = 10;
    private const float HitRadius = 10f;

    private ToneCurve curve = new();
    private int[]? histogram;
    private byte[] lut;
    private int draggedPoint = -1;
    private Color curveColor = Theme.Accent;

    public CurveEditor()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Surface;
        Cursor = Cursors.Cross;
        lut = curve.BuildLut();
    }

    /// <summary>Raised while the curve is being edited, for the read outs.</summary>
    public event EventHandler? CurveChanged;

    /// <summary>Raised when an edit finishes, which is when a preview is worth recomputing.</summary>
    public event EventHandler? CurveCommitted;

    public ToneCurve Curve
    {
        get => curve;
        set
        {
            curve = value;
            RebuildLut();
        }
    }

    public Color CurveColor
    {
        get => curveColor;
        set
        {
            curveColor = value;
            Invalidate();
        }
    }

    public void SetHistogram(int[]? values)
    {
        histogram = values;
        Invalidate();
    }

    public void RebuildLut()
    {
        lut = curve.BuildLut();
        CurveChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    /// <summary>Input value under the cursor, or null when the cursor is outside the grid.</summary>
    public int? HoverInput { get; private set; }

    public byte Output(int input) => lut[Math.Clamp(input, 0, 255)];

    // ------------------------------------------------------------------ input

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        var value = ToCurveSpace(e.Location);

        if (e.Button == MouseButtons.Right)
        {
            int index = curve.IndexNear(value.X, value.Y, ToCurveDistance(HitRadius));
            if (index > 0 && index < curve.Points.Count - 1)
            {
                curve.RemoveAt(index);
                RebuildLut();
                CurveCommitted?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (e.Button != MouseButtons.Left) return;

        draggedPoint = curve.IndexNear(value.X, value.Y, ToCurveDistance(HitRadius));
        if (draggedPoint < 0) draggedPoint = curve.Add(value.X, value.Y);

        curve.Move(draggedPoint, value.X, value.Y);
        RebuildLut();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var value = ToCurveSpace(e.Location);
        HoverInput = value.X is >= 0 and <= 255 ? (int)MathF.Round(value.X) : null;

        if (draggedPoint < 0)
        {
            Cursor = curve.IndexNear(value.X, value.Y, ToCurveDistance(HitRadius)) >= 0 ? Cursors.SizeAll : Cursors.Cross;
            CurveChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        curve.Move(draggedPoint, value.X, value.Y);
        RebuildLut();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (draggedPoint < 0) return;

        draggedPoint = -1;
        CurveCommitted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        HoverInput = null;
        CurveChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        // A double click on empty space would otherwise leave a stray point behind.
        Invalidate();
    }

    // ------------------------------------------------------------- coordinates

    private Rectangle Grid => new(
        Padding, Padding,
        Math.Max(1, ClientSize.Width - Padding * 2 - 1),
        Math.Max(1, ClientSize.Height - Padding * 2 - 1));

    private PointF ToCurveSpace(Point client)
    {
        var grid = Grid;
        return new PointF(
            (client.X - grid.X) * 255f / grid.Width,
            255f - (client.Y - grid.Y) * 255f / grid.Height);
    }

    private PointF ToClientSpace(float x, float y)
    {
        var grid = Grid;
        return new PointF(grid.X + x * grid.Width / 255f, grid.Y + (255f - y) * grid.Height / 255f);
    }

    private float ToCurveDistance(float clientDistance) => clientDistance * 255f / Grid.Width;

    // ---------------------------------------------------------------- painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var background = new SolidBrush(Theme.Surface))
        {
            g.FillRectangle(background, ClientRectangle);
        }

        var grid = Grid;

        DrawHistogram(g, grid);
        DrawGrid(g, grid);
        DrawCurve(g, grid);
        DrawPoints(g);

        using var border = new Pen(Theme.Border);
        g.DrawRectangle(border, grid);
    }

    private void DrawHistogram(Graphics g, Rectangle grid)
    {
        if (histogram == null) return;

        int max = histogram.Max();
        if (max <= 0) return;

        using var brush = new SolidBrush(Color.FromArgb(70, 150, 160, 175));

        for (int value = 0; value < 256; value++)
        {
            if (histogram[value] == 0) continue;

            // Square root scaling, otherwise one huge peak flattens everything else.
            float height = MathF.Sqrt(histogram[value] / (float)max) * grid.Height;
            float x = grid.X + value * grid.Width / 255f;
            float width = Math.Max(1f, grid.Width / 255f);

            g.FillRectangle(brush, x, grid.Bottom - height, width, height);
        }
    }

    private void DrawGrid(Graphics g, Rectangle grid)
    {
        using var gridPen = new Pen(Color.FromArgb(60, 255, 255, 255));
        using var diagonalPen = new Pen(Color.FromArgb(90, 255, 255, 255)) { DashStyle = DashStyle.Dash };

        for (int i = 1; i < 4; i++)
        {
            int x = grid.X + grid.Width * i / 4;
            int y = grid.Y + grid.Height * i / 4;
            g.DrawLine(gridPen, x, grid.Y, x, grid.Bottom);
            g.DrawLine(gridPen, grid.X, y, grid.Right, y);
        }

        g.DrawLine(diagonalPen, grid.X, grid.Bottom, grid.Right, grid.Y);
    }

    private void DrawCurve(Graphics g, Rectangle grid)
    {
        var line = new PointF[256];
        for (int value = 0; value < 256; value++) line[value] = ToClientSpace(value, lut[value]);

        using var pen = new Pen(curveColor, 2f);
        g.DrawLines(pen, line);

        if (HoverInput is not { } input) return;

        using var hoverPen = new Pen(Color.FromArgb(110, 255, 255, 255));
        var hover = ToClientSpace(input, lut[input]);
        g.DrawLine(hoverPen, hover.X, grid.Y, hover.X, grid.Bottom);
        g.DrawLine(hoverPen, grid.X, hover.Y, grid.Right, hover.Y);
    }

    private void DrawPoints(Graphics g)
    {
        using var fill = new SolidBrush(Theme.Text);
        using var outline = new Pen(Color.FromArgb(30, 32, 36), 1.5f);

        foreach (var point in curve.Points)
        {
            var center = ToClientSpace(point.X, point.Y);
            var box = new RectangleF(center.X - 4, center.Y - 4, 8, 8);
            g.FillRectangle(fill, box);
            g.DrawRectangle(outline, box.X, box.Y, box.Width, box.Height);
        }
    }
}
