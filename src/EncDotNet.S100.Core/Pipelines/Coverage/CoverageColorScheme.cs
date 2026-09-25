namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Maps value ranges to colours for portrayal of gridded coverage data.
/// </summary>
public sealed class CoverageColorScheme
{
    /// <summary>
    /// Name of the coverage value field the bands apply to (a
    /// <see cref="CoverageValueField.Name"/>, e.g. <c>"depth"</c>).
    /// </summary>
    public required string FieldName { get; init; }

    /// <summary>
    /// The colour bands, evaluated in order by <see cref="Resolve"/>; the
    /// first band whose range contains the value wins.
    /// </summary>
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
    /// <param name="value">The cell value, in the field's units.</param>
    /// <returns>
    /// The <see cref="ColorBand.Color"/> of the first band whose
    /// [<see cref="ColorBand.MinValue"/>, <see cref="ColorBand.MaxValue"/>)
    /// range contains <paramref name="value"/>, or <c>null</c> when none does.
    /// </returns>
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
    /// <summary>Lower bound of the band (inclusive), in the field's units.</summary>
    public required float MinValue { get; init; }

    /// <summary>Upper bound of the band (exclusive), in the field's units.</summary>
    public required float MaxValue { get; init; }

    /// <summary>Fill colour for values in the band, as a hex string (e.g. <c>"#C9EDFF"</c>) already resolved from the active palette.</summary>
    public required string Color { get; init; }

    /// <summary>Optional legend label for the band, or <c>null</c>.</summary>
    public string? Label { get; init; }
}
