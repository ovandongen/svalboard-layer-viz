using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Keymap.Builders;
using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Steps in the key picker wizard flow.
/// </summary>
public enum PickerStep
{
    /// <summary>Choose a category: Basic, Modifier, Layer, Media, Custom, Advanced.</summary>
    Category,

    /// <summary>Choose a specific builder or catalog entry within the category.</summary>
    Selection,

    /// <summary>Fill in builder argument slots (only for composite keycodes).</summary>
    Arguments,

    /// <summary>Preview the final keycode before applying.</summary>
    Preview,
}

/// <summary>
/// Categories shown in the first picker step.
/// </summary>
public enum PickerCategory
{
    Basic,
    Modifier,
    Layer,
    Media,
    Mouse,
    Macro,
    TapDance,
    Custom,
    Advanced,
}

/// <summary>
/// Drives the key picker dialog: category navigation, builder hosting,
/// catalog browsing, search, and recently-used tracking.
/// </summary>
public partial class PickerSessionViewModel : ObservableObject
{
    private readonly IReadOnlyList<IKeycodeBuilder> _builders;
    private readonly KeycodeService _keycodeService;
    private readonly IReadOnlyList<CustomKeycode> _customKeycodes;
    private readonly int _macroCount;
    private readonly int _tapDanceCount;

    [ObservableProperty] private PickerStep _currentStep = PickerStep.Category;
    [ObservableProperty] private PickerCategory? _selectedCategory;
    [ObservableProperty] private IKeycodeBuilder? _activeBuilder;
    [ObservableProperty] private KeycodeCatalog.CatalogEntry? _selectedCatalogEntry;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private KeycodeDescriptor? _result;
    [ObservableProperty] private string _previewLabel = "";
    [ObservableProperty] private ushort _previewRawCode;

    // Modifier toggles — applied to basic keys in catalog mode
    [ObservableProperty] private bool _modCtrl;
    [ObservableProperty] private bool _modShift;
    [ObservableProperty] private bool _modAlt;
    [ObservableProperty] private bool _modGui;
    /// <summary>When true, Ctrl/Shift/Alt/Gui map to their right-side counterparts.</summary>
    [ObservableProperty] private bool _modRight;

    /// <summary>
    /// The catalog entries matching the current category + search filter.
    /// </summary>
    [ObservableProperty] private IReadOnlyList<KeycodeCatalog.CatalogEntry> _filteredCatalog = [];

    /// <summary>
    /// Recently used keycodes (raw codes), most recent first. Shared across sessions.
    /// </summary>
    public static List<ushort> RecentlyUsed { get; } = new(capacity: 20);

    /// <summary>
    /// Invoked when the user confirms a selection (Apply). Fires synchronously
    /// with the final raw keycode just before the dialog closes.
    /// </summary>
    public Action<ushort>? Applied { get; set; }

    /// <summary>Invoked when the user cancels the picker.</summary>
    public Action? Cancelled { get; set; }

    /// <summary>
    /// All available builders for the current category.
    /// </summary>
    [ObservableProperty] private IReadOnlyList<IKeycodeBuilder> _availableBuilders = [];

    /// <summary>
    /// Layers with user-facing names, for binding to layer dropdowns in the
    /// Arguments step. Populated when the picker opens; empty list means
    /// dropdowns fall back to numeric range 0..15.
    /// </summary>
    public IReadOnlyList<LayerOption> LayerOptions { get; }

    /// <summary>
    /// All layer function kinds (MO/TG/TO/DF/TT/OSL), for LayerFunctionBuilder's combo.
    /// </summary>
    public static IReadOnlyList<LayerFunctionKind> LayerFunctionKinds { get; } =
        Enum.GetValues<LayerFunctionKind>();

