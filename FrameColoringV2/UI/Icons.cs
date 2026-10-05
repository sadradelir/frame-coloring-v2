using System.Drawing.Drawing2D;

namespace FrameColoringV2.UI;

/// <summary>
/// The toolbar icons, drawn with GDI+ instead of shipping image files so they stay
/// crisp at any size and can be tinted to whatever the theme asks for.
/// Every glyph is designed on a 100x100 grid and scaled down on the way out.
/// </summary>
public static class Icons
{
    public static int Toolbar => Theme.Px(20);
    public static int Small => Theme.Px(16);

    public static readonly Color Star = Color.FromArgb(240, 190, 70);

    private static readonly Dictionary<(string Name, int Size, int Color), Bitmap> Cache = new();

    public static Bitmap Get(string name, int? size = null, Color? color = null)
    {
        var tint = color ?? Theme.Text;
        var key = (Name: name, Size: size ?? Toolbar, Color: tint.ToArgb());

        if (!Cache.TryGetValue(key, out var bitmap))
        {
            bitmap = Render(name, key.Size, tint);
            Cache[key] = bitmap;
        }

        return bitmap;
    }

    private static Bitmap Render(string name, int size, Color tint)
    {
        var bitmap = new Bitmap(size, size);
        bitmap.SetResolution(96, 96);

        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size / 100f, size / 100f);

        using var pen = new Pen(tint, 9f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var brush = new SolidBrush(tint);
        using var wash = new SolidBrush(Color.FromArgb(70, tint));

        Draw(name, g, pen, brush, wash);
        return bitmap;
    }

