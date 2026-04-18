using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.Core.Models;
using SvalboardLayerViz.Core.QmkSettings;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// Groups QMK setting rows under a localized category header for display
/// in the Device tab.
/// </summary>
public record QmkSettingGroupViewModel(
    string GroupName,
    IReadOnlyList<QmkSettingRowViewModel> Items);

/// <summary>
/// ViewModel for the "Device" tab in <see cref="Views.SettingsWindow"/>.
/// Shows QMK firmware settings grouped by category. Read-only when not in
/// edit mode; when editable, value changes are staged as
/// <see cref="Core.Keymap.SetQmkSettingOp"/> via a callback to
/// <see cref="MainWindowViewModel.ApplySettingEdit"/>.
/// </summary>
public class QmkSettingsTabViewModel
{
    /// <summary>
    /// Tracks the current packed value for each bitfield QSID so that
    /// toggling one bit can read-modify-write the full value.
    /// </summary>
    private readonly Dictionary<ushort, ushort> _bitfieldValues = new();

    public QmkSettingsTabViewModel(
        IReadOnlyList<QmkSettingValue> deviceSettings,
        bool isEditMode,
        Action<ushort, ushort>? onSettingChanged)
    {
        IsEditMode = isEditMode;

        // Build rows from device settings, expanding bitfield QSIDs into per-bit rows
        var allRows = new List<QmkSettingRowViewModel>();
        foreach (var setting in deviceSettings)
        {
            var descriptors = QmkSettingsCatalog.GetAllForId(setting.SettingId);

            if (descriptors is not null && descriptors.Count > 0 && descriptors[0].IsBitfield)
            {
                // Bitfield QSID: expand into one row per bit
                _bitfieldValues[setting.SettingId] = setting.Value;
                foreach (var desc in descriptors)
                {
                    var bitValue = (ushort)((setting.Value >> desc.BitIndex) & 1);
                    var bitCallback = CreateBitfieldCallback(setting.SettingId, desc.BitIndex, onSettingChanged);
                    allRows.Add(new QmkSettingRowViewModel(desc, bitValue, isEditMode, bitCallback));
                }
            }
            else
            {
                // Scalar QSID: one row
                var descriptor = descriptors?[0] ?? QmkSettingsCatalog.GetOrFallback(setting.SettingId);
                allRows.Add(new QmkSettingRowViewModel(descriptor, setting.Value, isEditMode, onSettingChanged));
            }
        }

        Settings = allRows;
        HasSettings = allRows.Count > 0;

        // Group rows by catalog group order, only include non-empty groups
        var groups = new List<QmkSettingGroupViewModel>();
        foreach (var (groupName, _) in QmkSettingsCatalog.AllGroups)
        {
            var groupKey = GetGroupKey(groupName);
            var matchingRows = allRows
                .Where(r => r.GroupKey == groupKey)
                .ToList();

            if (matchingRows.Count > 0)
            {
                var localizedName = LocalizeGroupName(groupName);
                groups.Add(new QmkSettingGroupViewModel(localizedName, matchingRows));
            }
        }

        // Any rows not matched to a catalog group go to "Misc"
        var catalogGroupKeys = QmkSettingsCatalog.AllGroups
            .Select(g => GetGroupKey(g.GroupName))
            .ToHashSet();
        var miscRows = allRows
            .Where(r => !catalogGroupKeys.Contains(r.GroupKey))
            .ToList();
        if (miscRows.Count > 0)
            groups.Add(new QmkSettingGroupViewModel(Loc.Instance["QmkSettingGroup_Misc"], miscRows));

        Groups = groups;

        EditModeHint = Loc.Instance["Settings_DeviceEditHint"];
    }

    /// <summary>
    /// Creates a callback for a bitfield bit-row that reads the current packed value,
    /// flips the relevant bit, and forwards the full packed value to the parent callback.
    /// </summary>
    private Action<ushort, ushort>? CreateBitfieldCallback(
        ushort settingId, int bitIndex, Action<ushort, ushort>? parentCallback)
    {
        if (parentCallback is null) return null;

        return (_, newBitValue) =>
        {
            var current = _bitfieldValues.GetValueOrDefault(settingId);
            var packed = newBitValue != 0
                ? (ushort)(current | (1 << bitIndex))
                : (ushort)(current & ~(1 << bitIndex));
            _bitfieldValues[settingId] = packed;
            parentCallback(settingId, packed);
        };
    }

    public IReadOnlyList<QmkSettingRowViewModel> Settings { get; }
    public IReadOnlyList<QmkSettingGroupViewModel> Groups { get; }
    public bool IsEditMode { get; }
    public bool HasSettings { get; }
    public string EditModeHint { get; }

    /// <summary>
    /// Enables Slider value writes on all rows. Call this AFTER the UI has
    /// performed its initial layout pass so Slider binding init noise (which
    /// writes 0 before Min/Max apply) is silently ignored.
    /// </summary>
    public void ActivateSliders()
    {
        foreach (var row in Settings)
            row.MarkInitialized();
    }

    /// <summary>
    /// Maps the catalog's display group name (e.g. "Tap/Hold") to the GroupKey
    /// used on <see cref="QmkSettingDescriptor"/> (e.g. "TapHold").
    /// </summary>
    private static string GetGroupKey(string displayGroupName) =>
        displayGroupName switch
        {
            "Magic" => "Magic",
            "Grave Escape" => "GraveEscape",
            "Tap/Hold" => "TapHold",
            "One-shot" => "OneShot",
            "Combos" => "Combos",
            "Auto-shift" => "AutoShift",
            "Mouse Keys" => "MouseKeys",
            "Misc" => "Misc",
            _ => displayGroupName,
        };

    private static string LocalizeGroupName(string displayGroupName)
    {
        var key = displayGroupName switch
        {
            "Magic" => "QmkSettingGroup_Magic",
            "Grave Escape" => "QmkSettingGroup_GraveEscape",
            "Tap/Hold" => "QmkSettingGroup_TapHold",
            "One-shot" => "QmkSettingGroup_OneShot",
            "Combos" => "QmkSettingGroup_Combos",
            "Auto-shift" => "QmkSettingGroup_AutoShift",
            "Mouse Keys" => "QmkSettingGroup_MouseKeys",
            "Misc" => "QmkSettingGroup_Misc",
            _ => null,
        };
        if (key is null) return displayGroupName;
        var localized = Loc.Instance[key];
        return localized != key ? localized : displayGroupName;
    }
}