    /// <summary>
    /// Flat list of all basic keycode entries, for the Mod-Tap / Layer-Tap key-selector.
    /// </summary>
    public static IReadOnlyList<KeycodeCatalog.CatalogEntry> AllBasicKeycodes { get; } =
        KeycodeCatalog.Letters
            .Concat(KeycodeCatalog.Numbers)
            .Concat(KeycodeCatalog.Punctuation)
            .Concat(KeycodeCatalog.Editing)
            .Concat(KeycodeCatalog.Navigation)
            .Concat(KeycodeCatalog.FunctionKeys)
            .Concat(KeycodeCatalog.LockKeys)
            .Concat(KeycodeCatalog.Numpad)
            .Concat(KeycodeCatalog.Modifiers)
            .Concat(KeycodeCatalog.Special)
            .ToList();

    /// <summary>True when the device has macros and the Macro category should be shown.</summary>
    public bool HasMacros => _macroCount > 0;

    /// <summary>True when the device has tap-dance entries and the TapDance category should be shown.</summary>
    public bool HasTapDances => _tapDanceCount > 0;

    /// <summary>
    /// Categories the caller allows. When null, all categories are allowed.
    /// Setting this refreshes the ShowCategory_* properties.
    /// </summary>
    public IReadOnlySet<PickerCategory>? AllowedCategories
    {
        get => _allowedCategories;
        set
        {
            _allowedCategories = value;
            OnPropertyChanged(nameof(ShowCategory_Basic));
            OnPropertyChanged(nameof(ShowCategory_Modifier));
            OnPropertyChanged(nameof(ShowCategory_Layer));
            OnPropertyChanged(nameof(ShowCategory_Media));
            OnPropertyChanged(nameof(ShowCategory_Mouse));
            OnPropertyChanged(nameof(ShowCategory_Macro));
            OnPropertyChanged(nameof(ShowCategory_TapDance));
            OnPropertyChanged(nameof(ShowCategory_Custom));
            OnPropertyChanged(nameof(ShowCategory_Advanced));
        }
    }
    private IReadOnlySet<PickerCategory>? _allowedCategories;

    private bool IsAllowed(PickerCategory c) => _allowedCategories is null || _allowedCategories.Contains(c);

    public bool ShowCategory_Basic => IsAllowed(PickerCategory.Basic);
    public bool ShowCategory_Modifier => IsAllowed(PickerCategory.Modifier);
    public bool ShowCategory_Layer => IsAllowed(PickerCategory.Layer);
    public bool ShowCategory_Media => IsAllowed(PickerCategory.Media);
    public bool ShowCategory_Mouse => IsAllowed(PickerCategory.Mouse);
    public bool ShowCategory_Macro => HasMacros && IsAllowed(PickerCategory.Macro);
    public bool ShowCategory_TapDance => HasTapDances && IsAllowed(PickerCategory.TapDance);
    public bool ShowCategory_Custom => IsAllowed(PickerCategory.Custom);
    public bool ShowCategory_Advanced => IsAllowed(PickerCategory.Advanced);

    public PickerSessionViewModel() : this(BuilderRegistry.CreateAll(), new KeycodeService(), [], []) { }

    public PickerSessionViewModel(
        IReadOnlyList<IKeycodeBuilder> builders,
        KeycodeService? keycodeService = null,
        IReadOnlyList<CustomKeycode>? customKeycodes = null,
        IReadOnlyList<LayerOption>? layerOptions = null,
        int macroCount = 0,
        int tapDanceCount = 0)
    {
        _builders = builders;
        _keycodeService = keycodeService ?? new KeycodeService();
        _customKeycodes = customKeycodes ?? [];
        _macroCount = macroCount;
        _tapDanceCount = tapDanceCount;
        LayerOptions = layerOptions ?? [];
        if (_customKeycodes.Count > 0)
            _keycodeService.SetCustomKeycodes(_customKeycodes);
    }

    /// <summary>
    /// Whether the user can move forward from the current step.
    /// </summary>
    public bool CanGoNext => CurrentStep switch
    {
        PickerStep.Category => SelectedCategory.HasValue,
        PickerStep.Selection => ActiveBuilder is not null || Result is not null,
        PickerStep.Arguments => ActiveBuilder?.CanBuild == true,
        PickerStep.Preview => Result is not null,
        _ => false,
    };

