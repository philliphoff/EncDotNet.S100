namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Maps value ranges to colours for portrayal of gridded coverage data.
/// </summary>
public sealed class CoverageColorScheme
{
    public required string FieldName { get; init; }
    public required IReadOnlyList<ColorBand> Bands { get; init; }

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
