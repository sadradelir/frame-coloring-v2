using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using SImage = SixLabors.ImageSharp.Image;

namespace FrameColoringV2.Imaging;

/// <summary>
/// Pixel level operations used by the editor. Everything here works on an
/// <see cref="Image{Rgba32}"/> and has no knowledge of the UI or the file system layout.
/// </summary>
public static class FrameOps
{
    private static bool On(byte v) => v >= 250; // treat 255-ish as "on"

    // ---------------------------------------------------------------- painting

    public static Rgba32 AlphaBlend(Rgba32 over, Rgba32 under)
    {
        float alphaOver = over.A / 255f;
        float alphaUnder = under.A / 255f;

        float alphaResult = alphaOver + alphaUnder * (1 - alphaOver);
        if (alphaResult == 0) return new Rgba32(0, 0, 0, 0);

        byte r = (byte)((over.R * alphaOver + under.R * alphaUnder * (1 - alphaOver)) / alphaResult);
        byte g = (byte)((over.G * alphaOver + under.G * alphaUnder * (1 - alphaOver)) / alphaResult);
        byte b = (byte)((over.B * alphaOver + under.B * alphaUnder * (1 - alphaOver)) / alphaResult);
        byte a = (byte)(alphaResult * 255);

        return new Rgba32(r, g, b, a);
    }

    /// <param name="paintOver">
    /// False (the default) lays the color underneath what is already there, so it only shows
    /// up in the transparent and half transparent pixels and the line art stays untouched.
    /// True paints on top like an ordinary brush.
    /// </param>
    public static void Brush(Image<Rgba32> image, int centerX, int centerY, Rgba32 color, int size,
        bool paintOver = false)
    {
        for (int x = centerX - size; x <= centerX + size; x++)
        {
            for (int y = centerY - size; y <= centerY + size; y++)
            {
                if (x < 0 || x >= image.Width || y < 0 || y >= image.Height) continue;

                image[x, y] = color.A == 0
                    ? new Rgba32(0, 0, 0, 0) // eraser
                    : paintOver
                        ? AlphaBlend(color, image[x, y])
                        : AlphaBlend(image[x, y], color);
            }
        }
    }

    /// <summary>
    /// Flood fill that walks freely through transparent pixels and is allowed to bleed
    /// <paramref name="maxDistance"/> pixels into opaque ones (so anti-aliased edges get covered).
    /// </summary>
    public static void Fill(Image<Rgba32> image, int x, int y, Rgba32 color, int maxDistance,
        HashSet<(int, int)> visited)
    {
        if (x < 0 || x >= image.Width || y < 0 || y >= image.Height) return;

        var queue = new Queue<(int x, int y, int distance)>();
        queue.Enqueue((x, y, 0));

        while (queue.Count > 0)
        {
            var (cx, cy, distance) = queue.Dequeue();

            if (cx < 0 || cx >= image.Width || cy < 0 || cy >= image.Height) continue;
            if (distance > maxDistance) continue;
            if (!visited.Add((cx, cy))) continue;

            int nextDistance = image[cx, cy].A == 0 ? 0 : distance + 1;
            queue.Enqueue((cx + 1, cy, nextDistance));
            queue.Enqueue((cx - 1, cy, nextDistance));
            queue.Enqueue((cx, cy + 1, nextDistance));
            queue.Enqueue((cx, cy - 1, nextDistance));

            image[cx, cy] = AlphaBlend(image[cx, cy], color);
        }
    }

    /// <summary>
    /// Recolors a connected region that shares the seed pixel's key color, keeping the
    /// per pixel darkening of the original.
    /// </summary>
    public static void ReplaceFill(Image<Rgba32> image, int x, int y, Rgba32 targetColor,
        HashSet<(int, int)> visited, int maxDistance = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(visited);

        if (x < 0 || x >= image.Width || y < 0 || y >= image.Height) return;

        // Darkened pixels are mapped back to the key color they came from.
        Rgba32 sourceBase = GuessKeyBaseFromPixel(image[x, y]);
        if (!IsLikelyKeyBase(sourceBase)) return;

        const int offMax = 25; // leakage allowed in "off" channels
        const int onMin = 10;  // below this a pixel counts as black lineart

        var queue = new Queue<(int x, int y, int distance)>();
        queue.Enqueue((x, y, 0));

        while (queue.Count > 0)
        {
            var (cx, cy, distance) = queue.Dequeue();

            if (cx < 0 || cx >= image.Width || cy < 0 || cy >= image.Height) continue;
            if (distance > maxDistance) continue;
            if (!visited.Add((cx, cy))) continue;

            var pixel = image[cx, cy];
            if (!ColorHelper.IsDarkenedKeyColor(pixel, sourceBase, offMax, onMin)) continue;

            image[cx, cy] = ColorHelper.RotateKeyColorKeepDarkening(pixel, sourceBase, targetColor);

            int nextDistance = pixel.A == 0 ? 0 : distance + 1;
            queue.Enqueue((cx + 1, cy, nextDistance));
            queue.Enqueue((cx - 1, cy, nextDistance));
            queue.Enqueue((cx, cy + 1, nextDistance));
            queue.Enqueue((cx, cy - 1, nextDistance));
        }
    }

    private static bool IsLikelyKeyBase(Rgba32 baseKey) => On(baseKey.R) || On(baseKey.G) || On(baseKey.B);

