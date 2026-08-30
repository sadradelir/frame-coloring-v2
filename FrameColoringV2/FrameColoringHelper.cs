using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using SixLabors.ImageSharp.Processing;

namespace FrameColoringV2;

public class FrameColoringHelper
{
    public static void CopyNonDuplicatesTo(string folderPath, string processedFolderPath,
        IProgress<(int current, int total, string message)> progress = null)
    {
        var enumerable = System.IO.Directory.GetFiles(folderPath);
        int total = enumerable.Length;
        int index = 0;
        Image<Rgba32> lastImage = null;
        foreach (var file in enumerable)
        {
            index++;
            progress?.Report((index, total, $"Processing {index}/{total}: {Path.GetFileName(file)}"));

            if (lastImage != null)
            {
                var image = Image.Load<Rgba32>(file);
                // check if the two images are exactly the same
                if (!SameFile(image, lastImage, file))
                {
                    // copy to processed folder
                    File.Copy(file,
                        Path.Combine(processedFolderPath, Path.GetFileName(file)));
                }
            }
            else
            {
                File.Copy(file,
                    Path.Combine(processedFolderPath, Path.GetFileName(file)));
            }

            lastImage = Image.Load<Rgba32>(file);
        }

        progress?.Report((total, total, "Done."));
    }

    private static bool SameFile(Image<Rgba32> image, Image<Rgba32> lastImage, string file)
    {
        if (image.Width == lastImage.Width && image.Height == lastImage.Height)
        {
            bool same = true;
            for (int x = 0; x < image.Width; x++)
            {
                for (int y = 0; y < image.Height; y++)
                {
                    if (image[x, y] != lastImage[x, y])
                    {
                        same = false;
                        break;
                    }
                }
            }

            return same;
        }
        else
        {
            return false;
        }
    }

    public static void ProcessImage(string imagePath, Image<Rgba32> colorMask)
    {
        using var image = Image.Load<Rgba32>(imagePath);
        var image2 = image.CloneAs<Rgba32>();
        Empower(image2);
        Queue<(int, int, Rgba32)> fillZones = new Queue<(int, int, Rgba32)>();
        FindFillZones(colorMask, fillZones);
        HashSet<(int, int)> visited = new HashSet<(int, int)>();
        while (fillZones.Count > 0)
        {
            var (x, y, color) = fillZones.Dequeue();
            Fill(image2, x, y, color, 3, visited);
        }

        image2.Save(imagePath.Replace("processed", "colored")
            .Replace(".png", "_processed.png"));
    }

    private static void FindFillZones(Image<Rgba32> image, Queue<(int, int, Rgba32)> fillZones)
    {
        for (int x = 0; x < image.Width; x++)
        {
            for (int y = 0; y < image.Height; y++)
            {
                if (image[x, y].R + image[x, y].G + image[x, y].B > 0 && image[x, y].A == 255)
                {
                    fillZones.Enqueue((x, y, image[x, y]));
                }
            }
        }
    }

    public static void Empower(Image<Rgba32> image2)
    {
        // find the max alpha value
        float maxAlpha = 0;
        for (int x = 0; x < image2.Width; x++)
        {
            for (int y = 0; y < image2.Height; y++)
            {
                if (image2[x, y].A > maxAlpha)
                {
                    maxAlpha = image2[x, y].A;
                }
            }
        }

        float alphaMultiplier = 1.3f;
        for (int x = 0; x < image2.Width; x++)
        {
            for (int y = 0; y < image2.Height; y++)
            {
                var tAd = (image2[x, y].A * alphaMultiplier);
                byte tA = tAd > 255 ? (byte)255 : (byte)tAd;
                image2[x, y] =
                    new Rgba32(image2[x, y].R,
                        image2[x, y].G,
                        image2[x, y].B, tA
                    );
            }
        }
    }

