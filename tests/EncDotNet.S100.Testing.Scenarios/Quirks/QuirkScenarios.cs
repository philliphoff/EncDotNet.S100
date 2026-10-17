using EncDotNet.S100.Testing.Scenarios.World;

namespace EncDotNet.S100.Testing.Scenarios.Quirks;

/// <summary>
/// "Spit across scales" with one class of producer quirk switched on each
/// (#943). <c>QuirkScenarioTests</c> pins how the library handles each
/// one today, so a change in that handling is deliberate.
/// </summary>
/// <remarks>
/// Dataset names are those of <see cref="SyntheticScenarios.SpitAcrossScales"/>:
/// <c>101ZZ00SPIT5</c> (harbour), <c>101ZZ00SPIT3</c> (coastal),
/// <c>102ZZ00SPIT</c>, <c>104ZZ00SPIT_STN</c> and <c>104ZZ00SPIT_GRD</c>.
/// </remarks>
public static class QuirkScenarios
{
    /// <summary>The S-101 harbour cell.</summary>
    public const string HarbourCell = "101ZZ00SPIT5";

    /// <summary>The S-101 coastal cell.</summary>
    public const string CoastalCell = "101ZZ00SPIT3";

    /// <summary>The S-102 surface.</summary>
    public const string Surface = "102ZZ00SPIT";

    /// <summary>The S-104 station series.</summary>
    public const string Stations = "104ZZ00SPIT_STN";

    /// <summary>The S-104 grid.</summary>
    public const string WaterLevelGrid = "104ZZ00SPIT_GRD";

    /// <summary>A second harbour-band cell that overlaps <see cref="HarbourCell"/>.</summary>
    public const string OverlappingCell = "101ZZ00SPIT5B";

    /// <summary>A vertical datum other than the world's: mean lower low water (S-100 code 12).</summary>
    public const int OtherVerticalDatum = 12;

    /// <summary>The S-102 no-data hole: a 200 m square round <c>channel-west</c>.</summary>
    public static LocalRect NoDataHole { get; } = new(900, 2900, 1100, 3100);

    /// <summary>The S-104 record gap: 06:00 to 09:00 UTC on 1 March 2026.</summary>
    public static RecordGap Gap { get; } = new(new DateTime(2026, 3, 1, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc));

    /// <summary>The end of the expired S-104 run: 12:00 UTC on 1 March 2026.</summary>
    public static DateTime RunEnd { get; } = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>The area of the spiked <c>Sandwave</c>, west of the spit.</summary>
    public static LocalRect SpikedSandwave { get; } = new(1600, 3600, 2200, 4000);

    /// <summary>The overlap of <see cref="OverlappingCell"/> with the harbour cell.</summary>
    public static LocalRect Overlap { get; } = new(0, 3200, 3000, 6000);

    /// <summary>Every quirk scenario.</summary>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenarios.</returns>
    public static IReadOnlyList<ScenarioDefinition> All(ulong seed = SyntheticScenarios.DefaultSeed) =>
    [
        SwappedAxisOrder(seed),
        S102DatumMissing(seed),
        DatumMismatch(seed),
        DisplayScales(seed),
        NoDataHoles(seed),
        WaterLevelGapAndExpiredRun(seed),
        UnusualCrs(seed),
        Longitudes0To360(seed),
        MixedDimensionArea(seed),
        OverlappingSameBandCells(seed),
    ];

    /// <summary>
    /// The S-102 surface and both S-104 datasets with latitude and longitude
    /// exchanged (#650).
    /// </summary>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition SwappedAxisOrder(ulong seed = SyntheticScenarios.DefaultSeed) =>
        With(seed, "quirk-swapped-axis-order", (Surface, [new SwappedAxisOrder()]), (Stations, [new SwappedAxisOrder()]), (WaterLevelGrid, [new SwappedAxisOrder()]));

    /// <summary>The S-102 surface with no <c>verticalDatum</c> (#939).</summary>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition S102DatumMissing(ulong seed = SyntheticScenarios.DefaultSeed) =>
        With(seed, "quirk-s102-datum-missing", (Surface, [new DeclaredVerticalDatum(null)]));

