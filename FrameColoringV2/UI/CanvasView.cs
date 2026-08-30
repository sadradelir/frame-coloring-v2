using System.Drawing.Drawing2D;

namespace FrameColoringV2.UI;

public sealed class PixelMouseEventArgs : EventArgs
{
    public PixelMouseEventArgs(Point pixel, MouseButtons button, bool insideImage)
    {
        Pixel = pixel;
        Button = button;
        InsideImage = insideImage;
    }

    public Point Pixel { get; }
    public MouseButtons Button { get; }
    public bool InsideImage { get; }
}

/// <summary>
/// Zoomable / pannable canvas. It owns nothing but the view state: the composited frame
/// is handed to it as a bitmap and it reports mouse positions back in image pixel coordinates.
/// </summary>
public sealed class CanvasView : Control
{
    private Bitmap? surface;
    private double zoom = 1.0;
    private PointF origin;           // top left of the image, in client coordinates
    private bool panning;
    private Point panStart;
    private PointF panOriginStart;
    private bool spaceHeld;
    private bool viewAdjustedByUser;   // stop auto fitting once the user zooms or pans
    private TextureBrush? checkerBrush;

    public CanvasView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        BackColor = Theme.CanvasBack;
        TabStop = true;
        Cursor = Cursors.Cross;
    }

    public event EventHandler<PixelMouseEventArgs>? PixelMouseDown;
    public event EventHandler<PixelMouseEventArgs>? PixelMouseDrag;
    public event EventHandler<PixelMouseEventArgs>? PixelMouseMove;
    public event EventHandler? PixelMouseUp;
    public event EventHandler? ViewChanged;

    /// <summary>Raised on wheel with a modifier held, so the host can step through frames.</summary>
    public event EventHandler<int>? FrameStepRequested;

    public double Zoom
    {
        get => zoom;
        set => SetZoom(value, new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f));
    }

    public double MinZoom { get; set; } = 0.05;
    public double MaxZoom { get; set; } = 32.0;

    /// <summary>Crop rectangle drawn on top of the frame, in image coordinates.</summary>
    public Rectangle? CropOverlay { get; set; }

    public bool ShowPixelGrid { get; set; } = true;

    public Size ImageSize => surface?.Size ?? Size.Empty;

    public void SetSurface(Bitmap? bitmap, bool resetView)
    {
        surface = bitmap;
        if (resetView) FitToWindow();
        else Invalidate();
    }

    public void FitToWindow()
    {
        if (surface == null || ClientSize.Width < 4 || ClientSize.Height < 4) return;

        double scale = Math.Min((double)ClientSize.Width / surface.Width, (double)ClientSize.Height / surface.Height);
        zoom = Math.Clamp(scale * 0.95, MinZoom, MaxZoom);
        viewAdjustedByUser = false;
        CenterImage();
        ViewChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    /// <summary>Zoom requested by the user, which turns off automatic fitting.</summary>
    public void ZoomTo(double newZoom) => SetZoom(newZoom, new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f));

    public void CenterImage()
    {
        if (surface == null) return;

        origin = new PointF(
            (float)((ClientSize.Width - surface.Width * zoom) / 2),
            (float)((ClientSize.Height - surface.Height * zoom) / 2));
        Invalidate();
    }

    private void SetZoom(double newZoom, PointF anchor)
    {
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - zoom) < 1e-6) return;

        // Keep the pixel under the anchor point where it is.
        double imageX = (anchor.X - origin.X) / zoom;
        double imageY = (anchor.Y - origin.Y) / zoom;

        zoom = newZoom;
        origin = new PointF((float)(anchor.X - imageX * zoom), (float)(anchor.Y - imageY * zoom));
        viewAdjustedByUser = true;

        ViewChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public Point ClientToImage(Point client) => new(
        (int)Math.Floor((client.X - origin.X) / zoom),
        (int)Math.Floor((client.Y - origin.Y) / zoom));

    public bool IsInsideImage(Point pixel) =>
        surface != null && pixel.X >= 0 && pixel.Y >= 0 && pixel.X < surface.Width && pixel.Y < surface.Height;

    // ------------------------------------------------------------------ input

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        bool wantsPan = e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && spaceHeld);

        if (wantsPan)
        {
            panning = true;
            viewAdjustedByUser = true;
            panStart = e.Location;
            panOriginStart = origin;
            Cursor = Cursors.SizeAll;
            return;
        }

        if (e.Button is MouseButtons.Left or MouseButtons.Right)
        {
            var pixel = ClientToImage(e.Location);
            PixelMouseDown?.Invoke(this, new PixelMouseEventArgs(pixel, e.Button, IsInsideImage(pixel)));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (panning)
        {
            origin = new PointF(panOriginStart.X + (e.X - panStart.X), panOriginStart.Y + (e.Y - panStart.Y));
            Invalidate();
            return;
        }

        var pixel = ClientToImage(e.Location);
        var args = new PixelMouseEventArgs(pixel, e.Button, IsInsideImage(pixel));
        PixelMouseMove?.Invoke(this, args);

        if (e.Button is MouseButtons.Left or MouseButtons.Right)
        {
            PixelMouseDrag?.Invoke(this, args);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (panning)
        {
            panning = false;
            Cursor = Cursors.Cross;
            return;
        }

        PixelMouseUp?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (ModifierKeys.HasFlag(Keys.Control) || ModifierKeys.HasFlag(Keys.Shift))
        {
            FrameStepRequested?.Invoke(this, e.Delta < 0 ? 1 : -1);
            return;
        }

        double factor = e.Delta > 0 ? 1.2 : 1 / 1.2;
        SetZoom(zoom * factor, e.Location);
    }

    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space) spaceHeld = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space) spaceHeld = false;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        // Keep the frame fitted while the user has not taken over the view.
        if (!viewAdjustedByUser) FitToWindow();
        else Invalidate();
    }

    // ---------------------------------------------------------------- painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var background = new SolidBrush(Theme.CanvasBack))
        {
            g.FillRectangle(background, ClientRectangle);
        }

        if (surface == null)
        {
            DrawEmptyState(g);
            return;
        }

        var destination = new Rectangle(
            (int)Math.Round(origin.X),
            (int)Math.Round(origin.Y),
            (int)Math.Round(surface.Width * zoom),
            (int)Math.Round(surface.Height * zoom));

        DrawChecker(g, destination);

        g.InterpolationMode = zoom >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(surface, destination);

        using (var borderPen = new Pen(Theme.Border))
        {
            g.DrawRectangle(borderPen, destination);
        }

        if (ShowPixelGrid && zoom >= 8) DrawPixelGrid(g, destination);
        if (CropOverlay is { } crop) DrawCropOverlay(g, crop, destination);
    }

    private void DrawEmptyState(Graphics g)
    {
        const string message = "No frames open\n\nFile ▸ Open Folder…  (Ctrl+O)   ·   drag & drop images here";
        using var brush = new SolidBrush(Theme.TextDim);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(message, Theme.UiFont, brush, ClientRectangle, format);
    }

    private void DrawChecker(Graphics g, Rectangle destination)
    {
        checkerBrush ??= CreateCheckerBrush();

        var clip = g.Clip;
        g.SetClip(destination);
        checkerBrush.TranslateTransform(destination.X % 16, destination.Y % 16);
        g.FillRectangle(checkerBrush, destination);
        checkerBrush.ResetTransform();
        g.Clip = clip;
    }

    private static TextureBrush CreateCheckerBrush()
    {
        var tile = new Bitmap(16, 16);
        using (var tileGraphics = Graphics.FromImage(tile))
        {
            tileGraphics.Clear(Color.FromArgb(70, 74, 80));
            using var brush = new SolidBrush(Color.FromArgb(58, 62, 68));
            tileGraphics.FillRectangle(brush, 0, 0, 8, 8);
            tileGraphics.FillRectangle(brush, 8, 8, 8, 8);
        }

        return new TextureBrush(tile) { WrapMode = WrapMode.Tile };
    }

    private void DrawPixelGrid(Graphics g, Rectangle destination)
    {
        using var pen = new Pen(Color.FromArgb(40, 255, 255, 255));
        for (int x = 0; x <= surface!.Width; x++)
        {
            int screenX = destination.X + (int)Math.Round(x * zoom);
            g.DrawLine(pen, screenX, destination.Y, screenX, destination.Bottom);
        }

        for (int y = 0; y <= surface.Height; y++)
        {
            int screenY = destination.Y + (int)Math.Round(y * zoom);
            g.DrawLine(pen, destination.X, screenY, destination.Right, screenY);
        }
    }

    private void DrawCropOverlay(Graphics g, Rectangle crop, Rectangle destination)
    {
        var rectangle = new Rectangle(
            destination.X + (int)Math.Round(crop.X * zoom),
            destination.Y + (int)Math.Round(crop.Y * zoom),
            Math.Max(1, (int)Math.Round(crop.Width * zoom)),
            Math.Max(1, (int)Math.Round(crop.Height * zoom)));

        using (var shade = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
        {
            var region = new Region(destination);
            region.Exclude(rectangle);
            g.FillRegion(shade, region);
            region.Dispose();
        }

        using var pen = new Pen(Color.FromArgb(255, 90, 90), 1.5f) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, rectangle);

        using var handleBrush = new SolidBrush(Color.FromArgb(255, 90, 90));
        foreach (var handle in new[] { new Point(rectangle.Left, rectangle.Top), new Point(rectangle.Right, rectangle.Bottom) })
        {
            g.FillRectangle(handleBrush, handle.X - 3, handle.Y - 3, 7, 7);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) checkerBrush?.Dispose();
        base.Dispose(disposing);
    }
}
