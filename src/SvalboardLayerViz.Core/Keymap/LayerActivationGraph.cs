using SvalboardLayerViz.Core.Layout;
using SvalboardLayerViz.Core.Models;

namespace SvalboardLayerViz.Core.Keymap;

/// <summary>
/// Computes, for each layer, the shortest ordered path of activator keys that
/// reach it from L0 — e.g. [MO(2)@L0, MO(3)@L2] for L3.
///
/// The path is used by <see cref="TransparentKeyResolver"/> to scope TRNS
/// fallthrough to layers that are actually active at runtime, matching QMK
/// semantics (TRNS consults active layers, not every lower layer).
///
/// Svalboard-specific: thumb (row 0|5, col 5) hard-presses physically engage
/// (row, 2) as well, so an activator at col 5 co-activates a col 2 activator
/// on the same source layer. Handled via
/// <see cref="SvalboardLayout.GetCoactivatedPositions"/>.
/// </summary>
public static class LayerActivationGraph
{
    /// <summary>
    /// Builds the per-layer activation path map. Layers with no activator are
    /// given an empty path (caller treats as orphan → fallback stack [0, L]).
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<ActivationHop>> Build(IReadOnlyList<Layer> layers)
    {
        var result = new Dictionary<int, IReadOnlyList<ActivationHop>>(layers.Count);
        if (layers.Count == 0) return result;

        // Reverse edges: for each target layer, list of activators that reach it.
        // A co-activated (press-through) activator is also attached to its own
        // separate target — each hop still reflects a single MO/LT/etc. key.
        // byPosition indexes every hop by its origin (sourceLayer, row, col) so
        // press-through co-hop lookup is O(1); the key is unique because a
        // position holds at most one keycode per layer.
        var incoming = new Dictionary<int, List<ActivationHop>>();
        var byPosition = new Dictionary<(int SourceLayer, int Row, int Col), ActivationHop>();
        foreach (var layer in layers)
        {
            foreach (var key in layer.Keys)
            {
                if (!key.IsLayerSwitch || key.TargetLayer is not int target) continue;
                if (target == layer.Index) continue; // self-loops (MO(N) on L N) don't advance the stack
                var hop = new ActivationHop(layer.Index, key.Row, key.Col, target, key.SwitchType);
                if (!incoming.TryGetValue(target, out var list))
                    incoming[target] = list = new List<ActivationHop>();
                list.Add(hop);
                byPosition[(layer.Index, key.Row, key.Col)] = hop;
            }
        }

        // Stable tie-break: lowest source layer, then lowest (row, col).
        foreach (var list in incoming.Values)
            list.Sort(CompareHop);

        result[0] = Array.Empty<ActivationHop>();
        foreach (var layer in layers)
        {
            if (layer.Index == 0) continue;
            result[layer.Index] = BuildPath(layer.Index, incoming, byPosition);
        }

        return result;
    }

    /// <summary>
    /// Canonical one-call keymap resolution: builds the activation graph for
    /// <paramref name="layers"/>, attaches each layer's
    /// <see cref="Layer.ActivationPath"/>, then resolves TRNS via the active
    /// stack (matches QMK runtime). Mutates the list in place.
    /// </summary>
    public static void ResolveInto(List<Layer> layers)
    {
        var activationPaths = Build(layers);
        for (var i = 0; i < layers.Count; i++)
        {
            if (activationPaths.TryGetValue(i, out var path))
                layers[i] = layers[i] with { ActivationPath = path };
        }
        TransparentKeyResolver.Resolve(layers, activationPaths);
    }

    /// <summary>
    /// Returns the flattened active-layer stack for a given layer and its path:
    /// <c>[0, hop1.Target, …, pathLast.Target]</c>. When the path is empty the
    /// stack degrades to <c>[0, layerIndex]</c> (orphan fallback). For L0 the
    /// stack is just <c>[0]</c>.
    /// </summary>
    public static IReadOnlyList<int> GetActiveStack(int layerIndex, IReadOnlyList<ActivationHop> path)
    {
        if (layerIndex == 0) return new[] { 0 };
        if (path.Count == 0) return new[] { 0, layerIndex }; // orphan fallback

        var stack = new int[path.Count + 1];
        stack[0] = 0;
        for (var i = 0; i < path.Count; i++)
            stack[i + 1] = path[i].TargetLayer;
        return stack;
    }

    private static IReadOnlyList<ActivationHop> BuildPath(
        int target,
        Dictionary<int, List<ActivationHop>> incoming,
        IReadOnlyDictionary<(int SourceLayer, int Row, int Col), ActivationHop> byPosition)
    {
        // BFS from target back toward L0 via reverse edges, tracking the
        // shortest path per visited node. Press-through coupling is applied on
        // expansion: when we follow a hop with a co-activated position that is
        // itself a layer-switch, the co-hop is appended as a sibling step.
        var queue = new Queue<int>();
        var parent = new Dictionary<int, ActivationHop>();
        var parentCoHop = new Dictionary<int, ActivationHop?>();
        var visited = new HashSet<int> { target };
        queue.Enqueue(target);

        int? rootParent = null;
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == 0) { rootParent = 0; break; }
            if (!incoming.TryGetValue(cur, out var hops)) continue;

            foreach (var hop in hops)
            {
                if (!visited.Add(hop.SourceLayer)) continue;
                parent[hop.SourceLayer] = hop;
                parentCoHop[hop.SourceLayer] = FindCoHop(hop, byPosition);
                if (hop.SourceLayer == 0) { rootParent = 0; queue.Clear(); break; }
                queue.Enqueue(hop.SourceLayer);
            }
        }

        if (rootParent is null) return Array.Empty<ActivationHop>();

        // Rebuild forward path: walk from L0 upward via the chosen hops. For
        // a press-through pair the col-2 co-hop comes first (physically pressed
        // first on a soft press, with col 5 added on harder press).
        var forward = new List<ActivationHop>();
        var node = 0;
        var safety = parent.Count + 2;
        while (node != target && safety-- > 0)
        {
            if (!parent.TryGetValue(node, out var hop)) return Array.Empty<ActivationHop>();
            if (parentCoHop.TryGetValue(node, out var co) && co is ActivationHop coHop)
                forward.Add(coHop);
            forward.Add(hop);
            node = hop.TargetLayer;
        }
        return node == target ? forward : Array.Empty<ActivationHop>();
    }

    /// <summary>
    /// If <paramref name="hop"/>'s activator position has a Svalboard
    /// press-through co-activated position, and that co-key is itself a
    /// layer-switch on the same source layer, return it as a sibling hop.
    /// </summary>
    private static ActivationHop? FindCoHop(
        ActivationHop hop,
        IReadOnlyDictionary<(int SourceLayer, int Row, int Col), ActivationHop> byPosition)
    {
        foreach (var (coRow, coCol) in SvalboardLayout.GetCoactivatedPositions(hop.Row, hop.Col))
            if (byPosition.TryGetValue((hop.SourceLayer, coRow, coCol), out var co))
                return co;
        return null;
    }

    private static int CompareHop(ActivationHop a, ActivationHop b)
    {
        var c = a.SourceLayer.CompareTo(b.SourceLayer);
        if (c != 0) return c;
        c = a.Row.CompareTo(b.Row);
        if (c != 0) return c;
        return a.Col.CompareTo(b.Col);
    }
}