    /// <summary>
    /// Whether the user can go back from the current step.
    /// </summary>
    public bool CanGoBack => CurrentStep != PickerStep.Category;

    // --- Step visibility helpers for AXAML binding ---

    public bool IsStepCategory => CurrentStep == PickerStep.Category;
    public bool IsStepSelection => CurrentStep == PickerStep.Selection;
    public bool IsStepArguments => CurrentStep == PickerStep.Arguments;
    public bool IsStepPreview => CurrentStep == PickerStep.Preview;

    /// <summary>
    /// True when the Selection step should show the catalog: Basic, Media, Mouse,
    /// or Custom when the device exposes named custom keycodes.
    /// </summary>
    public bool IsCatalogMode => SelectedCategory is PickerCategory.Basic or PickerCategory.Media or PickerCategory.Mouse or PickerCategory.Macro or PickerCategory.TapDance
        || (SelectedCategory == PickerCategory.Custom && _customKeycodes.Count > 0);

    /// <summary>
    /// True when the Selection step should show builders: Modifier, Layer, or Custom
    /// when the device has no named custom keycodes (fallback to numeric index).
    /// </summary>
    public bool IsBuilderMode => SelectedCategory is PickerCategory.Modifier or PickerCategory.Layer
        || (SelectedCategory == PickerCategory.Custom && _customKeycodes.Count == 0);

    /// <summary>
    /// True when the Next button should be shown. Arguments always shows Next;
    /// Selection shows it only after state has been retained (Back from a later step).
    /// </summary>
    public bool ShowNextButton => CurrentStep switch
    {
        PickerStep.Arguments => true,
        PickerStep.Selection => ActiveBuilder is not null || Result is not null,
        _ => false,
    };

    /// <summary>True when the Apply button should be shown (Preview step).</summary>
    public bool ShowApplyButton => CurrentStep == PickerStep.Preview;

    [RelayCommand]
    private void SelectCategory(PickerCategory category)
    {
        SelectedCategory = category;

        // Catalog modes: Basic / Media / Mouse, plus Custom when device defines named keycodes.
        if (category is PickerCategory.Basic or PickerCategory.Media or PickerCategory.Mouse or PickerCategory.Macro or PickerCategory.TapDance
            || (category == PickerCategory.Custom && _customKeycodes.Count > 0))
        {
            UpdateFilteredCatalog();
            AvailableBuilders = [];
            CurrentStep = PickerStep.Selection;
        }
        // For Advanced, go to raw hex entry (preview step)
        else if (category == PickerCategory.Advanced)
        {
            ActiveBuilder = null;
            CurrentStep = PickerStep.Preview;
        }
        else
        {
            // Modifier/Layer — show builders. Custom falls here only when no
            // device definition is available (fallback to numeric-index builder).
            var builderCategory = category switch
            {
                PickerCategory.Modifier => BuilderCategory.Modifier,
                PickerCategory.Layer => BuilderCategory.Layer,
                PickerCategory.Custom => BuilderCategory.Custom,
                _ => throw new InvalidOperationException(),
            };
            AvailableBuilders = _builders.Where(b => b.Category == builderCategory).ToList();
            FilteredCatalog = [];
            CurrentStep = PickerStep.Selection;
        }

        NotifyStepChanged();
    }

    // Both SelectBuilder and SelectCatalogEntry are thin shims around their
    // observable backing properties — the real side effects (advance step,
    // clear Result, etc.) live in the partial OnXChanged hooks below, so the
    // ListBox SelectedItem two-way bindings in the view route through the
    // same code path as direct command invocation from tests.

    [RelayCommand]
    private void SelectBuilder(IKeycodeBuilder builder) => ActiveBuilder = builder;

    [RelayCommand]
    private void SelectCatalogEntry(KeycodeCatalog.CatalogEntry entry) => SelectedCatalogEntry = entry;

