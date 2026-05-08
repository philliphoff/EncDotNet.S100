namespace EncDotNet.S100.Pipelines;

/// <summary>
/// Mariner-configurable display preferences used by S-100 portrayal rules
/// (S-100 Part 9 §4.2 — "Mariner Selections"). These values are independent
/// of the current display viewport and are typically persisted across
/// sessions per user preference.
/// </summary>
/// <remarks>
/// All depth fields are stored in metres regardless of <see cref="DepthUnit"/>;
/// the unit only affects how depth values are presented to the mariner in
/// the viewer (Pick panel, legend, status bar, settings inputs). Default
/// values for the boolean toggles match the defaults declared by the bundled
/// S-101 portrayal catalogue (<c>portrayal_catalogue.xml</c> §<c>context</c>).
/// </remarks>
public sealed record MarinerSettings
{
    /// <summary>Safety contour depth in metres (S-101 PC parameter <c>SafetyContour</c>).</summary>
    public double SafetyContour { get; init; } = 30.0;

    /// <summary>Safety depth in metres for sounding selection (S-101 PC parameter <c>SafetyDepth</c>).</summary>
    public double SafetyDepth { get; init; } = 30.0;

    /// <summary>Shallow contour depth in metres (S-101 PC parameter <c>ShallowContour</c>).</summary>
    public double ShallowContour { get; init; } = 2.0;

    /// <summary>Deep contour depth in metres (S-101 PC parameter <c>DeepContour</c>).</summary>
    public double DeepContour { get; init; } = 30.0;

    /// <summary>
    /// Display unit used by the viewer when presenting depth values to the
    /// mariner (Pick panel, S-102 legend, status bar, settings inputs).
    /// Canonical depth storage in this record is always metres; this only
    /// affects formatting/parsing.
    /// </summary>
    public DepthUnit DepthUnit { get; init; } = DepthUnit.Metres;

    /// <summary>
    /// Whether to use the four-shade depth area scheme (S-101 PC parameter
    /// <c>FourShades</c>). When <c>false</c> only the two-shade scheme is
    /// used, in which case <see cref="ShallowContour"/> and
    /// <see cref="DeepContour"/> are typically ignored by the catalogue.
    /// </summary>
    public bool FourShades { get; init; } = false;

    /// <summary>
    /// Whether to highlight isolated dangers in shallow water (S-101 PC
    /// parameter <c>ShallowWaterDangers</c>).
    /// </summary>
    public bool ShallowWaterDangers { get; init; } = true;

    /// <summary>
    /// Whether to draw plain (un-symbolised) area boundaries (S-101 PC
    /// parameter <c>PlainBoundaries</c>).
    /// </summary>
    public bool PlainBoundaries { get; init; } = true;

    /// <summary>
    /// Whether to use simplified point symbols (S-101 PC parameter
    /// <c>SimplifiedSymbols</c>).
    /// </summary>
    public bool SimplifiedSymbols { get; init; } = false;

    /// <summary>
    /// Whether to draw light sectors as full lines extending to their
    /// nominal range (S-101 PC parameter <c>FullLightLines</c>).
    /// </summary>
    public bool FullLightLines { get; init; } = false;

    /// <summary>
    /// Whether the chart is being drawn under a radar overlay (S-101 PC
    /// parameter <c>RadarOverlay</c>).
    /// </summary>
    public bool RadarOverlay { get; init; } = false;

    /// <summary>
    /// Whether to ignore the cell scale-minimum attribute and always
    /// portray features regardless of the user's scale (S-101 PC parameter
    /// <c>IgnoreScaleMinimum</c>).
    /// </summary>
    public bool IgnoreScaleMinimum { get; init; } = false;

    /// <summary>
    /// Preferred 3-letter ISO 639-2/B language code for chart text (S-101
    /// PC parameter <c>NationalLanguage</c>). An empty string means "use
    /// the catalogue's declared default" — no override is sent to the
    /// portrayal engine.
    /// </summary>
    public string NationalLanguage { get; init; } = "";

    /// <summary>Default mariner settings (safety contour 30 m, etc.).</summary>
    public static MarinerSettings Default { get; } = new();
}
