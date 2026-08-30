using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FrameColoringV2.Imaging;

/// <summary>
/// Median filter over a circular neighbourhood, the same idea as Photoshop's
/// Filter ▸ Noise ▸ Median: every pixel becomes the median of its neighbours inside a
/// disc of the given radius. It removes speckles and rounds off jagged edges without the
/// smearing a blur would give.
///
/// The window slides one pixel at a time and keeps a histogram per channel, so the cost per
/// pixel grows with the radius only through the column updates, not through re-reading the
/// whole disc (Huang's running median).
/// </summary>
public static class MedianFilter
{
    public const int MinRadius = 1;
    public const int MaxRadius = 24;

    /// <summary>
    /// Applies the filter in place.
    /// </summary>
    /// <param name="ignoreTransparent">
    /// When true the colour channels only take fully or partly opaque neighbours into account,
    /// so the colour hiding inside transparent pixels cannot bleed into the sprite outline.
    /// Alpha itself is always filtered over the whole disc.
    /// </param>
    public static void Apply(Image<Rgba32> image, int radius, bool ignoreTransparent = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        radius = Math.Clamp(radius, MinRadius, MaxRadius);
        int width = image.Width;
        int height = image.Height;
        if (width == 0 || height == 0) return;

        var source = new Rgba32[width * height];
        image.CopyPixelDataTo(source);
        var target = new Rgba32[width * height];

        // Half width of the disc for every row offset.
        var spans = new int[radius * 2 + 1];
        for (int dy = -radius; dy <= radius; dy++)
        {
            spans[dy + radius] = (int)Math.Floor(Math.Sqrt(radius * radius - dy * dy));
        }

        var options = new ParallelOptions { CancellationToken = cancellationToken };
        Parallel.For(0, height, options,
            () => new RowState(),
            (y, _, state) =>
            {
                FilterRow(source, target, width, height, radius, spans, y, ignoreTransparent, state);
                return state;
            },
            _ => { });

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                target.AsSpan(y * width, width).CopyTo(accessor.GetRowSpan(y));
            }
        });
    }

    private static void FilterRow(Rgba32[] source, Rgba32[] target, int width, int height, int radius,
        int[] spans, int y, bool ignoreTransparent, RowState state)
    {
        state.Reset();

        // Fill the window for x = 0. Coordinates outside the image are clamped to the edge,
        // which keeps the sample count constant and avoids dark borders.
        for (int dy = -radius; dy <= radius; dy++)
        {
            int sampleY = Math.Clamp(y + dy, 0, height - 1);
            int span = spans[dy + radius];

            for (int dx = -span; dx <= span; dx++)
            {
                state.Add(source[sampleY * width + Math.Clamp(dx, 0, width - 1)], ignoreTransparent);
            }
        }

        for (int x = 0; x < width; x++)
        {
            if (x > 0)
            {
                // Move the disc one pixel to the right: drop its left column, take a new right one.
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int sampleY = Math.Clamp(y + dy, 0, height - 1);
                    int span = spans[dy + radius];
                    int rowStart = sampleY * width;

                    state.Remove(source[rowStart + Math.Clamp(x - 1 - span, 0, width - 1)], ignoreTransparent);
                    state.Add(source[rowStart + Math.Clamp(x + span, 0, width - 1)], ignoreTransparent);
                }
            }

            var original = source[y * width + x];
            target[y * width + x] = state.HasColorSamples
                ? new Rgba32(state.Red.Median(), state.Green.Median(), state.Blue.Median(), state.Alpha.Median())
                : new Rgba32(original.R, original.G, original.B, state.Alpha.Median());
        }
    }

    private sealed class RowState
    {
        public readonly ChannelHistogram Red = new();
        public readonly ChannelHistogram Green = new();
        public readonly ChannelHistogram Blue = new();
        public readonly ChannelHistogram Alpha = new();

        public bool HasColorSamples => Red.Count > 0;

        public void Reset()
        {
            Red.Clear();
            Green.Clear();
            Blue.Clear();
            Alpha.Clear();
        }

        public void Add(Rgba32 pixel, bool ignoreTransparent)
        {
            Alpha.Add(pixel.A);
            if (ignoreTransparent && pixel.A == 0) return;

            Red.Add(pixel.R);
            Green.Add(pixel.G);
            Blue.Add(pixel.B);
        }

        public void Remove(Rgba32 pixel, bool ignoreTransparent)
        {
            Alpha.Remove(pixel.A);
            if (ignoreTransparent && pixel.A == 0) return;

            Red.Remove(pixel.R);
            Green.Remove(pixel.G);
            Blue.Remove(pixel.B);
        }
    }

    /// <summary>
    /// A 256 bin histogram that keeps its median between updates, so reading the median back
    /// only has to step over the few bins the window moved past.
    /// </summary>
    private sealed class ChannelHistogram
    {
        private readonly int[] bins = new int[256];
        private int median;
        private int below; // samples strictly below the current median bin

        public int Count { get; private set; }

        public void Clear()
        {
            Array.Clear(bins);
            median = 0;
            below = 0;
            Count = 0;
        }

        public void Add(byte value)
        {
            bins[value]++;
            if (value < median) below++;
            Count++;
        }

        public void Remove(byte value)
        {
            bins[value]--;
            if (value < median) below--;
            Count--;
        }

        public byte Median()
        {
            if (Count == 0) return 0;

            int rank = (Count + 1) / 2;
            while (below + bins[median] < rank)
            {
                below += bins[median];
                median++;
            }

            while (below >= rank)
            {
                median--;
                below -= bins[median];
            }

            return (byte)median;
        }
    }
}