    private static void Draw(string name, Graphics g, Pen pen, Brush brush, Brush wash)
    {
        switch (name)
        {
            case "fill":
                // Bucket with a handle and a drop coming off the lip.
                var bucket = new[]
                {
                    new PointF(24, 38), new PointF(76, 38), new PointF(66, 86), new PointF(34, 86)
                };
                g.FillPolygon(wash, bucket);
                g.DrawPolygon(pen, bucket);
                g.DrawArc(pen, 34, 12, 32, 34, 180, 180);
                g.FillEllipse(brush, 84, 50, 14, 18);
                break;

            case "replace":
                // Two arrows chasing each other: this color becomes that one.
                g.DrawLine(pen, 22, 36, 74, 36);
                g.DrawLines(pen, new[] { new PointF(60, 22), new PointF(76, 36), new PointF(60, 50) });
                g.DrawLine(pen, 78, 70, 26, 70);
                g.DrawLines(pen, new[] { new PointF(40, 56), new PointF(24, 70), new PointF(40, 84) });
                break;

            case "brush":
                using (var handle = new Pen(pen.Brush, 14f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(handle, 82, 20, 46, 56);
                }

                g.FillPolygon(brush, new[]
                {
                    new PointF(40, 50), new PointF(56, 66), new PointF(30, 84), new PointF(16, 88), new PointF(22, 66)
                });
                break;

            case "eraser":
                var rubber = new[]
                {
                    new PointF(20, 62), new PointF(58, 22), new PointF(84, 46), new PointF(46, 86)
                };
                g.FillPolygon(wash, rubber);
                g.DrawPolygon(pen, rubber);
                g.DrawLine(pen, 20, 62, 46, 86);
                break;

            case "picker":
                g.FillEllipse(brush, 60, 12, 30, 30);
                using (var stem = new Pen(pen.Brush, 11f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(stem, 66, 36, 34, 68);
                }

                g.FillPolygon(brush, new[] { new PointF(28, 62), new PointF(42, 76), new PointF(16, 90) });
                break;

            case "crop":
                g.DrawLines(pen, new[] { new PointF(28, 10), new PointF(28, 72), new PointF(90, 72) });
                g.DrawLines(pen, new[] { new PointF(10, 28), new PointF(72, 28), new PointF(72, 90) });
                break;

            case "check":
                g.DrawLines(pen, new[] { new PointF(18, 54), new PointF(40, 76), new PointF(84, 24) });
                break;

            case "next":
                // Arrow running into a wall: move on to the following frame.
                g.DrawLine(pen, 16, 50, 62, 50);
                g.DrawLines(pen, new[] { new PointF(46, 32), new PointF(64, 50), new PointF(46, 68) });
                g.DrawLine(pen, 84, 26, 84, 74);
                break;

            case "onion":
                using (var layers = new Pen(pen.Brush, 7f) { LineJoin = LineJoin.Round })
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float y = 28 + i * 24;
                        g.DrawPolygon(layers, new[]
                        {
                            new PointF(50, y - 14), new PointF(86, y), new PointF(50, y + 14), new PointF(14, y)
                        });
                    }
                }

                break;

            case "zoom-in":
            case "zoom-out":
                g.DrawEllipse(pen, 16, 16, 54, 54);
                g.DrawLine(pen, 68, 68, 88, 88);
                g.DrawLine(pen, 30, 43, 56, 43);
                if (name == "zoom-in") g.DrawLine(pen, 43, 30, 43, 56);
                break;

            case "fit":
                // Four corners pushed outwards.
                g.DrawLines(pen, new[] { new PointF(14, 38), new PointF(14, 14), new PointF(38, 14) });
                g.DrawLines(pen, new[] { new PointF(62, 14), new PointF(86, 14), new PointF(86, 38) });
                g.DrawLines(pen, new[] { new PointF(86, 62), new PointF(86, 86), new PointF(62, 86) });
                g.DrawLines(pen, new[] { new PointF(38, 86), new PointF(14, 86), new PointF(14, 62) });
                break;

            case "actual-size":
                g.DrawRectangle(pen, 16, 16, 68, 68);
                g.FillEllipse(brush, 42, 42, 16, 16);
                break;

            case "star":
            case "star-outline":
                using (var star = StarPath(50, 52, 42, 18))
                using (var outline = new Pen(pen.Brush, 8f) { LineJoin = LineJoin.Round })
                {
                    if (name == "star") g.FillPath(brush, star);
                    else g.DrawPath(outline, star);
                }

                break;

            case "star-clear":
                using (var cleared = StarPath(50, 52, 40, 17))
                using (var clearedPen = new Pen(pen.Brush, 8f) { LineJoin = LineJoin.Round })
                {
                    g.DrawPath(clearedPen, cleared);
                    g.DrawLine(clearedPen, 14, 90, 88, 12);
                }

                break;

            case "save":
                g.DrawRectangle(pen, 16, 16, 68, 68);
                g.FillRectangle(brush, 36, 16, 30, 26);
                g.FillRectangle(wash, 30, 58, 40, 26);
                break;

            case "reload":
                using (var loop = new Pen(pen.Brush, 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(loop, 20, 20, 60, 60, 60, 280);
                }

                g.FillPolygon(brush, new[] { new PointF(76, 8), new PointF(88, 40), new PointF(56, 34) });
                break;

            case "trash":
                g.DrawLine(pen, 16, 28, 84, 28);
                g.DrawLine(pen, 40, 16, 60, 16);
                g.DrawLines(pen, new[] { new PointF(26, 34), new PointF(32, 88), new PointF(68, 88), new PointF(74, 34) });
                break;

            case "select-all":
                g.FillRectangle(wash, 14, 14, 34, 34);
                g.FillRectangle(wash, 52, 14, 34, 34);
                g.FillRectangle(wash, 14, 52, 34, 34);
                g.FillRectangle(brush, 52, 52, 34, 34);
                break;

            case "select-none":
                using (var dashed = new Pen(pen.Brush, 8f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawRectangle(dashed, 16, 16, 68, 68);
                }

                break;

            case "select-invert":
                g.DrawRectangle(pen, 16, 16, 68, 68);
                g.FillPolygon(brush, new[] { new PointF(16, 84), new PointF(84, 16), new PointF(84, 84) });
                break;

            default:
                g.DrawRectangle(pen, 20, 20, 60, 60);
                break;
        }
    }

    private static GraphicsPath StarPath(float cx, float cy, float outer, float inner)
    {
        var points = new PointF[10];
        for (int i = 0; i < 10; i++)
        {
            double angle = -Math.PI / 2 + i * Math.PI / 5;
            float radius = i % 2 == 0 ? outer : inner;
            points[i] = new PointF(cx + (float)(Math.Cos(angle) * radius), cy + (float)(Math.Sin(angle) * radius));
        }

        var path = new GraphicsPath();
        path.AddPolygon(points);
        return path;
    }
}
