namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Maps value ranges to colours for portrayal of gridded coverage data.
/// </summary>
public sealed class CoverageColorScheme
{
    public required string FieldName { get; init; }
    public required IReadOnlyList<ColorBand> Bands { get; init; }

    /// <summary>
    /// Optional colour applied to cells whose value equals the
    /// coverage's no-data sentinel. When <c>null</c>, the renderer
    /// leaves no-data cells transparent (legacy behaviour). When set,
    /// the renderer paints them with this hex colour — typically the
    /// portrayal catalogue's <c>NODTA</c> token resolved against the
    /// active palette (S-100 Part 9 colour-table semantics).
    /// </summary>
    public string? NoDataColor { get; init; }

    /// <summary>
    /// Resolves a value to a colour hex string using the bands.
    /// Returns <c>null</c> for no-data or out-of-range values.
    /// </summary>
    public string? Resolve(float value)
    {
        for (int i = 0; i < Bands.Count; i++)
        {
            var band = Bands[i];
            if (value >= band.MinValue && value < band.MaxValue)
                return band.Color;
        }

        return null;
    }
}

/// <summary>
/// A single band in a coverage color scheme mapping a value range to a colour.
/// </summary>
public sealed class ColorBand
{
    public required float MinValue { get; init; }
    public required float MaxValue { get; init; }
    public required string Color { get; init; }
    public string? Label { get; init; }
}
