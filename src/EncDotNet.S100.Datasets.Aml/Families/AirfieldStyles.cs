namespace EncDotNet.S100.Datasets.Aml.Families;

/// <summary>
/// Airfields and runways, in the <see cref="AmlFamily.Air"/> family: common
/// in the AML 3.0 flight information trials (FAI, MFI), and in RAL.
/// </summary>
/// <remarks>
/// Both are IHO S-57 chart classes (decision 19): the ENC draws its own
/// symbol where it has the feature, and AML adds a chart-like glyph in the
/// AML frame at a point (<see cref="ChartLikeGlyphs.Airfield"/>,
/// <see cref="ChartLikeGlyphs.Runway"/>) or the ticked boundary round an
/// area, with the name.
/// </remarks>
public static class AirfieldStyles
{
    /// <summary>Airports and airfields.</summary>
    public static AmlClassStyle Airfield { get; } = new("AIRARE", AmlFamily.Air, AmlClassKind.Area, "Airport or airfield")
    {
        Glyph = ChartLikeGlyphs.Airfield,
        IsChartClass = true,
    };

    /// <summary>Runways.</summary>
    public static AmlClassStyle Runway { get; } = new("RUNWAY", AmlFamily.Air, AmlClassKind.Area, "Runway")
    {
        Glyph = ChartLikeGlyphs.Runway,
        IsChartClass = true,
    };

    /// <summary>The module, in legend order.</summary>
    public static IAmlStyleModule Module { get; } = new AmlStyleModule("Airfields", [Airfield, Runway]);
}
