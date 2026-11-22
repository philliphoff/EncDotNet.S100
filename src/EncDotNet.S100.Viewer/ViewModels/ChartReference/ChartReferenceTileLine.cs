namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// One line of the Chart reference page's tile grid (#1639): up to
/// <see cref="Columns"/> tiles of one section. The grid is a list of lines
/// and section headers, so it virtualises (S-401 has 926 items).
/// </summary>
/// <param name="tiles">The line's tiles, left to right.</param>
/// <param name="columns">How many tiles a full line holds, so a short last line keeps the grid's widths.</param>
internal sealed class ChartReferenceTileLine(IReadOnlyList<ChartReferenceRowViewModel> tiles, int columns)
{
    /// <summary>The line's tiles, left to right.</summary>
    public IReadOnlyList<ChartReferenceRowViewModel> Tiles { get; } = tiles;

    /// <summary>How many tiles a full line holds.</summary>
    public int Columns { get; } = columns;
}
