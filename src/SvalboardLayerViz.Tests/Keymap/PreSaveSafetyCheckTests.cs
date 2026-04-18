using SvalboardLayerViz.Core.Keymap;
using Xunit;

namespace SvalboardLayerViz.Tests.Keymap;

public class PreSaveSafetyCheckTests
{
    // Helper: create a keymap with given dimensions, all KC_NO
    private static ushort[,,] MakeEmpty(int layers = 2, int rows = 2, int cols = 2) =>
        new ushort[layers, rows, cols];

    // Helper: create a "clean" keymap — base layer has real keys, layer 1 has MO(0) + keys
    private static ushort[,,] MakeClean()
    {
        var km = new ushort[2, 2, 2];
        // Layer 0: A, B, C, MO(1)
        km[0, 0, 0] = 0x0004; // KC_A
        km[0, 0, 1] = 0x0005; // KC_B
        km[0, 1, 0] = 0x0006; // KC_C
        km[0, 1, 1] = 0x5221; // MO(1)
        // Layer 1: D, E, F, MO(0) — has escape
        km[1, 0, 0] = 0x0007; // KC_D
        km[1, 0, 1] = 0x0008; // KC_E
        km[1, 1, 0] = 0x0009; // KC_F
        km[1, 1, 1] = 0x5220; // MO(0)
        return km;
    }

    // --- Clean keymap ---

    [Fact]
    public void Check_CleanKeymap_ReturnsNoWarnings()
    {
        var warnings = PreSaveSafetyCheck.Check(MakeClean());
        Assert.Empty(warnings);
    }

    // --- BaseLayerUnusable ---

    [Fact]
    public void Check_AllEmptyBaseLayer_WarnsBaseLayerUnusable()
    {
        var km = MakeEmpty();
        // Layer 1 has some keys so we don't also get UnreachableLayer noise
        km[1, 0, 0] = 0x0004;

        var warnings = PreSaveSafetyCheck.Check(km);
        Assert.Contains(warnings, w => w.Kind == SafetyWarningKind.BaseLayerUnusable);
    }

    [Fact]
    public void Check_BaseLayerWithOneKey_NoBaseLayerUnusable()
    {
        var km = MakeEmpty();
        km[0, 0, 0] = 0x0004; // KC_A — one usable key
        var warnings = PreSaveSafetyCheck.Check(km);
        Assert.DoesNotContain(warnings, w => w.Kind == SafetyWarningKind.BaseLayerUnusable);
    }

    // --- AllTransparentBaseLayer ---

    [Fact]
    public void Check_AllTransparentBaseLayer_Warns()
    {
        var km = MakeEmpty();
        // Fill base layer with KC_TRNS
        for (var r = 0; r < 2; r++)
            for (var c = 0; c < 2; c++)
                km[0, r, c] = 0x0001;

        var warnings = PreSaveSafetyCheck.Check(km);
        Assert.Contains(warnings, w => w.Kind == SafetyWarningKind.AllTransparentBaseLayer);
    }

    [Fact]
    public void Check_MixedBaseLayer_NoAllTransparent()
    {
        var km = MakeEmpty();
        km[0, 0, 0] = 0x0001; // KC_TRNS
        km[0, 0, 1] = 0x0004; // KC_A
        var warnings = PreSaveSafetyCheck.Check(km);
        Assert.DoesNotContain(warnings, w => w.Kind == SafetyWarningKind.AllTransparentBaseLayer);
    }

    // --- UnreachableLayer ---

    [Fact]
    public void Check_UnreachableLayer_Warns()
    {
        var km = new ushort[3, 2, 2];
        // Layer 0: has keys, MO(1) only — layer 2 is unreachable
        km[0, 0, 0] = 0x0004;
        km[0, 0, 1] = 0x5221; // MO(1)
        // Layer 1: has keys + MO(0)
        km[1, 0, 0] = 0x0005;
        km[1, 1, 1] = 0x5220; // MO(0)
        // Layer 2: has keys but no one switches to it
        km[2, 0, 0] = 0x0006;

        var warnings = PreSaveSafetyCheck.Check(km);
        Assert.Contains(warnings, w => w.Kind == SafetyWarningKind.UnreachableLayer && w.Layer == 2);
    }

