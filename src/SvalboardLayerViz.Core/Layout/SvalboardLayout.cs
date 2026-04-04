namespace SvalboardLayerViz.Core.Layout;

/// <summary>
/// Defines the physical key positions for the Svalboard.
///
/// Coordinates are taken directly from keybard-ng/src/constants/svalboard-layout.ts.
/// Positions are in layout units; 1 unit = 60 px (UNIT_SIZE from keybard-ng).
///
/// Matrix layout: 10 rows × 6 cols.
///   Row 0        — left inner modifier keys (6 keys)
///   Rows 1–4     — left finger clusters: Index, Middle, Ring, Pinky (5 keys each)
///   Row 5        — thumb cluster (6 keys, shared row)
///   Rows 6–9     — right finger clusters: Index, Middle, Ring, Pinky (5 keys each)
///
/// Column pattern for finger clusters (rows 1–4, 6–9):
///   col 0 = South  col 1 = East  col 2 = Down  col 3 = North  col 4 = West
/// </summary>
public static class SvalboardLayout
{
    /// <summary>Pixels per layout unit. All layout coordinates use this scale factor.</summary>
    public const double Scale = 60.0;

    /// <summary>X origin for right-hand keys (subtracted to get hand-relative coordinates).</summary>
    public const double RightHandOriginX = 12.5;

    /// <summary>Width of each hand in layout units (max key X + width - min key X).</summary>
    public const double HandWidth = 11.8;

    /// <summary>Height of each hand in layout units.</summary>
    public const double HandHeight = 7.0;

    /// <summary>Horizontal gap between hands in layout units.</summary>
    public const double HandGap = 0.7;

    /// <summary>Returns true if the given row belongs to the right hand (rows 5-9: R-Thumb + R-fingers).</summary>
    public static bool IsRightHand(int row) => row >= 5;

    private static readonly KeyPosition[] _positions =
    [
        // Row 0 — left inner modifiers
        new(0, 0, 10.8, 6.0, "L-Mod", "Ctrl"),
        new(0, 1, 10.8, 5.0, "L-Mod", "Tab"),
        new(0, 2,  9.5, 6.0, "L-Mod", "Shift"),
        new(0, 3,  8.2, 5.0, "L-Mod", "Enter"),
        new(0, 4,  8.2, 6.0, "L-Mod", "Fn"),
        new(0, 5,  9.5, 5.0, "L-Mod", "Caps"),

        // Row 1 — L-Index
        new(1, 0,  9.5, 3.5, "L-Index", "South"),
        new(1, 1, 10.5, 2.5, "L-Index", "East"),
        new(1, 2,  9.5, 2.5, "L-Index", "Down"),
        new(1, 3,  9.5, 1.5, "L-Index", "North"),
        new(1, 4,  8.5, 2.5, "L-Index", "West"),

        // Row 2 — L-Middle
        new(2, 0,  7.0, 2.0, "L-Middle", "South"),
        new(2, 1,  8.0, 1.0, "L-Middle", "East"),
        new(2, 2,  7.0, 1.0, "L-Middle", "Down"),
        new(2, 3,  7.0, 0.0, "L-Middle", "North"),
        new(2, 4,  6.0, 1.0, "L-Middle", "West"),

        // Row 3 — L-Ring
        new(3, 0,  3.5, 2.0, "L-Ring", "South"),
        new(3, 1,  4.5, 1.0, "L-Ring", "East"),
        new(3, 2,  3.5, 1.0, "L-Ring", "Down"),
        new(3, 3,  3.5, 0.0, "L-Ring", "North"),
        new(3, 4,  2.5, 1.0, "L-Ring", "West"),

        // Row 4 — L-Pinky
        new(4, 0,  1.0, 3.5, "L-Pinky", "South"),
        new(4, 1,  2.0, 2.5, "L-Pinky", "East"),
        new(4, 2,  1.0, 2.5, "L-Pinky", "Down"),
        new(4, 3,  1.0, 1.5, "L-Pinky", "North"),
        new(4, 4,  0.0, 2.5, "L-Pinky", "West"),

        // Row 5 — thumb cluster (both hands share this row)
        new(5, 0, 12.5, 6.0, "L-Thumb", "Inner"),
        new(5, 1, 12.5, 5.0, "L-Thumb", "Outer"),
        new(5, 2, 13.8, 6.0, "L-Thumb", "Center"),
        new(5, 3, 15.1, 5.0, "R-Thumb", "Outer"),
        new(5, 4, 15.1, 6.0, "R-Thumb", "Inner"),
        new(5, 5, 13.8, 5.0, "R-Thumb", "Center"),

        // Row 6 — R-Index
        new(6, 0, 13.8, 3.5, "R-Index", "South"),
        new(6, 1, 14.8, 2.5, "R-Index", "East"),
        new(6, 2, 13.8, 2.5, "R-Index", "Down"),
        new(6, 3, 13.8, 1.5, "R-Index", "North"),
        new(6, 4, 12.8, 2.5, "R-Index", "West"),

        // Row 7 — R-Middle
        new(7, 0, 16.3, 2.0, "R-Middle", "South"),
        new(7, 1, 17.3, 1.0, "R-Middle", "East"),
        new(7, 2, 16.3, 1.0, "R-Middle", "Down"),
        new(7, 3, 16.3, 0.0, "R-Middle", "North"),
        new(7, 4, 15.3, 1.0, "R-Middle", "West"),

        // Row 8 — R-Ring
        new(8, 0, 19.8, 2.0, "R-Ring", "South"),
        new(8, 1, 20.8, 1.0, "R-Ring", "East"),
        new(8, 2, 19.8, 1.0, "R-Ring", "Down"),
        new(8, 3, 19.8, 0.0, "R-Ring", "North"),
        new(8, 4, 18.8, 1.0, "R-Ring", "West"),

        // Row 9 — R-Pinky
        new(9, 0, 22.3, 3.5, "R-Pinky", "South"),
        new(9, 1, 23.3, 2.5, "R-Pinky", "East"),
        new(9, 2, 22.3, 2.5, "R-Pinky", "Down"),
        new(9, 3, 22.3, 1.5, "R-Pinky", "North"),
        new(9, 4, 21.3, 2.5, "R-Pinky", "West"),
    ];

    public static IReadOnlyList<KeyPosition> GetKeyPositions() => _positions;
}

/// <summary>
/// Physical position of a single key on the board.
/// </summary>
public record KeyPosition(
    int Row,
    int Col,
    double X,
    double Y,
    string Cluster,
    string Direction,
    double Width = 1.0,
    double Height = 1.0
);
