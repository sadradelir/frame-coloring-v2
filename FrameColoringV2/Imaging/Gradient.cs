using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FrameColoringV2.Imaging;

public readonly record struct GradientStop(float Position, Rgba32 Color);

/// <summary>
/// A colour ramp defined by stops, and the gradient map built from it: every pixel's
/// brightness picks a colour out of the ramp, the same as Photoshop's Gradient Map.
/// </summary>
public sealed class Gradient
{
    private readonly List<GradientStop> stops = new();

    public Gradient(IEnumerable<GradientStop>? initial = null)
    {
        stops.AddRange(initial ?? new[]
        {
            new GradientStop(0f, new Rgba32(0, 0, 0, 255)),
            new GradientStop(1f, new Rgba32(255, 255, 255, 255))
        });

        Sort();
        EnsureTwoStops();
    }

    public IReadOnlyList<GradientStop> Stops => stops;

    public Gradient Clone() => new(stops);

    /// <summary>Adds a stop and returns its index.</summary>
    public int Add(float position, Rgba32 color)
    {
        position = Math.Clamp(position, 0f, 1f);
        stops.Add(new GradientStop(position, color));
        Sort();

        return stops.FindIndex(stop => stop.Position == position && stop.Color.Equals(color));
    }

    public void RemoveAt(int index)
    {
        // Two stops are the minimum a ramp can be built from.
        if (stops.Count <= 2 || index < 0 || index >= stops.Count) return;
        stops.RemoveAt(index);
    }

    /// <summary>Moves a stop and returns its index after the list is put back in order.</summary>
    public int Move(int index, float position)
    {
        if (index < 0 || index >= stops.Count) return index;

        var moved = stops[index] with { Position = Math.Clamp(position, 0f, 1f) };
        stops[index] = moved;
        Sort();

        return stops.IndexOf(moved);
    }

    public void SetColor(int index, Rgba32 color)
    {
        if (index < 0 || index >= stops.Count) return;
        stops[index] = stops[index] with { Color = color };
    }

    public int IndexNear(float position, float tolerance)
    {
        int best = -1;
        float bestDistance = tolerance;

        for (int i = 0; i < stops.Count; i++)
        {
            float distance = Math.Abs(stops[i].Position - position);
            if (distance > bestDistance) continue;

            bestDistance = distance;
            best = i;
        }

        return best;
    }

    /// <summary>Colour of the ramp at <paramref name="position"/> (0..1).</summary>
    public Rgba32 Sample(float position)
    {
        position = Math.Clamp(position, 0f, 1f);

        if (position <= stops[0].Position) return stops[0].Color;
        if (position >= stops[^1].Position) return stops[^1].Color;

        for (int i = 0; i < stops.Count - 1; i++)
        {
            var left = stops[i];
            var right = stops[i + 1];
            if (position > right.Position) continue;

            float span = right.Position - left.Position;
            float t = span <= 0 ? 0 : (position - left.Position) / span;

            return new Rgba32(
                Lerp(left.Color.R, right.Color.R, t),
                Lerp(left.Color.G, right.Color.G, t),
                Lerp(left.Color.B, right.Color.B, t),
                Lerp(left.Color.A, right.Color.A, t));
        }

        return stops[^1].Color;
    }

    /// <summary>256 entry table, indexed by brightness.</summary>
    public Rgba32[] BuildLut(bool reverse = false)
    {
        var lut = new Rgba32[256];
        for (int i = 0; i < 256; i++)
        {
            float position = i / 255f;
            lut[i] = Sample(reverse ? 1f - position : position);
        }

        return lut;
    }

    private static byte Lerp(byte a, byte b, float t) => (byte)Math.Clamp(MathF.Round(a + (b - a) * t), 0, 255);

    private void Sort() => stops.Sort((a, b) => a.Position.CompareTo(b.Position));

    private void EnsureTwoStops()
    {
        if (stops.Count >= 2) return;

        if (stops.Count == 0) stops.Add(new GradientStop(0f, new Rgba32(0, 0, 0, 255)));
        stops.Add(new GradientStop(1f, new Rgba32(255, 255, 255, 255)));
    }

    // --------------------------------------------------------------- presets

    public static IReadOnlyList<(string Name, Func<Gradient> Create)> Presets { get; } =
        new (string, Func<Gradient>)[]
    {
        ("Black to white", () => FromHex((0f, "#000000"), (1f, "#ffffff"))),
        ("White to black", () => FromHex((0f, "#ffffff"), (1f, "#000000"))),
        ("Sepia", () => FromHex((0f, "#1b1109"), (0.5f, "#8a6134"), (1f, "#f5e3c8"))),
        ("Cold steel", () => FromHex((0f, "#0b1420"), (0.5f, "#4c6f8f"), (1f, "#e8f2ff"))),
        ("Fire", () => FromHex((0f, "#1a0500"), (0.35f, "#a02704"), (0.7f, "#f08a1a"), (1f, "#ffe9a8"))),
        ("Toxic", () => FromHex((0f, "#04140a"), (0.5f, "#2f8f3a"), (1f, "#ddff9c"))),
        ("Violet dusk", () => FromHex((0f, "#140a24"), (0.5f, "#6b4a9c"), (1f, "#ffd9f0")))
    };

    public static Gradient FromHex(params (float Position, string Hex)[] entries) =>
        new(entries.Select(entry => new GradientStop(entry.Position, Rgba32.ParseHex(entry.Hex))));

    // ------------------------------------------------------------ the filter

    /// <summary>
    /// Replaces every pixel's colour with the ramp colour for its brightness.
    /// Alpha is kept, fully transparent pixels are skipped, and <paramref name="amount"/>
    /// (0..1) blends between the original colour and the mapped one.
    /// </summary>
    public void ApplyMap(Image<Rgba32> image, bool reverse = false, float amount = 1f,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var lut = BuildLut(reverse);
        amount = Math.Clamp(amount, 0f, 1f);

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

                    int brightness = (int)(0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B);
                    Rgba32 mapped = lut[Math.Clamp(brightness, 0, 255)];

                    pixel.R = Lerp(pixel.R, mapped.R, amount);
                    pixel.G = Lerp(pixel.G, mapped.G, amount);
                    pixel.B = Lerp(pixel.B, mapped.B, amount);
                }
            }
        });
    }
}
