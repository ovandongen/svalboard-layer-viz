namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Single source of truth for all known QMK keycodes, grouped by category.
/// Used by <see cref="KeycodeService"/> for label resolution and by the
/// key picker UI for catalog browsing.
/// </summary>
public static class KeycodeCatalog
{
    /// <summary>
    /// A keycode entry in the catalog: raw code, display label, and optional
    /// shifted symbol (for keys that produce a different character with Shift).
    /// </summary>
    public record CatalogEntry(ushort Code, string Label, string? ShiftedLabel = null);

    // --- Grouped catalog lists ---

    public static readonly IReadOnlyList<CatalogEntry> Letters =
    [
        new(0x04, "A"), new(0x05, "B"), new(0x06, "C"), new(0x07, "D"),
        new(0x08, "E"), new(0x09, "F"), new(0x0A, "G"), new(0x0B, "H"),
        new(0x0C, "I"), new(0x0D, "J"), new(0x0E, "K"), new(0x0F, "L"),
        new(0x10, "M"), new(0x11, "N"), new(0x12, "O"), new(0x13, "P"),
        new(0x14, "Q"), new(0x15, "R"), new(0x16, "S"), new(0x17, "T"),
        new(0x18, "U"), new(0x19, "V"), new(0x1A, "W"), new(0x1B, "X"),
        new(0x1C, "Y"), new(0x1D, "Z"),
    ];

    public static readonly IReadOnlyList<CatalogEntry> Numbers =
    [
        new(0x1E, "1", "!"), new(0x1F, "2", "@"), new(0x20, "3", "#"),
        new(0x21, "4", "$"), new(0x22, "5", "%"), new(0x23, "6", "^"),
        new(0x24, "7", "&"), new(0x25, "8", "*"), new(0x26, "9", "("),
        new(0x27, "0", ")"),
    ];

    public static readonly IReadOnlyList<CatalogEntry> Punctuation =
    [
        new(0x2D, "-", "_"), new(0x2E, "=", "+"),
        new(0x2F, "[", "{"), new(0x30, "]", "}"),
        new(0x31, "\\", "|"), new(0x32, "#"),
        new(0x33, ";", ":"), new(0x34, "'", "\""),
        new(0x35, "`", "~"), new(0x36, ",", "<"),
        new(0x37, ".", ">"), new(0x38, "/", "?"),
    ];

    public static readonly IReadOnlyList<CatalogEntry> Editing =
    [
        new(0x28, "Enter"), new(0x29, "Esc"), new(0x2A, "Bksp"),
        new(0x2B, "Tab"), new(0x2C, "Space"),
    ];

    public static readonly IReadOnlyList<CatalogEntry> Navigation =
    [
        new(0x46, "PrtSc"), new(0x47, "ScrLk"), new(0x48, "Pause"),
        new(0x49, "Ins"), new(0x4A, "Home"), new(0x4B, "PgUp"),
        new(0x4C, "Del"), new(0x4D, "End"), new(0x4E, "PgDn"),
        new(0x4F, "Right"), new(0x50, "Left"), new(0x51, "Down"), new(0x52, "Up"),
    ];

    public static readonly IReadOnlyList<CatalogEntry> FunctionKeys =
    [
        new(0x3A, "F1"), new(0x3B, "F2"), new(0x3C, "F3"), new(0x3D, "F4"),
        new(0x3E, "F5"), new(0x3F, "F6"), new(0x40, "F7"), new(0x41, "F8"),
        new(0x42, "F9"), new(0x43, "F10"), new(0x44, "F11"), new(0x45, "F12"),
        new(0x68, "F13"), new(0x69, "F14"), new(0x6A, "F15"), new(0x6B, "F16"),
        new(0x6C, "F17"), new(0x6D, "F18"), new(0x6E, "F19"), new(0x6F, "F20"),
        new(0x70, "F21"), new(0x71, "F22"), new(0x72, "F23"), new(0x73, "F24"),
    ];

    public static readonly IReadOnlyList<CatalogEntry> LockKeys =
    [
        new(0x39, "Caps"), new(0x53, "NumLk"),
    ];

    public static readonly IReadOnlyList<CatalogEntry> Numpad =
    [
        new(0x54, "KP /"), new(0x55, "KP *"), new(0x56, "KP -"),
        new(0x57, "KP +"), new(0x58, "KP Ent"),
        new(0x59, "KP 1"), new(0x5A, "KP 2"), new(0x5B, "KP 3"),
        new(0x5C, "KP 4"), new(0x5D, "KP 5"), new(0x5E, "KP 6"),
        new(0x5F, "KP 7"), new(0x60, "KP 8"), new(0x61, "KP 9"),
        new(0x62, "KP 0"), new(0x63, "KP ."),
    ];