    [Fact]
    public void Check_AllLayersReachable_NoUnreachableWarning()
    {
        var km = new ushort[3, 2, 2];
        km[0, 0, 0] = 0x0004;
        km[0, 0, 1] = 0x5221; // MO(1)
        km[0, 1, 0] = 0x5222; // MO(2)
        km[1, 0, 0] = 0x0005;
        km[1, 1, 1] = 0x5220; // MO(0)
        km[2, 0, 0] = 0x0006;
        km[2, 1, 1] = 0x5220; // MO(0)

        var warnings = PreSaveSafetyCheck.Check(km);
        Assert.DoesNotContain(warnings, w => w.Kind == SafetyWarningKind.UnreachableLayer);
    }

    [Fact]
    public void Check_EmptyUnreachableLayer_NoWarning()
    {
        // Layer 2 is unreachable but completely empty — don't warn
        var km = new ushort[3, 2, 2];
        km[0, 0, 0] = 0x0004;
        km[0, 0, 1] = 0x5221; // MO(1)
        km[1, 0, 0] = 0x0005;
        km[1, 1, 1] = 0x5220; // MO(0)
        // Layer 2: all KC_NO (0x0000)

        var warnings = PreSaveSafetyCheck.Check(km);
        Assert.DoesNotContain(warnings, w => w.Kind == SafetyWarningKind.UnreachableLayer);
    }

    // --- Auto-mouse layer heuristic ---

    [Fact]
    public void Check_UnreachableMouseLayer_NoWarning()
    {
        // Layer 2 is unreachable, but contains several mouse keycodes —
        // treat as an auto-mouse target layer and skip the warning.
        var km = new ushort[3, 3, 3];
        km[0, 0, 0] = 0x0004;
        km[0, 0, 1] = 0x5221; // MO(1)
        km[1, 0, 0] = 0x0005;
        km[1, 1, 1] = 0x5220; // MO(0)
        // Layer 2 — three mouse keys (Ms↑, Btn1, Wh↑)
        km[2, 0, 0] = 0x00CD; // Ms↑
        km[2, 0, 1] = 0x00D1; // Btn1
        km[2, 0, 2] = 0x00D6; // Wh↑

        var warnings = PreSaveSafetyCheck.Check(km);
        Assert.DoesNotContain(warnings, w => w.Kind == SafetyWarningKind.UnreachableLayer);
    }

    [Fact]
    public void Check_UnreachableLayerWithOneMouseKey_StillWarns()
    {
        // Single mouse key on an otherwise-letters layer shouldn't count as
        // an auto-mouse target — still warn.
        var km = new ushort[3, 3, 3];
        km[0, 0, 0] = 0x0004;
        km[0, 0, 1] = 0x5221; // MO(1)
        km[1, 0, 0] = 0x0005;
        km[1, 1, 1] = 0x5220; // MO(0)
        km[2, 0, 0] = 0x0007; // KC_D
        km[2, 0, 1] = 0x0008; // KC_E
        km[2, 0, 2] = 0x00D1; // Btn1 — only one mouse key

        var warnings = PreSaveSafetyCheck.Check(km);
        Assert.Contains(warnings, w => w.Kind == SafetyWarningKind.UnreachableLayer && w.Layer == 2);
    }

    // --- Warning detail ---

    [Fact]
    public void Warning_HasLayerInfo()
    {
        var km = MakeEmpty();
        km[1, 0, 0] = 0x0004; // Layer 1 has a key but no escape
        // Layer 1 unreachable (no MO(1) on layer 0)
        var warnings = PreSaveSafetyCheck.Check(km);

        foreach (var w in warnings)
        {
            if (w.Layer.HasValue)
                Assert.NotNull(w.Detail);
        }
    }

    // --- Layer switch via LM (LayerMod) ---

    [Fact]
    public void Check_LMCountsAsReachable()
    {
        var km = new ushort[2, 2, 2];
        km[0, 0, 0] = 0x0004;
        km[0, 0, 1] = 0x5011; // LM(1, Ctrl)
        km[1, 0, 0] = 0x0005;
        km[1, 1, 1] = 0x5220; // MO(0)

        var warnings = PreSaveSafetyCheck.Check(km);
        Assert.DoesNotContain(warnings, w => w.Kind == SafetyWarningKind.UnreachableLayer);
    }
}
