using SvalboardLayerViz.Core.Layout;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// ViewModel for a cluster background shape in the board visualization.
/// Computed from the bounding box of key positions in each cluster.
/// </summary>
public class ClusterViewModel
{
    private const double Scale = 60.0;
    private const double Padding = 4.0; // pixels of padding around the bounding box

    public string Name { get; }
    public double Left { get; }
    public double Top { get; }
    public double Width { get; }
    public double Height { get; }

    public ClusterViewModel(string name, IEnumerable<KeyPosition> positions)
    {
        Name = name;
        var list = positions.ToList();

        var minX = list.Min(p => p.X) * Scale - Padding;
        var minY = list.Min(p => p.Y) * Scale - Padding;
        var maxX = list.Max(p => p.X + p.Width) * Scale + Padding;
        var maxY = list.Max(p => p.Y + p.Height) * Scale + Padding;

        Left = minX;
        Top = minY;
        Width = maxX - minX;
        Height = maxY - minY;
    }

    public static IReadOnlyList<ClusterViewModel> BuildFromLayout()
    {
        return SvalboardLayout.GetKeyPositions()
            .GroupBy(p => p.Cluster)
            .Select(g => new ClusterViewModel(g.Key, g))
            .ToList();
    }
}