    /// <summary>
    /// The S-102 surface on mean lower low water while the S-104 water
    /// levels stay on lowest astronomical tide.
    /// </summary>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition DatumMismatch(ulong seed = SyntheticScenarios.DefaultSeed) =>
        With(seed, "quirk-datum-mismatch", (Surface, [new DeclaredVerticalDatum(OtherVerticalDatum)]));

    /// <summary>
    /// The harbour cell's display scales inverted (minimum 3 000, maximum
    /// 22 000) and the coastal cell's left out.
    /// </summary>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition DisplayScales(ulong seed = SyntheticScenarios.DefaultSeed) =>
        With(
            seed,
            "quirk-display-scales",
            (HarbourCell, [new DeclaredDisplayScales(3000, 22000)]),
            (CoastalCell, [new DeclaredDisplayScales(null, null)]));

    /// <summary>The S-102 surface with a no-data hole round <c>channel-west</c> (<see cref="NoDataHole"/>).</summary>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition NoDataHoles(ulong seed = SyntheticScenarios.DefaultSeed) =>
        With(seed, "quirk-s102-nodata-holes", (Surface, [new NoDataHoles([NoDataHole])]));

    /// <summary>
    /// Both S-104 datasets with a gap from 06:00 to 09:00 (<see cref="Gap"/>),
    /// and a run that ends at 12:00 (<see cref="RunEnd"/>), so later times are
    /// past it.
    /// </summary>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition WaterLevelGapAndExpiredRun(ulong seed = SyntheticScenarios.DefaultSeed) =>
        With(
            seed,
            "quirk-s104-gap-and-expired-run",
            (Stations, [Gap, new ForecastRunEnds(RunEnd)]),
            (WaterLevelGrid, [Gap, new ForecastRunEnds(RunEnd)]));

    /// <summary>The S-102 surface declaring ETRS89 (EPSG:4258) while its coordinates are WGS 84.</summary>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition UnusualCrs(ulong seed = SyntheticScenarios.DefaultSeed) =>
        With(seed, "quirk-unusual-crs", (Surface, [new DeclaredHorizontalCrs(4258)]));

    /// <summary>The S-102 surface and both S-104 datasets in the 0…360° longitude frame (#773).</summary>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition Longitudes0To360(ulong seed = SyntheticScenarios.DefaultSeed) =>
        With(
            seed,
            "quirk-longitude-0-360",
            (Surface, [new LongitudeFrame0To360()]),
            (Stations, [new LongitudeFrame0To360()]),
            (WaterLevelGrid, [new LongitudeFrame0To360()]));

    /// <summary>
    /// The harbour cell with a <c>Sandwave</c> area whose ring has a
    /// 150 m zero-width spike (<see cref="SpikedSandwave"/>, #891).
    /// </summary>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition MixedDimensionArea(ulong seed = SyntheticScenarios.DefaultSeed) =>
        With(seed, "quirk-mixed-dimension-area", (HarbourCell, [new SpikedArea("Sandwave", SpikedSandwave, 150)]));

    /// <summary>
    /// A second harbour-band cell (<see cref="OverlappingCell"/>) over the
    /// north-west of the harbour (<see cref="Overlap"/>), charting every depth
    /// 1 m shoaler: two cells of the same band that disagree (#947).
    /// </summary>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition OverlappingSameBandCells(ulong seed = SyntheticScenarios.DefaultSeed)
    {
        var harbour = SyntheticScenarios.SyntheticSoundCells[0];
        var overlapping = harbour with
        {
            DatasetName = OverlappingCell,
            Title = "Synthetic Sound harbour, older survey (SYNTHETIC, fictitious; not for navigation)",
            Extent = Overlap,
            FoidSubdivision = 3,
        };
        return With(seed, "quirk-overlapping-same-band-cells", (OverlappingCell, [new ShoalBias(1.0)])) with
        {
            Cells = [.. SyntheticScenarios.SyntheticSoundCells, overlapping],
        };
    }

    private static ScenarioDefinition With(ulong seed, string name, params (string Dataset, ProducerQuirk[] Quirks)[] quirks) =>
        SyntheticScenarios.SpitAcrossScales(seed) with
        {
            Name = name,
            Quirks = quirks.ToDictionary(q => q.Dataset, q => (IReadOnlyList<ProducerQuirk>)q.Quirks, StringComparer.Ordinal),
        };
}
