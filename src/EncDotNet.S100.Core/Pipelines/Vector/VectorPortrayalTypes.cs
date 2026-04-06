namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Supporting types referenced by the vector portrayal catalogue.
/// </summary>

public sealed class Script
{
    public required string Name { get; init; }
    public required string Source { get; init; }
}

public sealed class SvgSymbol
{
    public required string Name { get; init; }
    public required string SvgContent { get; init; }
    public double PivotX { get; init; }
    public double PivotY { get; init; }
}

public sealed class LineStyle
{
    public required string Name { get; init; }
    public required float Width { get; init; }
    public required string Color { get; init; }
    public float[]? DashPattern { get; init; }
}

public sealed class AreaFill
{
    public required string Name { get; init; }
    public string? Color { get; init; }
    public string? PatternSymbol { get; init; }
}

/// <summary>
/// Controls visibility of features by viewing group assignment.
/// </summary>
public sealed class ViewingGroupController
{
    private readonly Dictionary<int, bool> _visibility = new();

    public IReadOnlyDictionary<int, bool> GroupVisibility => _visibility;

    public void SetVisible(int viewingGroup, bool visible)
    {
        _visibility[viewingGroup] = visible;
    }

    public bool IsVisible(int viewingGroup)
    {
        return !_visibility.TryGetValue(viewingGroup, out var visible) || visible;
    }
}