    private static Rgba32 GuessKeyBaseFromPixel(Rgba32 p)
    {
        int max = Math.Max(p.R, Math.Max(p.G, p.B));
        if (max < 10) return new Rgba32(0, 0, 0, 255); // near black

        // A channel counts as "on" when it is close to the max, which keeps
        // yellow / magenta / cyan keys intact.
        const double closeToMax = 0.90;
        bool rOn = p.R >= max * closeToMax;
        bool gOn = p.G >= max * closeToMax;
        bool bOn = p.B >= max * closeToMax;

        return new Rgba32((byte)(rOn ? 255 : 0), (byte)(gOn ? 255 : 0), (byte)(bOn ? 255 : 0), 255);
    }

    // ------------------------------------------------------------- whole frame

    /// <summary>Boosts alpha so semi transparent edges become solid enough to fill.</summary>
    public static void Empower(Image<Rgba32> image, float alphaMultiplier = 1.3f)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    float boosted = row[x].A * alphaMultiplier;
                    row[x].A = boosted > 255 ? (byte)255 : (byte)boosted;
                }
            }
        });
    }

    /// <summary>Resets every pixel to fully transparent.</summary>
    public static void ClearTransparent(Image<Rgba32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                accessor.GetRowSpan(y).Clear();
            }
        });
    }

    /// <summary>Draws a soft black drop shadow of the sprite behind itself.</summary>
    public static void SDF(Image<Rgba32> image, float blurSigma = 5.5f)
    {
        using Image<Rgba32> shadow = image.Clone(ctx => ctx.GaussianBlur(blurSigma));

        shadow.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32(0, 0, 0, row[x].A); // black, same alpha
                }
            }
        });

        using var composed = new Image<Rgba32>(image.Width, image.Height);
        composed.Mutate(ctx =>
        {
            ctx.DrawImage(shadow, 1f);
            ctx.DrawImage(image, 1f);
        });

        image.Mutate(ctx => ctx.DrawImage(composed, 1f));
    }

    public static Image<Rgba32> Crop(Image<Rgba32> image, int x, int y, int width, int height)
    {
        x = Math.Clamp(x, 0, Math.Max(0, image.Width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, image.Height - 1));
        width = Math.Clamp(width, 1, image.Width - x);
        height = Math.Clamp(height, 1, image.Height - y);

        return image.Clone(ctx => ctx.Crop(new Rectangle(x, y, width, height)));
    }

    /// <summary>Bounding box of every non transparent pixel, or an empty rectangle.</summary>
    public static Rectangle GetTrimBounds(Image<Rgba32> image)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        bool foundContent = false;

        for (int x = 0; x < image.Width; x++)
        {
            for (int y = 0; y < image.Height; y++)
            {
                if (image[x, y].A == 0) continue;

                foundContent = true;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return foundContent
            ? new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)
            : new Rectangle(0, 0, 0, 0);
    }

    /// <summary>Counts transparent pixels enclosed by the sprite, i.e. holes still to be colored.</summary>
    public static int Analyze(Image<Rgba32> image)
    {
        int w = image.Width;
        int h = image.Height;

        var reachable = new bool[w, h];
        var queue = new Queue<(int x, int y)>();

        void TryEnqueue(int nx, int ny)
        {
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) return;
            if (reachable[nx, ny]) return;
            if (image[nx, ny].A != 0) return;
            reachable[nx, ny] = true;
            queue.Enqueue((nx, ny));
        }

        TryEnqueue(0, 0);

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            TryEnqueue(x + 1, y);
            TryEnqueue(x - 1, y);
            TryEnqueue(x, y + 1);
            TryEnqueue(x, y - 1);
        }

        int count = 0;
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
        {
            if (image[x, y].A == 0 && !reachable[x, y]) count++;
        }

        return count;
    }

    // ----------------------------------------------------------------- import

    /// <summary>
    /// Copies every frame of <paramref name="sourceFolder"/> into <paramref name="targetFolder"/>,
    /// skipping frames that are pixel identical to the previous one. Returns the copied paths.
    /// </summary>
    public static List<string> CopyNonDuplicatesTo(string sourceFolder, string targetFolder,
        IProgress<(int current, int total, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetFolder);

        var files = Directory.GetFiles(sourceFolder)
            .Where(IsSupportedImage)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var copied = new List<string>();
        Image<Rgba32>? previous = null;

        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string file = files[i];
                progress?.Report((i + 1, files.Count, $"Processing {i + 1}/{files.Count}: {Path.GetFileName(file)}"));

                var current = SImage.Load<Rgba32>(file);
                if (previous == null || !PixelEquals(current, previous))
                {
                    string destination = Path.Combine(targetFolder, Path.GetFileName(file));
                    File.Copy(file, destination, overwrite: true);
                    copied.Add(destination);
                }

                previous?.Dispose();
                previous = current;
            }
        }
        finally
        {
            previous?.Dispose();
        }

        progress?.Report((files.Count, files.Count, $"Done. {copied.Count} unique frames copied."));
        return copied;
    }

    public static bool IsSupportedImage(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".png" or ".bmp" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".tga";
    }

    private static bool PixelEquals(Image<Rgba32> a, Image<Rgba32> b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;

        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                if (a[x, y] != b[x, y]) return false;
            }
        }

        return true;
    }
}
