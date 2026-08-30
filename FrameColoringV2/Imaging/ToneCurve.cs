using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FrameColoringV2.Imaging;

public readonly record struct CurvePoint(float X, float Y);

/// <summary>
/// One curve of a Photoshop style Curves adjustment: a handful of control points through
/// which a smooth, monotone curve is fitted and baked into a 256 entry lookup table.
/// X is the input value, Y the output value, both 0..255.
/// </summary>
public sealed class ToneCurve
{
    private readonly List<CurvePoint> points = new() { new CurvePoint(0, 0), new CurvePoint(255, 255) };

    public IReadOnlyList<CurvePoint> Points => points;

    public bool IsIdentity =>
        points.Count == 2 &&
        points[0] is { X: 0, Y: 0 } &&
        points[1] is { X: 255, Y: 255 };

    public ToneCurve Clone()
    {
        var clone = new ToneCurve();
        clone.points.Clear();
        clone.points.AddRange(points);
        return clone;
    }

    public void Reset()
    {
        points.Clear();
        points.Add(new CurvePoint(0, 0));
        points.Add(new CurvePoint(255, 255));
    }

    /// <summary>Adds a control point and returns its index.</summary>
    public int Add(float x, float y)
    {
        x = Math.Clamp(x, 0, 255);
        y = Math.Clamp(y, 0, 255);

        int index = points.FindIndex(point => point.X > x);
        if (index < 0) index = points.Count;

        points.Insert(index, new CurvePoint(x, y));
        return index;
    }

    public void RemoveAt(int index)
    {
        // The two ends stay put, otherwise the curve would lose its 0..255 range.
        if (index <= 0 || index >= points.Count - 1) return;
        points.RemoveAt(index);
    }

    /// <summary>Moves a point, keeping the control points in order and the ends at 0 and 255.</summary>
    public void Move(int index, float x, float y)
    {
        if (index < 0 || index >= points.Count) return;

        y = Math.Clamp(y, 0, 255);

        if (index == 0) x = 0;
        else if (index == points.Count - 1) x = 255;
        else x = Math.Clamp(x, points[index - 1].X + 1, points[index + 1].X - 1);

        points[index] = new CurvePoint(x, y);
    }

    public int IndexNear(float x, float y, float radius)
    {
        int best = -1;
        float bestDistance = radius * radius;

        for (int i = 0; i < points.Count; i++)
        {
            float dx = points[i].X - x;
            float dy = points[i].Y - y;
            float distance = dx * dx + dy * dy;

            if (distance > bestDistance) continue;

            bestDistance = distance;
            best = i;
        }

        return best;
    }

    /// <summary>
    /// Bakes the curve into a lookup table using monotone cubic interpolation
    /// (Fritsch–Carlson), which passes through every control point without the
    /// overshoot a plain spline would give.
    /// </summary>
    public byte[] BuildLut()
    {
        var lut = new byte[256];
        int n = points.Count;

        if (n == 1)
        {
            Array.Fill(lut, (byte)Math.Clamp(points[0].Y, 0, 255));
            return lut;
        }

        var x = new float[n];
        var y = new float[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = points[i].X;
            y[i] = points[i].Y;
        }

        var slopes = ComputeSlopes(x, y, n);

        for (int value = 0; value < 256; value++)
        {
            lut[value] = (byte)Math.Clamp(MathF.Round(Evaluate(x, y, slopes, n, value)), 0, 255);
        }

        return lut;
    }

    private static float[] ComputeSlopes(float[] x, float[] y, int n)
    {
        var secants = new float[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            float dx = x[i + 1] - x[i];
            secants[i] = dx <= 0 ? 0 : (y[i + 1] - y[i]) / dx;
        }

        var slopes = new float[n];
        slopes[0] = secants[0];
        slopes[n - 1] = secants[n - 2];

        for (int i = 1; i < n - 1; i++)
        {
            // A local extremum flattens the curve, which is what keeps it monotone.
            slopes[i] = secants[i - 1] * secants[i] <= 0 ? 0 : (secants[i - 1] + secants[i]) / 2;
        }

        for (int i = 0; i < n - 1; i++)
        {
            if (secants[i] == 0)
            {
                slopes[i] = 0;
                slopes[i + 1] = 0;
                continue;
            }

            float alpha = slopes[i] / secants[i];
            float beta = slopes[i + 1] / secants[i];
            float magnitude = alpha * alpha + beta * beta;

            if (magnitude <= 9) continue;

            float scale = 3f / MathF.Sqrt(magnitude);
            slopes[i] = scale * alpha * secants[i];
            slopes[i + 1] = scale * beta * secants[i];
        }

        return slopes;
    }

