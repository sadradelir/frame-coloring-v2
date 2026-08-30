using System.Drawing.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FrameColoringV2.UI;

/// <summary>Converts ImageSharp frames into GDI+ bitmaps the canvas can blit.</summary>
public static class BitmapBridge
{
    /// <summary>Returns a bitmap of the same size as the frame, reusing <paramref name="existing"/> when possible.</summary>
    public static Bitmap ToBitmap(Image<Rgba32> source, Bitmap? existing)
    {
        Bitmap target = existing != null && existing.Width == source.Width && existing.Height == source.Height
            ? existing
            : new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

        if (!ReferenceEquals(target, existing)) existing?.Dispose();

        CopyInto(source, target);
        return target;
    }

    private static void CopyInto(Image<Rgba32> source, Bitmap target)
    {
        BitmapData data = target.LockBits(
            new System.Drawing.Rectangle(0, 0, target.Width, target.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            IntPtr scan0 = data.Scan0;
            int stride = data.Stride;

            source.ProcessPixelRows(accessor =>
            {
                unsafe
                {
                    var basePointer = (byte*)scan0;
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        Span<Rgba32> row = accessor.GetRowSpan(y);
                        byte* destination = basePointer + (long)y * stride;

                        for (int x = 0; x < row.Length; x++)
                        {
                            Rgba32 pixel = row[x];
                            int offset = x * 4;
                            destination[offset + 0] = pixel.B;
                            destination[offset + 1] = pixel.G;
                            destination[offset + 2] = pixel.R;
                            destination[offset + 3] = pixel.A;
                        }
                    }
                }
            });
        }
        finally
        {
            target.UnlockBits(data);
        }
    }
}