    public static readonly IReadOnlyList<CatalogEntry> Modifiers =
    [
        new(0xE0, "LCtrl"), new(0xE1, "LShift"), new(0xE2, "LAlt"), new(0xE3, "LGUI"),
        new(0xE4, "RCtrl"), new(0xE5, "RShift"), new(0xE6, "RAlt"), new(0xE7, "RGUI"),
    ];

    public static readonly IReadOnlyList<CatalogEntry> MouseKeys =
    [
        new(0xCD, "Ms\u2191"), new(0xCE, "Ms\u2193"),
        new(0xCF, "Ms\u2190"), new(0xD0, "Ms\u2192"),
        new(0xD1, "Btn1"), new(0xD2, "Btn2"), new(0xD3, "Btn3"),
        new(0xD4, "Btn4"), new(0xD5, "Btn5"),
        new(0xD6, "Wh\u2191"), new(0xD7, "Wh\u2193"),
        new(0xD8, "Wh\u2190"), new(0xD9, "Wh\u2192"),
        new(0xDA, "Acl0"), new(0xDB, "Acl1"), new(0xDC, "Acl2"),
    ];

    public static readonly IReadOnlyList<CatalogEntry> Media =
    [
        new(0x7F, "Mute"), new(0x80, "Vol+"), new(0x81, "Vol-"),
    ];

    public static readonly IReadOnlyList<CatalogEntry> Special =
    [
        new(0x64, "NUBS"), new(0x65, "App"),
    ];

    /// <summary>
    /// Keycodes that don't fit the basic 0x00–0xFF range or any structured
    /// QMK range: empty, transparent, and named QMK specials (Repeat, Layer
    /// Lock). Listed in the picker alongside basic keys but resolved via
    /// dedicated descriptors (not <see cref="BasicKeycode"/>), so the picker
    /// must run catalog codes through <see cref="KeycodeDecoder"/> rather
    /// than wrapping them in BasicKeycode directly.
    /// </summary>
    public static readonly IReadOnlyList<CatalogEntry> SpecialKeys =
    [
        new(0x0000, "Empty"),
        new(0x0001, "▽"),
        new(0x7C79, "Repeat"),
        new(0x7C7B, "LyrLk"),
    ];

    /// <summary>
    /// Full 16-bit codes that map to named QMK specials handled by
    /// <see cref="SpecialKeycode"/>. Excludes 0x0000/0x0001 since those
    /// already have dedicated descriptors (NoKeycode/TransparentKeycode).
    /// </summary>
    public static readonly IReadOnlyDictionary<ushort, string> NamedSpecialKeycodes =
        new Dictionary<ushort, string>
        {
            { 0x7C79, "Repeat" },
            { 0x7C7B, "LyrLk" },
        };

    // --- Derived lookup dictionaries (built once, used by KeycodeService) ---

    private static Dictionary<ushort, string>? _basicKeycodes;
    private static Dictionary<ushort, string>? _shiftedSymbols;

    /// <summary>
    /// Flat lookup: raw keycode → display label for all basic keycodes (0x00–0xFF).
    /// </summary>
    public static IReadOnlyDictionary<ushort, string> BasicKeycodes =>
        _basicKeycodes ??= BuildBasicKeycodes();

    /// <summary>
    /// Shift + base key → resulting symbol (US ANSI layout).
    /// </summary>
    public static IReadOnlyDictionary<ushort, string> ShiftedSymbols =>
        _shiftedSymbols ??= BuildShiftedSymbols();

    /// <summary>
    /// All catalog groups, for iteration by the key picker.
    /// </summary>
    public static IReadOnlyList<(string GroupName, IReadOnlyList<CatalogEntry> Entries)> AllGroups =>
    [
        ("Letters", Letters),
        ("Numbers", Numbers),
        ("Punctuation", Punctuation),
        ("Editing", Editing),
        ("Navigation", Navigation),
        ("Function Keys", FunctionKeys),
        ("Lock Keys", LockKeys),
        ("Numpad", Numpad),
        ("Modifiers", Modifiers),
        ("Mouse", MouseKeys),
        ("Media", Media),
        ("Special", Special),
    ];

    private static Dictionary<ushort, string> BuildBasicKeycodes()
    {
        var dict = new Dictionary<ushort, string>();
        foreach (var group in AllGroups)
        {
            foreach (var entry in group.Entries)
                dict.TryAdd(entry.Code, entry.Label);
        }
        return dict;
    }

    private static Dictionary<ushort, string> BuildShiftedSymbols()
    {
        var dict = new Dictionary<ushort, string>();
        foreach (var group in AllGroups)
        {
            foreach (var entry in group.Entries)
            {
                if (entry.ShiftedLabel is not null)
                    dict.TryAdd(entry.Code, entry.ShiftedLabel);
            }
        }
        return dict;
    }
}
