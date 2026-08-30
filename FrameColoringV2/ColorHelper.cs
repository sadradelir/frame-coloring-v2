using System.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;
using Color = SixLabors.ImageSharp.Color;

namespace FrameColoringV2;

public static class ColorHelper
{
    
     // ---- Key-color helpers (for your 0/255 palette) ----

    private static bool On(byte v) => v >= 250; // treat 255-ish as "on"

    private static byte ComputeK(Rgba32 pixel, Rgba32 sourceBase)
    {
        int sum = 0;
        int count = 0;

        if (On(sourceBase.R)) { sum += pixel.R; count++; }
        if (On(sourceBase.G)) { sum += pixel.G; count++; }
        if (On(sourceBase.B)) { sum += pixel.B; count++; }

        return count == 0 ? (byte)0 : (byte)(sum / count); // 0..255
    }

    private static Rgba32 ApplyKToTarget(byte k, Rgba32 targetBase, byte alpha)
    {
        byte r = On(targetBase.R) ? k : (byte)0;
        byte g = On(targetBase.G) ? k : (byte)0;
        byte b = On(targetBase.B) ? k : (byte)0;
        return new Rgba32(r, g, b, alpha);
    }

    // Rotate the key-color shading from sourceBase -> targetBase (preserve dark edges)
    public static Rgba32 RotateKeyColorKeepDarkening(Rgba32 pixel, Rgba32 sourceBase, Rgba32 targetBase)
    {
        byte k = ComputeK(pixel, sourceBase);
        return ApplyKToTarget(k, targetBase, pixel.A);
    }

    // Membership test: include darkened pixels of sourceBase, exclude black lineart and other colors
    // Tweak offMax/onMin for your sprites if needed.
    public static bool IsDarkenedKeyColor(Rgba32 pixel, Rgba32 sourceBase, int offMax = 25, int onMin = 10)
    {
        int maxPx = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
        if (maxPx < onMin) return false; // near-black, treat as lineart / not fill

        bool srcR = On(sourceBase.R);
        bool srcG = On(sourceBase.G);
        bool srcB = On(sourceBase.B);

        // "off" channels should be small (avoid bleeding into other regions)
        if (!srcR && pixel.R > offMax) return false;
        if (!srcG && pixel.G > offMax) return false;
        if (!srcB && pixel.B > offMax) return false;

        // at least one "on" channel should be present enough
        if (srcR && pixel.R < onMin) return false;
        if (srcG && pixel.G < onMin) return false;
        if (srcB && pixel.B < onMin) return false;

        return true;
    }

}