    /// <summary>
    /// Sets the tap key on the active builder (ModTap / LayerTap). Used by the
    /// in-template basic-key grid, which lives inside the builder's data template.
    /// </summary>
    [RelayCommand]
    private void SelectTapKey(ushort code)
    {
        if (ActiveBuilder is IHasTapKey h)
            h.TapKey = code;
    }

    [RelayCommand]
    private void GoNext()
    {
        if (!CanGoNext) return;

        switch (CurrentStep)
        {
            // Forward from Selection is only reachable via GoBack — catalog entries
            // and builder buttons auto-advance on click. When retained state exists,
            // Next returns the user to whichever step they were on.
            case PickerStep.Selection when ActiveBuilder is not null:
                CurrentStep = PickerStep.Arguments;
                break;
            case PickerStep.Selection when Result is not null:
                CurrentStep = PickerStep.Preview;
                break;
            case PickerStep.Arguments when ActiveBuilder is not null:
                Result = ActiveBuilder.Build();
                PreviewLabel = ActiveBuilder.PreviewLabel;
                PreviewRawCode = KeycodeEncoder.Encode(Result);
                CurrentStep = PickerStep.Preview;
                break;
        }

        NotifyStepChanged();
    }

    [RelayCommand]
    private void GoBack()
    {
        if (!CanGoBack) return;

        CurrentStep = CurrentStep switch
        {
            PickerStep.Preview when ActiveBuilder is not null => PickerStep.Arguments,
            // Advanced category jumps Category → Preview directly, so going back must skip Selection.
            PickerStep.Preview when SelectedCategory == PickerCategory.Advanced => PickerStep.Category,
            PickerStep.Preview => PickerStep.Selection,
            PickerStep.Arguments => PickerStep.Selection,
            PickerStep.Selection => PickerStep.Category,
            _ => CurrentStep,
        };

        // Only the "abandon this category" transition clears retained state.
        // Preview→Arguments and Arguments→Selection preserve ActiveBuilder and
        // Result so re-pressing Next re-enters the previous step without rework.
        if (CurrentStep == PickerStep.Category)
        {
            SelectedCategory = null;
            ActiveBuilder = null;
            SelectedCatalogEntry = null;
            Result = null;
        }

        NotifyStepChanged();
    }

    /// <summary>
    /// Confirms the selection — called by the dialog's Apply button.
    /// Adds to recently-used list.
    /// </summary>
    [RelayCommand]
    private void Confirm()
    {
        if (Result is null) return;

        var raw = KeycodeEncoder.Encode(Result);
        RecentlyUsed.Remove(raw);
        RecentlyUsed.Insert(0, raw);
        while (RecentlyUsed.Count > 20)
            RecentlyUsed.RemoveAt(RecentlyUsed.Count - 1);

        Applied?.Invoke(raw);
    }

    /// <summary>
    /// Sets a raw keycode directly (Advanced mode / raw hex entry / recently-used).
    /// Re-hydrates wizard state (category, builder, slot values) so Back navigation
    /// lands on a meaningful, pre-populated step instead of an empty screen.
    /// </summary>
    public void SetRawKeycode(ushort code)
    {
        Result = KeycodeDecoder.Decode(code);
        PreviewRawCode = code;
        var label = _keycodeService.Resolve(code).Label;
        PreviewLabel = string.IsNullOrEmpty(label) ? $"0x{code:X4}" : label;

        HydrateWizardStateFrom(Result);

        CurrentStep = PickerStep.Preview;
        NotifyStepChanged();
    }