    public static void Fill(Image<Rgba32> image, int x, int y, Rgba32 targetColor, int maxDistance,
        HashSet<(int, int)> visited)
    {
        int iterations = 0;
        if (x < 0 || x >= image.Width || y < 0 || y >= image.Height)
            return;

        // Use a queue to process pixels iteratively
        var queue = new Queue<(int, int, int)>(); // (x, y, distance)

        queue.Enqueue((x, y, 0));

        while (queue.Count > 0)
        {
            iterations++;
            var (currentX, currentY, distance) = queue.Dequeue();
            if (visited.Contains((currentX, currentY)))
                continue;

            // Skip if out of bounds or already processed
            if (currentX < 0 || currentX >= image.Width || currentY < 0 || currentY >= image.Height)
                continue;

            if (distance > maxDistance)
                continue;

            // Add adjacent pixels to the queue
            if (image[currentX, currentY].A == 0)
            {
                queue.Enqueue((currentX + 1, currentY, 0));
                queue.Enqueue((currentX - 1, currentY, 0));
                queue.Enqueue((currentX, currentY + 1, 0));
                queue.Enqueue((currentX, currentY - 1, 0));
            }
            else
            {
                queue.Enqueue((currentX + 1, currentY, distance + 1));
                queue.Enqueue((currentX - 1, currentY, distance + 1));
                queue.Enqueue((currentX, currentY + 1, distance + 1));
                queue.Enqueue((currentX, currentY - 1, distance + 1));
            }

            // alpha blending
            image[currentX, currentY] = AlphaBlend(image[currentX, currentY], targetColor);
            visited.Add((currentX, currentY));
        }
    }


   public static void ReplaceFill(
        Image<Rgba32> image,
        int x,
        int y,
        Rgba32 targetColor,
        HashSet<(int, int)> visited,
        int maxDistance = 999999,
        double cosThreshold = 0.999,      // not used in key-color mode, kept for signature compatibility
        double blackLenThreshold = 12.0)  // not used directly; we use onMin as near-black cutoff
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        if (visited == null) throw new ArgumentNullException(nameof(visited));

        if (x < 0 || x >= image.Width || y < 0 || y >= image.Height)
            return;

        // Source color is determined by the clicked pixel's "dominant key direction".
        // If you always want a specific source (e.g., always replace GREEN), hardcode it here:
        // var sourceBase = new Rgba32(0,255,0);
        //
        // Otherwise, use the seed pixel's closest key base; simplest: treat seed as the base itself.
        // Since your sprites use key colors, seed is usually already a key (or darkened key).
        var seed = image[x, y];

        // Decide sourceBase from seed by checking which channel(s) are dominant.
        // This maps darkened pixels back to their base key color.
        Rgba32 sourceBase = GuessKeyBaseFromPixel(seed);

        // If the seed is near-black or not a key-ish color, don't do anything.
        if (!IsLikelyKeyBase(sourceBase))
            return;

        var queue = new Queue<(int x, int y, int dist)>();
        queue.Enqueue((x, y, 0));

        // Parameters for membership test (tweak if needed)
        const int offMax = 25; // how much leakage in "off" channels you allow
        const int onMin = 10;  // below this treat as black/lineart

