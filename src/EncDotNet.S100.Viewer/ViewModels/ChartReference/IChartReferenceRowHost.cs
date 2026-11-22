using Avalonia.Media;
using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>What a Chart reference row asks of its list: glyphs, the tile and links.</summary>
internal interface IChartReferenceRowHost
{
    /// <summary>The palette's chart background, which glyphs sit on.</summary>
    IBrush Tile { get; }

    /// <summary>
    /// The row's glyph if it is drawn already; else starts drawing it, sets
    /// it on the row when done (<see cref="ChartReferenceRowViewModel.SetGlyph"/>)
    /// and returns <see langword="null"/>.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The glyph, or <see langword="null"/>.</returns>
    IImage? RequestGlyph(ChartReferenceRowViewModel row);

    /// <summary>Selects an entry's row, switching mode or clearing the search when needed.</summary>
    /// <param name="key">The entry.</param>
    void Open(ReferenceKey key);
}
