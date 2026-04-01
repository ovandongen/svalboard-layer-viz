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
        byte? deviceHue = null, byte? deviceSat = null, byte? deviceVal = null,
        string? userHexColor = null)
    {
        double h, s, l;
        bool isUserColor = false;

        if (userHexColor is not null)
        {
            (h, s, l) = HexToHsl(userHexColor);
            isUserColor = true;
        }
        else if (deviceHue.HasValue && deviceSat.HasValue && deviceVal.HasValue)
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

        return BuildColors(h, s, l, isUserColor);
    }

    /// <summary>
    /// Gets colors from a user-specified hex color string.
    /// </summary>
    public static LayerColors GetLayerColors(string userHexColor)
    {
        var (h, s, l) = HexToHsl(userHexColor);
        return BuildColors(h, s, l, isUserColor: true);
    }

    private static LayerColors BuildColors(double h, double s, double l, bool isUserColor = false)
    {
        string bg, border, accent, transBg, transBorder;

        if (isUserColor)
        {
            // User-picked colors: keep close to what they chose
            bg = HslToHex(h, s, l);
            border = HslToHex(h, s * 0.8, Math.Min(l + 0.15, 1.0));
            accent = HslToHex(h, s, Math.Min(l + 0.1, 1.0));
            transBg = HslToHex(h, s * 0.4, l * 0.6);
            transBorder = HslToHex(h, s * 0.35, Math.Min(l * 0.6 + 0.2, 1.0));
        }
        else
        {
            // Algorithmic: darker tinted keys for default dark theme look
            bg = HslToHex(h, s * 0.6, l * 0.42);
            border = HslToHex(h, s * 0.5, l * 0.65);
            accent = HslToHex(h, s * 0.8, l * 0.70);
            transBg = HslToHex(h, s * 0.3, l * 0.28);
            transBorder = HslToHex(h, s * 0.25, l * 0.50);
        }

        // Auto text color based on background luminance
        var textColor = GetContrastTextColor(bg);
        var transTextColor = GetContrastTextColor(transBg);

        return new LayerColors(bg, border, accent, transBg, transBorder, textColor, transTextColor);
    }

    /// <summary>
    /// Returns white or black text color based on background luminance.
    /// Uses W3C relative luminance formula.
    /// </summary>
    internal static string GetContrastTextColor(string hexBackground)
    {
        var hex = hexBackground.TrimStart('#');
        if (hex.Length != 6) return "#FFFFFF";

        var r = Convert.ToInt32(hex[..2], 16) / 255.0;
        var g = Convert.ToInt32(hex[2..4], 16) / 255.0;
        var b = Convert.ToInt32(hex[4..6], 16) / 255.0;

        // Relative luminance (sRGB)
        var luminance = 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);

        return luminance > 0.4 ? "#1E1E2E" : "#FFFFFF";
    }

    private static double Linearize(double c) =>
        c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    internal static (double H, double S, double L) HexToHsl(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6)
            throw new ArgumentException($"Invalid hex color: #{hex}");

        var r = Convert.ToInt32(hex[..2], 16) / 255.0;
        var g = Convert.ToInt32(hex[2..4], 16) / 255.0;
        var b = Convert.ToInt32(hex[4..6], 16) / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;

        if (max == min)
            return (0, 0, l);

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);

        double hue;
        if (max == r)
            hue = ((g - b) / d + (g < b ? 6 : 0)) / 6;
        else if (max == g)
            hue = ((b - r) / d + 2) / 6;
        else
            hue = ((r - g) / d + 4) / 6;

        return (hue * 360, s, l);
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
    string TransparentBorder,
    string TextColor = "#FFFFFF",
    string TransparentTextColor = "#FFFFFF"
);