        while (queue.Count > 0)
        {
            var (cx, cy, dist) = queue.Dequeue();

            if (cx < 0 || cx >= image.Width || cy < 0 || cy >= image.Height)
                continue;

            if (dist > maxDistance)
                continue;

            if (!visited.Add((cx, cy)))
                continue;

            var px = image[cx, cy];

            // Only operate on pixels that are part of the same (darkened) key color region
            if (!ColorHelper.IsDarkenedKeyColor(px, sourceBase, offMax, onMin))
                continue;

            // Rotate into target while keeping per-pixel darkness k
            var rotated = ColorHelper.RotateKeyColorKeepDarkening(px, sourceBase, targetColor);

            // Preserve your smooth edges: blend rather than hard overwrite
            image[cx, cy] = rotated;

            // Distance logic similar to what you had
            int nd = (px.A == 0) ? 0 : dist + 1;

            queue.Enqueue((cx + 1, cy, nd));
            queue.Enqueue((cx - 1, cy, nd));
            queue.Enqueue((cx, cy + 1, nd));
            queue.Enqueue((cx, cy - 1, nd));
        }
    }
   
   private static bool On(byte v) => v >= 250; // treat 255-ish as "on"

   
   // ---- Helpers to infer which key color the seed belongs to ----

   private static bool IsLikelyKeyBase(Rgba32 baseKey)
   {
       // any of R/G/B is on (>=250) -> it's one of your keys (red/green/blue/yellow/pink/cyan/white)
       return On(baseKey.R) || On(baseKey.G) || On(baseKey.B);
   }

   private static Rgba32 GuessKeyBaseFromPixel(Rgba32 p)
   {
       // Decide which channels are intended "on" based on dominance.
       // Works for:
       //  (k,0,0) -> red
       //  (0,k,0) -> green
       //  (0,0,k) -> blue
       //  (k,k,0) -> yellow
       //  (k,0,k) -> magenta/pink
       //  (0,k,k) -> cyan
       //  (k,k,k) -> white/gray (probably you don't want to replace those)
       int r = p.R, g = p.G, b = p.B;

       int max = Math.Max(r, Math.Max(g, b));
       if (max < 10) return new Rgba32(0, 0, 0, 255); // near-black

       // Consider channel "on" if it's close to the max (handles yellow/pink/cyan)
       // tighten/loosen this ratio if needed:
       const double closeToMax = 0.90;

       bool rOn = r >= max * closeToMax;
       bool gOn = g >= max * closeToMax;
       bool bOn = b >= max * closeToMax;

       return new Rgba32(
           (byte)(rOn ? 255 : 0),
           (byte)(gOn ? 255 : 0),
           (byte)(bOn ? 255 : 0),
           255);
   }


    public static Rgba32 AlphaBlend(Rgba32 over, Rgba32 under)
    {
        // Get the alpha values as floats (0.0 to 1.0)
        float alphaOver = over.A / 255f;
        float alphaUnder = under.A / 255f;

        // Calculate the resulting alpha
        float alphaResult = alphaOver + alphaUnder * (1 - alphaOver);

        // If the resulting alpha is 0, return transparent
        if (alphaResult == 0)
        {
            return new Rgba32(0, 0, 0, 0);
        }

        // Calculate the resulting color channels
        byte r = (byte)((over.R * alphaOver + under.R * alphaUnder * (1 - alphaOver)) / alphaResult);
        byte g = (byte)((over.G * alphaOver + under.G * alphaUnder * (1 - alphaOver)) / alphaResult);
        byte b = (byte)((over.B * alphaOver + under.B * alphaUnder * (1 - alphaOver)) / alphaResult);
        byte a = (byte)(alphaResult * 255);

        // Return the blended color
        return new Rgba32(r, g, b, a);
    }

    public static void Brush(Image<Rgba32> imageToFill, int imageX, int imageY, Rgba32 fillColor, int size)
    {
        for (int x = imageX - size; x <= imageX + size; x++)
        {
            for (int y = imageY - size; y <= imageY + size; y++)
            {
                if (x >= 0 && x < imageToFill.Width && y >= 0 && y < imageToFill.Height)
                {
                    if (fillColor.A == 0) // eraser
                    {
                        imageToFill[x, y] = new Rgba32(0, 0, 0, 0);
                    }
                    else
                    {
                        imageToFill[x, y] = AlphaBlend(imageToFill[x, y],fillColor );
                    }
                }
            }
        }
    }

    public static int Analyze(Image<Rgba32> image)
    {
        int w = image.Width;
        int h = image.Height;

        // Flood fill from (0,0) on a temporary mask to mark "outside" empty pixels.
        var reachable = new bool[w, h];
        var queue = new Queue<(int x, int y)>();

        if (image[0, 0].A == 0)
        {
            reachable[0, 0] = true;
            queue.Enqueue((0, 0));
        }

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            TryEnqueue(x + 1, y);
            TryEnqueue(x - 1, y);
            TryEnqueue(x, y + 1);
            TryEnqueue(x, y - 1);
        }

        void TryEnqueue(int nx, int ny)
        {
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) return;
            if (reachable[nx, ny]) return;
            if (image[nx, ny].A != 0) return;
            reachable[nx, ny] = true;
            queue.Enqueue((nx, ny));
        }

        // Count empty pixels that aren't reachable from the corner -> uncolored holes.
        int count = 0;
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (image[x, y].A == 0 && !reachable[x, y])
                {
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Gets the bounding rectangle of all non-transparent pixels in the image
    /// </summary>
    public static Rectangle GetTrimBounds(Image<Rgba32> image)
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

        bool foundContent = false;

        // Scan the entire image to find the bounds of non-transparent pixels
        for (int x = 0; x < image.Width; x++)
        {
            for (int y = 0; y < image.Height; y++)
            {
                // Check if pixel is not fully transparent
                if (image[x, y].A > 0)
                {
                    foundContent = true;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        // If no content found, return empty rectangle
        if (!foundContent)
        {
            return new Rectangle(0, 0, 0, 0);
        }

        // Return the bounding rectangle (inclusive bounds, so add 1 to width/height)
        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    public static Image<Rgba32> Crop(Image<Rgba32> image, int x, int y, int width, int height)
    {
        // Define the rectangle for cropping
        var cropRectangle = new Rectangle(x, y, width, height);

        // Clone and crop the image to avoid modifying the original
        Image<Rgba32> croppedImage = image.Clone(ctx => ctx.Crop(cropRectangle));

        return croppedImage;
    }

    public static void LineVertical(Image<Rgba32> image, int p0, Rgba32 color)
    {
        for (int y = 0; y < image.Height; y++)
        {
            image[p0, y] = color;
        }
    }

    public static void LineHorizontal(Image<Rgba32> image, int p0, Rgba32 color)
    {
        for (int x = 0; x < image.Width; x++)
        {
            image[x, p0] = color;
        }
    }

    public static void DrawRectangle(Image<Rgba32> pictureBoxImage, int x, int y, int w, int h, Rgba32 red)
    {
        for (int i = x; i < x + w; i++)
        {
            if (i >= 0 && i < pictureBoxImage.Width)
            {
                if (y >= 0 && y < pictureBoxImage.Height)
                    pictureBoxImage[i, y] = red;
                if (y + h >= 0 && y + h < pictureBoxImage.Height)
                    pictureBoxImage[i, y + h] = red;
            }
        }

        for (int i = y; i < y + h; i++)
        {
            if (i >= 0 && i < pictureBoxImage.Height)
            {
                if (x >= 0 && x < pictureBoxImage.Width)
                    pictureBoxImage[x, i] = red;
                if (x + w >= 0 && x + w < pictureBoxImage.Width)
                    pictureBoxImage[x + w, i] = red;
            }
        }
    }

    public static void SDF(Image<Rgba32> loadedImage)
    {
        // --- tweak this ---
        float blurSigma = 5.5f; // "a little" blur (try 1.5f .. 6f)

        int w = loadedImage.Width;
        int h = loadedImage.Height;

        // 1) Create blurred copy (shadow base)
        using Image<Rgba32> blurred = loadedImage.Clone(ctx => ctx.GaussianBlur(blurSigma));

        // 2) Turn blurred copy into black while preserving alpha
        blurred.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    byte a = row[x].A;
                    row[x] = new Rgba32(0, 0, 0, a); // black, same alpha
                }
            }
        });

        // 3) Compose: black blurred behind + original on top
        Image<Rgba32> result = new Image<Rgba32>(w, h);
        result.Mutate(ctx =>
        {
            ctx.DrawImage(blurred, 1f); // behind
            ctx.DrawImage(loadedImage, 1f); // on top
        });

        // If you want to "rewrite it into loadedImage" (in-place style),
        // you can do:
        loadedImage.Mutate(ctx => ctx.DrawImage(result, 1f));
        // But usually returning result is cleaner.
        // return result;
    }
}