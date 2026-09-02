using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FrameColoringV2.Imaging;

/// <summary>
/// Rewrites the alpha channel of a frame as a signed distance field:
///
///     alpha = saturate(0.5 + signedDistance / (2 * spread))
///
/// with the distance in pixels, positive inside the silhouette. RGB is never touched, so the
/// key colour data and the black outline survive exactly as they are.
///
/// The silhouette is taken where the existing alpha saturates (alpha >= threshold), not at the
/// midpoint: frames baked for a selection halo ramp up to 1 at the edge, so the 0.5 crossing
/// sits about 1.7px outside the real edge.
///
/// A distance transform of a binary mask is only accurate to ±0.5px while the 8 bit encoding
/// resolves 2*spread/256 (0.19px at spread 24), so the mask is supersampled before the
/// transform and the result divided back down: 4x gets to ±0.125px, under the storage
/// resolution.
/// </summary>
public static class DistanceFieldBaker
{
    public const float DefaultSpread = 24f;
    public const byte DefaultSolidThreshold = 250;
    public const int DefaultSupersample = 4;
    public const int MaxSupersample = 4;

    private const float Infinity = 1e20f;
    private const float SeedOffsetScale = 0.75f;

    public static void Bake(Image<Rgba32> image, float spread = DefaultSpread,
        byte solidThreshold = DefaultSolidThreshold, int supersample = DefaultSupersample,
        bool subPixelEdge = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        spread = Math.Max(0.5f, spread);
        int scale = Math.Clamp(supersample, 1, MaxSupersample);

        int width = image.Width;
        int height = image.Height;
        int sampleWidth = width * scale;
        int sampleHeight = height * scale;

        // 1. Binary mask at the supersampled resolution.
        var solid = new bool[sampleWidth * sampleHeight];
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].A < solidThreshold) continue;

                    for (int sy = 0; sy < scale; sy++)
                    {
                        int rowStart = (y * scale + sy) * sampleWidth + x * scale;
                        for (int sx = 0; sx < scale; sx++) solid[rowStart + sx] = true;
                    }
                }
            }
        });

        cancellationToken.ThrowIfCancellationRequested();

        // 2. Signed distance on the mask grid.
        float[] signed = subPixelEdge
            ? SubPixelSignedDistance(solid, sampleWidth, sampleHeight, cancellationToken)
            : BinarySignedDistance(solid, sampleWidth, sampleHeight, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        // 3. Average each block back down to one source pixel.
        var sum = new float[width * height];
        Parallel.For(0, height, y =>
        {
            for (int sy = 0; sy < scale; sy++)
            {
                int rowStart = (y * scale + sy) * sampleWidth;
                for (int sx = 0; sx < sampleWidth; sx++)
                {
                    sum[y * width + sx / scale] += signed[rowStart + sx];
                }
            }
        });

        // 4. Encode. Averaging the block converts grid units back to pixels.
        float perPixel = 1f / (scale * scale * scale);
        float encode = 1f / (2f * spread);

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    float distance = sum[y * width + x] * perPixel;
                    float alpha = 0.5f + distance * encode;
                    row[x].A = (byte)Math.Clamp(MathF.Round(alpha * 255f), 0, 255);
                }
            }
        });
    }

    /// <summary>
    /// Signed distance straight from the binary mask: exact for that mask, but the edge itself
    /// is only known to half a cell, so the field carries the mask's staircase.
    /// </summary>
    private static float[] BinarySignedDistance(bool[] solid, int width, int height,
        CancellationToken cancellationToken)
    {
        var signed = new float[solid.Length];
        var squared = new float[solid.Length];

        // Empty cells take the distance to the nearest solid one, negative.
        SquaredEuclideanDistance(solid, seed: true, width, height, squared, cancellationToken);
        Parallel.For(0, solid.Length, i =>
        {
            if (!solid[i]) signed[i] = -(MathF.Sqrt(squared[i]) - 0.5f);
        });

        // Solid cells take the distance to the nearest empty one, positive.
        SquaredEuclideanDistance(solid, seed: false, width, height, squared, cancellationToken);
        Parallel.For(0, solid.Length, i =>
        {
            if (solid[i]) signed[i] = MathF.Sqrt(squared[i]) - 0.5f;
        });

        return signed;
    }

    /// <summary>
    /// Signed distance to a sub-pixel reconstruction of the edge. The mask is smoothed into a
    /// coverage-like field whose 0.5 isoline lands between cell centres; cells on the edge are
    /// seeded with the exact offset to that isoline, and the nearest point on it is then
    /// propagated outwards by dead reckoning (Grevera 2004), so distant cells inherit a real
    /// boundary point instead of a rounded cell count.
    /// </summary>
    private static float[] SubPixelSignedDistance(bool[] solid, int width, int height,
        CancellationToken cancellationToken)
    {
        int count = width * height;

        // Separable [1 2 1] / 4 smoothing of the mask.
        var smoothed = new float[count];
        var temporary = new float[count];

        Parallel.For(0, height, y =>
        {
            int rowStart = y * width;
            for (int x = 0; x < width; x++)
            {
                float left = solid[rowStart + Math.Max(0, x - 1)] ? 1f : 0f;
                float middle = solid[rowStart + x] ? 1f : 0f;
                float right = solid[rowStart + Math.Min(width - 1, x + 1)] ? 1f : 0f;
                temporary[rowStart + x] = (left + 2f * middle + right) * 0.25f;
            }
        });

        Parallel.For(0, height, y =>
        {
            int up = Math.Max(0, y - 1) * width;
            int down = Math.Min(height - 1, y + 1) * width;
            int rowStart = y * width;

            for (int x = 0; x < width; x++)
            {
                smoothed[rowStart + x] = (temporary[up + x] + 2f * temporary[rowStart + x] + temporary[down + x]) * 0.25f;
            }
        });

        cancellationToken.ThrowIfCancellationRequested();

        var distance = new float[count];
        var nearestX = new float[count];
        var nearestY = new float[count];
        Array.Fill(distance, Infinity);

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (!IsBoundary(solid, width, height, x, y)) continue;

                float gx = (Sample(smoothed, width, height, x + 1, y) - Sample(smoothed, width, height, x - 1, y)) * 0.5f;
                float gy = (Sample(smoothed, width, height, x, y + 1) - Sample(smoothed, width, height, x, y - 1)) * 0.5f;
                float length = MathF.Sqrt(gx * gx + gy * gy);

                // A flat gradient means the smoothing could not place the edge; fall back to the
                // cell boundary, half a cell away.
                // The isoline can only be about a cell away from a boundary cell; clamping keeps
                // a flat local gradient from throwing the seed point across the image.
                // The 3x3 tent reports ±0.667 for an axis aligned edge that really sits half a
                // cell away, so the raw estimate is scaled by 0.5/0.667 = 0.75. Measured against
                // exact edges at 45°, 20° and 7° and against a circle, that factor is the best
                // one across all of them.
                float offset = length < 1e-5f
                    ? (solid[index] ? 0.5f : -0.5f)
                    : Math.Clamp((smoothed[index] - 0.5f) / length * SeedOffsetScale, -1f, 1f);

                float normalX = length < 1e-5f ? 0f : gx / length;
                float normalY = length < 1e-5f ? 0f : gy / length;

                distance[index] = Math.Abs(offset);
                nearestX[index] = x - offset * normalX;
                nearestY[index] = y - offset * normalY;
            }
        });

        cancellationToken.ThrowIfCancellationRequested();

        DeadReckoning(distance, nearestX, nearestY, width, height);

        var signed = new float[count];
        Parallel.For(0, count, i => signed[i] = solid[i] ? distance[i] : -distance[i]);

        return signed;
    }

    private static bool IsBoundary(bool[] solid, int width, int height, int x, int y)
    {
        bool centre = solid[y * width + x];

        if (x > 0 && solid[y * width + x - 1] != centre) return true;
        if (x < width - 1 && solid[y * width + x + 1] != centre) return true;
        if (y > 0 && solid[(y - 1) * width + x] != centre) return true;
        if (y < height - 1 && solid[(y + 1) * width + x] != centre) return true;

        return false;
    }

    private static float Sample(float[] field, int width, int height, int x, int y) =>
        field[Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)];

    /// <summary>
    /// Sweeps that propagate the nearest boundary point. The distance is recomputed from that
    /// point every time instead of being accumulated, which is what keeps the far field accurate.
    /// A single forward/backward pair can only carry a point along monotone paths, so cells far
    /// from a curved edge can end up with a point that is not the nearest one; repeating the pair
    /// until nothing improves fixes that, and it converges in two or three rounds.
    /// </summary>
    private static void DeadReckoning(float[] distance, float[] nearestX, float[] nearestY, int width, int height)
    {
        const int maxRounds = 8;
        for (int round = 0; round < maxRounds; round++)
        {
            if (!DeadReckoningPass(distance, nearestX, nearestY, width, height)) return;
        }
    }

    private static bool DeadReckoningPass(float[] distance, float[] nearestX, float[] nearestY, int width, int height)
    {
        bool improved = false;

        void Relax(int index, int neighbour, int x, int y)
        {
            if (distance[neighbour] >= Infinity) return;

            float dx = x - nearestX[neighbour];
            float dy = y - nearestY[neighbour];
            float candidate = MathF.Sqrt(dx * dx + dy * dy);

            if (candidate >= distance[index] - 1e-4f) return;

            distance[index] = candidate;
            nearestX[index] = nearestX[neighbour];
            nearestY[index] = nearestY[neighbour];
            improved = true;
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;

                if (y > 0)
                {
                    if (x > 0) Relax(index, index - width - 1, x, y);
                    Relax(index, index - width, x, y);
                    if (x < width - 1) Relax(index, index - width + 1, x, y);
                }

                if (x > 0) Relax(index, index - 1, x, y);
            }
        }

        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = width - 1; x >= 0; x--)
            {
                int index = y * width + x;

                if (y < height - 1)
                {
                    if (x < width - 1) Relax(index, index + width + 1, x, y);
                    Relax(index, index + width, x, y);
                    if (x > 0) Relax(index, index + width - 1, x, y);
                }

                if (x < width - 1) Relax(index, index + 1, x, y);
            }
        }

        return improved;
    }

    /// <summary>
    /// Felzenszwalb &amp; Huttenlocher's exact O(n) transform: squared distance from every cell to
    /// the nearest cell whose mask value is <paramref name="seed"/>.
    /// </summary>
    private static void SquaredEuclideanDistance(bool[] mask, bool seed, int width, int height,
        float[] output, CancellationToken cancellationToken)
    {
        for (int i = 0; i < output.Length; i++) output[i] = mask[i] == seed ? 0f : Infinity;

        cancellationToken.ThrowIfCancellationRequested();

        // Columns first, then rows: the 1D transform is separable.
        Parallel.For(0, width, () => new Workspace(Math.Max(width, height)), (x, _, workspace) =>
        {
            for (int y = 0; y < height; y++) workspace.Source[y] = output[y * width + x];

            Transform1D(workspace, height);

            for (int y = 0; y < height; y++) output[y * width + x] = workspace.Result[y];
            return workspace;
        }, _ => { });

        cancellationToken.ThrowIfCancellationRequested();

        Parallel.For(0, height, () => new Workspace(Math.Max(width, height)), (y, _, workspace) =>
        {
            int rowStart = y * width;
            for (int x = 0; x < width; x++) workspace.Source[x] = output[rowStart + x];

            Transform1D(workspace, width);

            for (int x = 0; x < width; x++) output[rowStart + x] = workspace.Result[x];
            return workspace;
        }, _ => { });
    }

    private static void Transform1D(Workspace workspace, int n)
    {
        float[] f = workspace.Source;
        float[] d = workspace.Result;
        int[] v = workspace.Hulls;
        float[] z = workspace.Boundaries;

        int k = 0;
        v[0] = 0;
        z[0] = -Infinity;
        z[1] = Infinity;

        for (int q = 1; q < n; q++)
        {
            // Intersection of the parabola at q with the one currently on top of the hull.
            float s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
            while (s <= z[k])
            {
                k--;
                s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
            }

            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = Infinity;
        }

        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            float dx = q - v[k];
            d[q] = dx * dx + f[v[k]];
        }
    }

    private sealed class Workspace
    {
        public Workspace(int size)
        {
            Source = new float[size];
            Result = new float[size];
            Hulls = new int[size];
            Boundaries = new float[size + 1];
        }

        public float[] Source { get; }
        public float[] Result { get; }
        public int[] Hulls { get; }
        public float[] Boundaries { get; }
    }

    // ------------------------------------------------------------ verification

    /// <summary>
    /// Checks a baked frame: the alpha should read 128 on the silhouette, 0 at
    /// <paramref name="spread"/> pixels outside, 255 at <paramref name="spread"/> pixels inside,
    /// and rise monotonically by 255/(2*spread) per pixel in between.
    /// </summary>
    public static string Verify(Image<Rgba32> image, float spread = DefaultSpread)
    {
        ArgumentNullException.ThrowIfNull(image);

        int width = image.Width;
        int height = image.Height;
        float expectedStep = 255f / (2f * spread);

        // Pick the scanline whose body is widest while still leaving a full band on both
        // sides, so the check is not run against a run that is clipped by the image border.
        int bestRow = -1;
        int bestRun = 0;
        int edge = 0;
        int spreadPixels = (int)MathF.Round(spread);

        for (int y = 0; y < height; y++)
        {
            int run = 0;
            int start = 0;

            for (int x = 0; x <= width; x++)
            {
                bool inside = x < width && image[x, y].A >= 128;

                if (inside)
                {
                    if (run == 0) start = x;
                    run++;
                    continue;
                }

                bool roomOutside = start >= spreadPixels;
                bool roomInside = run >= spreadPixels;

                // A row near the top or bottom of the body measures the distance to that edge
                // instead, and never reaches full white one spread in.
                bool deepEnough = roomInside && image[start + spreadPixels, y].A == 255;

                if (run > bestRun && roomOutside && deepEnough)
                {
                    bestRun = run;
                    bestRow = y;
                    edge = start;
                }

                run = 0;
            }
        }

        if (bestRow < 0) return "No scanline has a body wide enough to check against this spread.";

        int from = Math.Max(0, edge - spreadPixels);
        int to = Math.Min(width - 1, edge + spreadPixels);

        var report = new StringBuilder();
        report.AppendLine($"Scanline y = {bestRow}, silhouette edge at x = {edge}, body {bestRun}px wide.");
        report.AppendLine();

        // Where the field actually crosses 0.5, interpolated between the two samples.
        int before = image[Math.Max(0, edge - 1), bestRow].A;
        int after = image[edge, bestRow].A;
        if (after != before)
        {
            float crossing = edge - 1 + (128f - before) / (after - before);
            report.AppendLine($"0.5 crossing at x = {crossing:0.00} (the mask boundary sits at {edge - 0.5f:0.00})");
            report.AppendLine();
        }

        report.AppendLine($"alpha at the edge      (x={edge}): {image[edge, bestRow].A}   expected ~128");
        report.AppendLine($"alpha {spreadPixels}px outside  (x={from}): {image[from, bestRow].A}   expected ~0");
        report.AppendLine($"alpha {spreadPixels}px inside   (x={to}): {image[to, bestRow].A}   expected ~255");
        report.AppendLine();

        int monotonicBreaks = 0;
        int stepOutliers = 0;
        int stepsChecked = 0;
        float stepSum = 0;
        float worstDeviation = -1;
        float worstStep = 0;
        int worstAt = -1;

        for (int x = from; x < to; x++)
        {
            int current = image[x, bestRow].A;
            int next = image[x + 1, bestRow].A;
            if (next < current) monotonicBreaks++;

            // Clamped ends carry no gradient, so only the unsaturated part is checked.
            if (current is 0 or 255 || next is 0 or 255) continue;

            float step = next - current;
            stepsChecked++;
            stepSum += step;

            float deviation = Math.Abs(step - expectedStep);
            if (deviation > 1f) stepOutliers++;

            if (deviation > worstDeviation)
            {
                worstDeviation = deviation;
                worstStep = step;
                worstAt = x;
            }
        }

        report.AppendLine($"across x = {from}..{to} ({stepsChecked} unclamped step(s))");
        report.AppendLine($"  mean step:            {(stepsChecked == 0 ? 0 : stepSum / stepsChecked):0.00} (expected {expectedStep:0.00})");
        report.AppendLine($"  monotonic:            {(monotonicBreaks == 0 ? "yes" : $"no, {monotonicBreaks} step(s) go backwards")}");
        report.AppendLine($"  step {expectedStep:0.0} ± 1 per pixel: " +
                          (stepOutliers == 0
                              ? "yes"
                              : $"no, {stepOutliers} outlier(s), worst {worstStep:0.0} at x = {worstAt}"));

        if (stepOutliers == 0 && stepsChecked > 0)
        {
            report.AppendLine($"  worst step:           {worstStep:0.00} at x = {worstAt}");
        }

        return report.ToString();
    }
}
