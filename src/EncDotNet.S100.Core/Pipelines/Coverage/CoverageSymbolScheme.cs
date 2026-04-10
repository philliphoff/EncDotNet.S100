namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Maps value ranges to oriented symbols for overlay rendering of gridded coverage data
/// (e.g. S-111 current arrows).
/// </summary>
public sealed class CoverageSymbolScheme
{
    /// <summary>The coverage field used to select the symbol band (e.g. "surfaceCurrentSpeed").</summary>
    public required string ValueFieldName { get; init; }

    /// <summary>The coverage field whose value supplies the rotation angle in degrees clockwise from north.</summary>
    public required string RotationFieldName { get; init; }

    /// <summary>The ordered list of symbol bands mapping value ranges to symbol references.</summary>
    public required IReadOnlyList<SymbolBand> Bands { get; init; }

    /// <summary>
    /// Resolves a value to its matching symbol band, or <c>null</c> for no-data / out-of-range.
    /// </summary>
    public SymbolBand? Resolve(float value)
    {
        for (int i = 0; i < Bands.Count; i++)
        {
            var band = Bands[i];
            if (value >= band.MinValue && value < band.MaxValue)
                return band;
        }

        return null;
    }
}

/// <summary>
/// A single band in a coverage symbol scheme, mapping a value range to
/// a symbol reference with scaling behaviour.
/// </summary>
public sealed class SymbolBand
{
    public required float MinValue { get; init; }
    public required float MaxValue { get; init; }

    /// <summary>Symbol reference name (e.g. "SCAROW01").</summary>
    public required string SymbolRef { get; init; }

    /// <summary>
    /// When <c>true</c>, the symbol scale is computed as <see cref="ScaleFactor"/> × value.
    /// When <c>false</c>, <see cref="ScaleFactor"/> is a fixed scale.
    /// </summary>
    public bool ScaleByValue { get; init; }

    /// <summary>
    /// Either the fixed scale factor or the per-unit multiplier
    /// (depending on <see cref="ScaleByValue"/>).
    /// </summary>
    public float ScaleFactor { get; init; } = 1.0f;

    public string? Label { get; init; }
}
