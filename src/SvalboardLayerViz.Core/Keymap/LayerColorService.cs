namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Generates visually distinct colors for each layer.
/// Uses HLS color space for perceptually smooth gradients (matching Skim's approach).
/// Supports device-provided HSV colors or algorithmic generation from layer index.
/// </summary>
public static class LayerColorService
{
    /// <summary>
    /// Gets colors for a layer. If device HSV is provided, converts it; otherwise generates
    /// from layer index using evenly-spaced hues.
    /// </summary>
    public static LayerColors GetLayerColors(int layerIndex, int totalLayers,
        byte? deviceHue = null, byte? deviceSat = null, byte? deviceVal = null)
    {
        double h, s, l;

        if (deviceHue.HasValue && deviceSat.HasValue && deviceVal.HasValue)
        {
            // Convert QMK HSV (H: 0-255 → 0-360, S: 0-255 → 0-1, V: 0-255 → 0-1) to HLS
            var hDeg = deviceHue.Value / 255.0 * 360.0;
            var sNorm = deviceSat.Value / 255.0;
            var vNorm = deviceVal.Value / 255.0;
            (h, s, l) = HsvToHsl(hDeg, sNorm, vNorm);
        }
        else
        {
            // Algorithmic: evenly space hues across the color wheel
            var count = Math.Max(totalLayers, 1);
            h = (layerIndex * 360.0 / count + 210) % 360; // Start at blue (210°)
            s = 0.55;
            l = 0.50;
        }

        return new LayerColors(
            Background: HslToHex(h, s * 0.6, l * 0.42),
            Border: HslToHex(h, s * 0.5, l * 0.65),
            Accent: HslToHex(h, s * 0.8, l * 0.70),
            TransparentBackground: HslToHex(h, s * 0.3, l * 0.28),
            TransparentBorder: HslToHex(h, s * 0.25, l * 0.50)
        );
    }

    internal static (double H, double S, double L) HsvToHsl(double h, double s, double v)
    {
        var l = v * (1 - s / 2);
        var sl = (l is > 0 and < 1) ? (v - l) / Math.Min(l, 1 - l) : 0;
        return (h, sl, l);
    }

    internal static string HslToHex(double h, double s, double l)
    {
        var (r, g, b) = HslToRgb(h, s, l);
        var ri = (int)Math.Round(r * 255);
        var gi = (int)Math.Round(g * 255);
        var bi = (int)Math.Round(b * 255);
        return $"#{ri:X2}{gi:X2}{bi:X2}";
    }

    internal static (double R, double G, double B) HslToRgb(double h, double s, double l)
    {
        if (s == 0)
            return (l, l, l);

        h /= 360.0;
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;

        return (
            HueToRgb(p, q, h + 1.0 / 3),
            HueToRgb(p, q, h),
            HueToRgb(p, q, h - 1.0 / 3)
        );
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}

/// <summary>
/// Color set for a layer, with variants for normal and transparent keys.
/// </summary>
public record LayerColors(
    string Background,
    string Border,
    string Accent,
    string TransparentBackground,
    string TransparentBorder
);