    /// <summary>
    /// Seeds the wizard for a decoded descriptor so GoBack from Preview lands on
    /// a populated Arguments/Selection step. With observable typed builders, each
    /// arm becomes direct property assignment.
    /// </summary>
    private void HydrateWizardStateFrom(KeycodeDescriptor descriptor)
    {
        ActiveBuilder = null;
        AvailableBuilders = [];
        FilteredCatalog = [];
        SelectedCatalogEntry = null;

        switch (descriptor)
        {
            case BasicKeycode basic:
                if (KeycodeCatalog.MouseKeys.Any(e => e.Code == basic.BaseCode))
                    SelectedCategory = PickerCategory.Mouse;
                else if (KeycodeCatalog.Media.Any(e => e.Code == basic.BaseCode))
                    SelectedCategory = PickerCategory.Media;
                else
                    SelectedCategory = PickerCategory.Basic;
                UpdateFilteredCatalog();
                SelectedCatalogEntry = FilteredCatalog.FirstOrDefault(e => e.Code == basic.BaseCode);
                break;

            // Empty / Transparent / named QMK specials all live in the Basic
            // catalog under the SpecialKeys group, so Back from Preview lands
            // on the same grid the user picked them from.
            case NoKeycode:
                SelectedCategory = PickerCategory.Basic;
                UpdateFilteredCatalog();
                SelectedCatalogEntry = FilteredCatalog.FirstOrDefault(e => e.Code == 0x0000);
                break;

            case TransparentKeycode:
                SelectedCategory = PickerCategory.Basic;
                UpdateFilteredCatalog();
                SelectedCatalogEntry = FilteredCatalog.FirstOrDefault(e => e.Code == 0x0001);
                break;

            case SpecialKeycode special:
                SelectedCategory = PickerCategory.Basic;
                UpdateFilteredCatalog();
                SelectedCatalogEntry = FilteredCatalog.FirstOrDefault(e => e.Code == special.Code);
                break;

            case ModTapKeycode modTap:
                SelectedCategory = PickerCategory.Modifier;
                AvailableBuilders = _builders.Where(b => b.Category == BuilderCategory.Modifier).ToList();
                if (AvailableBuilders.OfType<ModTapBuilder>().FirstOrDefault() is { } mt)
                {
                    mt.Mods = modTap.Mods;
                    mt.TapKey = modTap.BaseCode;
                    ActiveBuilder = mt;
                }
                break;

            case OneShotModKeycode osm:
                SelectedCategory = PickerCategory.Modifier;
                AvailableBuilders = _builders.Where(b => b.Category == BuilderCategory.Modifier).ToList();
                if (AvailableBuilders.OfType<OneShotModBuilder>().FirstOrDefault() is { } osmB)
                {
                    osmB.Mods = osm.Mods;
                    ActiveBuilder = osmB;
                }
                break;

            case LayerTapKeycode layerTap:
                SelectedCategory = PickerCategory.Layer;
                AvailableBuilders = _builders.Where(b => b.Category == BuilderCategory.Layer).ToList();
                if (AvailableBuilders.OfType<LayerTapBuilder>().FirstOrDefault() is { } lt)
                {
                    lt.Layer = layerTap.Layer;
                    lt.TapKey = layerTap.BaseCode;
                    ActiveBuilder = lt;
                }
                break;

            case LayerModKeycode layerMod:
                SelectedCategory = PickerCategory.Layer;
                AvailableBuilders = _builders.Where(b => b.Category == BuilderCategory.Layer).ToList();
                if (AvailableBuilders.OfType<LayerModBuilder>().FirstOrDefault() is { } lm)
                {
                    lm.Layer = layerMod.Layer;
                    lm.Mods = layerMod.Mods;
                    ActiveBuilder = lm;
                }
                break;

            case LayerFunctionKeycode layerFn:
                SelectedCategory = PickerCategory.Layer;
                AvailableBuilders = _builders.Where(b => b.Category == BuilderCategory.Layer).ToList();
                if (AvailableBuilders.OfType<LayerFunctionBuilder>().FirstOrDefault() is { } lf)
                {
                    lf.Kind = layerFn.Kind;
                    lf.Layer = layerFn.Layer;
                    ActiveBuilder = lf;
                }
                break;

            case MacroKeycode mk:
                SelectedCategory = PickerCategory.Macro;
                UpdateFilteredCatalog();
                SelectedCatalogEntry = FilteredCatalog.FirstOrDefault(e => e.Code == (ushort)(0x7700 + mk.MacroIndex));
                break;

            case TapDanceKeycode td:
                SelectedCategory = PickerCategory.TapDance;
                UpdateFilteredCatalog();
                SelectedCatalogEntry = FilteredCatalog.FirstOrDefault(e => e.Code == (ushort)(0x5700 + td.Index));
                break;

            case CustomKeycodeDescriptor custom:
                SelectedCategory = PickerCategory.Custom;
                if (_customKeycodes.Count > 0)
                {
                    // Device exposes named customs — use catalog mode so Back lands
                    // on the named-keycode grid rather than a numeric stepper.
                    UpdateFilteredCatalog();
                    var customCode = (ushort)(0x7E00 + custom.Index);
                    SelectedCatalogEntry = FilteredCatalog.FirstOrDefault(e => e.Code == customCode);
                }
                else
                {
                    AvailableBuilders = _builders.Where(b => b.Category == BuilderCategory.Custom).ToList();
                    if (AvailableBuilders.OfType<CustomKeycodeBuilder>().FirstOrDefault() is { } ck)
                    {
                        ck.Index = custom.Index;
                        ActiveBuilder = ck;
                    }
                }
                break;

            default:
                // ModifiedKeycode, RawKeycode — no dedicated builder exists.
                // Fall back to Advanced so Back returns to the category picker
                // rather than an empty Selection screen.
                SelectedCategory = PickerCategory.Advanced;
                break;
        }
    }

