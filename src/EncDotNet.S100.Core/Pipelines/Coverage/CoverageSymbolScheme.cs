namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Maps value ranges to oriented symbols for overlay rendering of gridded coverage data
/// (e.g. S-111 current arrows).
/// </summary>
public sealed class CoverageSymbolScheme
{
    /// <summary>The coverage field used to select the symbol band (e.g. "surfaceCurrentSpeed").</summary>
    public required string ValueFieldName { get; init; }

    /// <summary>
    /// The coverage field whose value supplies the rotation angle in degrees
    /// clockwise from north, before <see cref="RotationOffsetDegrees"/> is added.
    /// </summary>
    public required string RotationFieldName { get; init; }

    /// <summary>The ordered list of symbol bands mapping value ranges to symbol references.</summary>
    public required IReadOnlyList<SymbolBand> Bands { get; init; }

    /// <summary>
    /// Bands used instead of <see cref="Bands"/> for cells south of the
    /// equator, or <see langword="null"/> (the default) to use
    /// <see cref="Bands"/> everywhere. Wind barbs use this: their feathers go
    /// on the other side of the staff in the southern hemisphere.
    /// </summary>
    public IReadOnlyList<SymbolBand>? SouthernHemisphereBands { get; init; }

    /// <summary>
    /// Degrees added to the rotation field's value before the symbol is drawn.
    /// Defaults to 0, for a field that gives the direction the symbol should
    /// point (S-111 currents flow toward their direction). A field that gives
    /// the direction a quantity comes <em>from</em> (wave direction, wind
    /// direction) drawn with an arrow-shaped symbol uses 180, so the arrow
    /// points the way the waves travel.
    /// </summary>
    public double RotationOffsetDegrees { get; init; }

    /// <summary>
    /// The length, in millimetres, of a symbol drawn at scale 1 — the symbol's
    /// largest dimension, which <see cref="SymbolThinning"/> spaces symbols by.
    /// Defaults to 10 mm, the nominal S-111 arrow height (S-111 Ed 2.0.0
    /// Figure 9-1).
    /// </summary>
    public double NominalSymbolLengthMillimetres { get; init; } = 10.0;

    /// <summary>
    /// The thinning ratio <c>Rmax</c> between the largest symbol's length and the
    /// displayed symbol spacing (see <see cref="SymbolThinning"/>). Defaults to
    /// <see cref="SymbolThinning.DefaultMaxSymbolToSpacingRatio"/>.
    /// </summary>
    public double MaxSymbolToSpacingRatio { get; init; } = SymbolThinning.DefaultMaxSymbolToSpacingRatio;

    /// <summary>
    /// Resolves a value to its matching symbol band, or <c>null</c> for no-data / out-of-range.
    /// </summary>
    /// <param name="value">The cell's value in the <see cref="ValueFieldName"/> field.</param>
    /// <param name="latitude">
    /// The cell's latitude in degrees. Selects <see cref="SouthernHemisphereBands"/>
    /// when it is negative and that list is set; ignored otherwise.
    /// </param>
    public SymbolBand? Resolve(float value, double latitude)
    {
        var bands = latitude < 0 && SouthernHemisphereBands is { } southern ? southern : Bands;
        for (int i = 0; i < bands.Count; i++)
        {
            var band = bands[i];
            if (value >= band.MinValue && value < band.MaxValue)
                return band;
        }

        return null;
    }

    /// <summary>
    /// The drawn rotation, in degrees clockwise from north in [0, 360), for a
    /// rotation field value: <paramref name="direction"/> plus
    /// <see cref="RotationOffsetDegrees"/>.
    /// </summary>
    /// <param name="direction">The cell's value in the <see cref="RotationFieldName"/> field.</param>
    public float RotationFor(float direction)
    {
        if (RotationOffsetDegrees == 0)
            return direction;

        double rotation = (direction + RotationOffsetDegrees) % 360.0;
        return (float)(rotation < 0 ? rotation + 360.0 : rotation);
    }
}

/// <summary>
/// A single band in a coverage symbol scheme, mapping a value range to
/// a symbol reference with scaling behaviour.
/// </summary>
public sealed class SymbolBand
{
    /// <summary>Lower bound of the band (inclusive), in the value field's units.</summary>
    public required float MinValue { get; init; }

    /// <summary>Upper bound of the band (exclusive), in the value field's units.</summary>
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

    /// <summary>
    /// The symbol scale for <paramref name="value"/>: <see cref="ScaleFactor"/> ×
    /// <paramref name="value"/> when <see cref="ScaleByValue"/> is set, otherwise
    /// <see cref="ScaleFactor"/>.
    /// </summary>
    /// <param name="value">The value that selected this band.</param>
    public double ScaleFor(float value) => ScaleByValue ? (double)ScaleFactor * value : ScaleFactor;

    /// <summary>Optional legend label for the band, or <c>null</c>.</summary>
    public string? Label { get; init; }
}