    private static float Evaluate(float[] x, float[] y, float[] slopes, int n, float value)
    {
        if (value <= x[0]) return y[0];
        if (value >= x[n - 1]) return y[n - 1];

        int segment = 0;
        while (segment < n - 2 && value > x[segment + 1]) segment++;

        float h = x[segment + 1] - x[segment];
        if (h <= 0) return y[segment];

        float t = (value - x[segment]) / h;
        float t2 = t * t;
        float t3 = t2 * t;

        // Cubic Hermite basis.
        return (2 * t3 - 3 * t2 + 1) * y[segment]
             + (t3 - 2 * t2 + t) * h * slopes[segment]
             + (-2 * t3 + 3 * t2) * y[segment + 1]
             + (t3 - t2) * h * slopes[segment + 1];
    }
}

/// <summary>
/// The full adjustment: a composite curve applied on top of the per channel ones,
/// exactly like the RGB / Red / Green / Blue entries in Photoshop's Curves dialog,
/// plus an alpha curve which is handy for sprites.
/// </summary>
public sealed class CurveSet
{
    public ToneCurve Composite { get; private set; } = new();
    public ToneCurve Red { get; private set; } = new();
    public ToneCurve Green { get; private set; } = new();
    public ToneCurve Blue { get; private set; } = new();
    public ToneCurve Alpha { get; private set; } = new();

    public bool IsIdentity =>
        Composite.IsIdentity && Red.IsIdentity && Green.IsIdentity && Blue.IsIdentity && Alpha.IsIdentity;

    public ToneCurve this[CurveChannel channel] => channel switch
    {
        CurveChannel.Red => Red,
        CurveChannel.Green => Green,
        CurveChannel.Blue => Blue,
        CurveChannel.Alpha => Alpha,
        _ => Composite
    };

    public CurveSet Clone() => new()
    {
        Composite = Composite.Clone(),
        Red = Red.Clone(),
        Green = Green.Clone(),
        Blue = Blue.Clone(),
        Alpha = Alpha.Clone()
    };

    public void Reset()
    {
        Composite.Reset();
        Red.Reset();
        Green.Reset();
        Blue.Reset();
        Alpha.Reset();
    }

    /// <summary>Per channel curve first, then the composite one, folded into a single table.</summary>
    public (byte[] red, byte[] green, byte[] blue, byte[] alpha) BuildLuts()
    {
        var composite = Composite.BuildLut();

        return (Compose(Red.BuildLut(), composite),
                Compose(Green.BuildLut(), composite),
                Compose(Blue.BuildLut(), composite),
                Alpha.BuildLut());
    }

    private static byte[] Compose(byte[] channel, byte[] composite)
    {
        var result = new byte[256];
        for (int value = 0; value < 256; value++) result[value] = composite[channel[value]];
        return result;
    }

    /// <summary>
    /// Applies the curves in place. Fully transparent pixels are left alone so the colour and
    /// the alpha hiding in them cannot surface as fringes around the sprite.
    /// </summary>
    public void Apply(Image<Rgba32> image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var (red, green, blue, alpha) = BuildLuts();

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    ref Rgba32 pixel = ref row[x];
                    if (pixel.A == 0) continue;

                    pixel.R = red[pixel.R];
                    pixel.G = green[pixel.G];
                    pixel.B = blue[pixel.B];
                    pixel.A = alpha[pixel.A];
                }
            }
        });
    }

    /// <summary>Histograms of the open frame, drawn behind the curve as a guide.</summary>
    public static int[][] BuildHistograms(Image<Rgba32> image)
    {
        // 0 = luminance, 1 = red, 2 = green, 3 = blue, 4 = alpha
        var histograms = new int[5][];
        for (int i = 0; i < histograms.Length; i++) histograms[i] = new int[256];

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 pixel = row[x];
                    if (pixel.A == 0) continue;

                    int luminance = (int)(0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B);
                    histograms[0][Math.Clamp(luminance, 0, 255)]++;
                    histograms[1][pixel.R]++;
                    histograms[2][pixel.G]++;
                    histograms[3][pixel.B]++;
                    histograms[4][pixel.A]++;
                }
            }
        });

        return histograms;
    }
}

public enum CurveChannel
{
    Composite,
    Red,
    Green,
    Blue,
    Alpha
}