    // Live update CanGoNext / ShowNextButton as the user fills the active builder.
    // Also drives the "user picked a builder on Selection" advance to Arguments —
    // gated on CurrentStep so the hydration path (which sets ActiveBuilder while
    // CurrentStep is still Category) doesn't accidentally jump steps.
    partial void OnActiveBuilderChanged(IKeycodeBuilder? oldValue, IKeycodeBuilder? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= OnActiveBuilderPropertyChanged;
        if (newValue is not null)
            newValue.PropertyChanged += OnActiveBuilderPropertyChanged;

        if (newValue is not null && CurrentStep == PickerStep.Selection)
        {
            Result = null;
            CurrentStep = PickerStep.Arguments;
            NotifyStepChanged();
        }
    }

    // Same gating idea for the catalog entry: tracks selection during hydration
    // (so the ListBox highlights the previously-picked item on Back), and only
    // auto-advances when the user actually clicks while on the Selection step.
    partial void OnSelectedCatalogEntryChanged(KeycodeCatalog.CatalogEntry? value)
    {
        if (value is null) return;
        if (CurrentStep != PickerStep.Selection) return;

        // Run the entry through the decoder so codes outside the basic 0x00–0xFF
        // range (Empty 0x0000, Transparent 0x0001, named QMK specials, custom
        // QK_KB) emit their proper descriptor instead of being wrapped in
        // BasicKeycode (which would be conceptually wrong and break hydration).
        var descriptor = KeycodeDecoder.Decode(value.Code);
        var label = value.Label;
        var rawCode = value.Code;

        // Apply modifier toggles to basic keycodes
        var mods = CollectModFlags();
        if (mods != ModFlags.None && descriptor is BasicKeycode basic)
        {
            descriptor = new ModifiedKeycode(mods, basic.BaseCode);
            rawCode = KeycodeEncoder.Encode(descriptor);
            label = $"{FormatModPrefix(mods)}+{label}";
        }

        Result = descriptor;
        PreviewLabel = label;
        PreviewRawCode = rawCode;
        CurrentStep = PickerStep.Preview;
        NotifyStepChanged();
    }

    private void OnActiveBuilderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(ShowNextButton));
    }

    private void NotifyStepChanged()
    {
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsStepCategory));
        OnPropertyChanged(nameof(IsStepSelection));
        OnPropertyChanged(nameof(IsStepArguments));
        OnPropertyChanged(nameof(IsStepPreview));
        OnPropertyChanged(nameof(IsCatalogMode));
        OnPropertyChanged(nameof(IsBuilderMode));
        OnPropertyChanged(nameof(ShowNextButton));
        OnPropertyChanged(nameof(ShowApplyButton));
    }

    partial void OnSearchTextChanged(string value) => UpdateFilteredCatalog();

    private void UpdateFilteredCatalog()
    {
        var groups = SelectedCategory switch
        {
            PickerCategory.Basic => KeycodeCatalog.Letters
                .Concat(KeycodeCatalog.Numbers)
                .Concat(KeycodeCatalog.Punctuation)
                .Concat(KeycodeCatalog.Editing)
                .Concat(KeycodeCatalog.Navigation)
                .Concat(KeycodeCatalog.FunctionKeys)
                .Concat(KeycodeCatalog.LockKeys)
                .Concat(KeycodeCatalog.Numpad)
                .Concat(KeycodeCatalog.Modifiers)
                .Concat(KeycodeCatalog.Special)
                .Concat(KeycodeCatalog.SpecialKeys),
            PickerCategory.Media => KeycodeCatalog.Media,
            PickerCategory.Mouse => KeycodeCatalog.MouseKeys,
            PickerCategory.Macro => BuildMacroCatalog(),
            PickerCategory.TapDance => BuildTapDanceCatalog(),
            PickerCategory.Custom => BuildCustomCatalog(),
            _ => Enumerable.Empty<KeycodeCatalog.CatalogEntry>(),
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            groups = groups.Where(e =>
                e.Label.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (e.ShiftedLabel is not null && e.ShiftedLabel.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        FilteredCatalog = groups.ToList();
    }

    private IEnumerable<KeycodeCatalog.CatalogEntry> BuildMacroCatalog()
    {
        for (var i = 0; i < _macroCount; i++)
            yield return new KeycodeCatalog.CatalogEntry((ushort)(0x7700 + i), $"M{i}");
    }

    private IEnumerable<KeycodeCatalog.CatalogEntry> BuildTapDanceCatalog()
    {
        for (var i = 0; i < _tapDanceCount; i++)
            yield return new KeycodeCatalog.CatalogEntry((ushort)(0x5700 + i), $"TD{i}");
    }

    private IEnumerable<KeycodeCatalog.CatalogEntry> BuildCustomCatalog()
    {
        for (var i = 0; i < _customKeycodes.Count; i++)
        {
            var custom = _customKeycodes[i];
            var label = !string.IsNullOrEmpty(custom.ShortName) ? custom.ShortName : custom.Name;
            yield return new KeycodeCatalog.CatalogEntry((ushort)(0x7E00 + i), label);
        }
    }

    /// <summary>Reads modifier checkboxes into ModFlags bitmask.</summary>
    private ModFlags CollectModFlags()
    {
        var flags = ModFlags.None;
        if (ModCtrl) flags |= ModFlags.Ctrl;
        if (ModShift) flags |= ModFlags.Shift;
        if (ModAlt) flags |= ModFlags.Alt;
        if (ModGui) flags |= ModFlags.Gui;
        // QMK's modified-keycode field shares a single Right bit across all mods:
        // one keycode can't encode L+R mix. Right toggle flips the whole set.
        if (ModRight && flags != ModFlags.None) flags |= ModFlags.Right;
        return flags;
    }

    /// <summary>Formats ModFlags as a readable prefix like "Shift" or "RCtrl+RAlt".</summary>
    private static string FormatModPrefix(ModFlags mods)
    {
        var prefix = mods.HasFlag(ModFlags.Right) ? "R" : "";
        var names = new List<string>(4);
        if (mods.HasFlag(ModFlags.Ctrl)) names.Add($"{prefix}Ctrl");
        if (mods.HasFlag(ModFlags.Shift)) names.Add($"{prefix}Shift");
        if (mods.HasFlag(ModFlags.Alt)) names.Add($"{prefix}Alt");
        if (mods.HasFlag(ModFlags.Gui)) names.Add($"{prefix}Gui");
        return string.Join("+", names);
    }
}
