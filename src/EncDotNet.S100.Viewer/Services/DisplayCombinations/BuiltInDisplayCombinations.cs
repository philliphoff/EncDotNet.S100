using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services.DisplayCombinations;

/// <summary>
/// The display combinations that ship with SoundCharts (design note §2). They
/// are kept in code so their labels are localised, and can't be edited or
/// deleted. The first three are the <b>Build up the picture</b> ladder: chart,
/// then bathymetry, then water level and the rest.
/// </summary>
internal static class BuiltInDisplayCombinations
{
    /// <summary>Id of <b>Charted only</b>.</summary>
    public const string ChartedId = "charted";

    /// <summary>Id of <b>Chart + bathymetry</b>.</summary>
    public const string ChartedBathymetryId = "charted-bathymetry";

    /// <summary>Id of <b>Pilotage</b>.</summary>
    public const string PilotageId = "pilotage";

    /// <summary>Id of <b>Passage planning</b>.</summary>
    public const string PassagePlanningId = "passage-planning";

    /// <summary>Id of <b>Everything loaded</b>.</summary>
    public const string AllLoadedId = "all-loaded";

    /// <summary>
    /// The S-101 portrayal catalogue's <b>Important Text</b> viewing group
    /// (viewing-group layer 101).
    /// </summary>
    internal static readonly IReadOnlySet<int> S101ImportantText = new HashSet<int> { 11 };

    /// <summary>
    /// The S-101 portrayal catalogue's <b>Other Text</b> viewing groups
    /// (viewing-group layer 102: names, light descriptions and all other
    /// chart text).
    /// </summary>
    internal static readonly IReadOnlySet<int> S101OtherText =
        new HashSet<int> { 21, 23, 24, 25, 26, 27, 28, 29, 30, 32 };

    /// <summary>The weather layers a combination can switch map-wide.</summary>
    internal static readonly IReadOnlyList<string> WeatherLayerKeys =
    [
        PressureIsobarStyle.Key,
        WindSpeedStyle.Key,
        WaveDirectionStyle.Key,
    ];

    /// <summary>The S-111 station (non-grid) arrows sub-layer.</summary>
    internal const string S111StationsKey = "s111.stations";

    /// <summary>The S-111 grid arrows sub-layer.</summary>
    internal const string S111ArrowsKey = "s111.arrows";

    /// <summary>Creates the built-ins, in menu order.</summary>
    public static IReadOnlyList<DisplayCombination> Create()
    {
        var chartText = new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["S-101"] = S101OtherText,
        };
        var chartTextShown = new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["S-101"] = S101ImportantText,
        };

        var charted = DisplayCombination.Create(ChartedId, Strings.DisplayCombination_Charted, DisplayCombinationOrigin.BuiltIn) with
        {
            Products = Products("S-101"),
            Category = EcdisDisplayCategory.Standard,
            HiddenViewingGroups = chartText,
            ShownViewingGroups = chartTextShown,
            Description = Strings.DisplayCombination_Charted_Description,
            LadderStep = 1,
        };

        var bathymetry = charted with
        {
            Id = ChartedBathymetryId,
            Label = Strings.DisplayCombination_ChartedBathymetry,
            Products = Products("S-101", "S-102"),
            Description = Strings.DisplayCombination_ChartedBathymetry_Description,
            LadderStep = 2,
        };

        var pilotageLayers = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var key in WeatherLayerKeys)
            pilotageLayers[key] = false;
        var pilotage = DisplayCombination.Create(PilotageId, Strings.DisplayCombination_Pilotage, DisplayCombinationOrigin.BuiltIn) with
        {
            Products = Products("S-101", "S-102", "S-104", "S-111", "S-124", "S-125", "S-201"),
            Category = EcdisDisplayCategory.Standard,
            LayerVisibility = pilotageLayers,
            SubLayerVisibility = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [S104DatasetProcessor.IsolineLayerKey] = false,
                [S104DatasetProcessor.StationLayerKey] = true,
                [S111ArrowsKey] = true,
                [S111StationsKey] = false,
            },
            Description = Strings.DisplayCombination_Pilotage_Description,
            LadderStep = 3,
        };

        var planningProducts = new HashSet<string>(DisplayCombinationProducts.All(), StringComparer.OrdinalIgnoreCase);
        planningProducts.Remove("S-111");
        planningProducts.Remove(DisplayCombinationProducts.Weather);
        var planning = DisplayCombination.Create(PassagePlanningId, Strings.DisplayCombination_PassagePlanning, DisplayCombinationOrigin.BuiltIn) with
        {
            Products = planningProducts,
            Category = EcdisDisplayCategory.OtherInformation,
            SubLayerVisibility = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [S104DatasetProcessor.IsolineLayerKey] = true,
            },
            Description = Strings.DisplayCombination_PassagePlanning_Description,
        };

        // Every product in, nothing else set: today's behaviour.
        var all = DisplayCombination.Create(AllLoadedId, Strings.DisplayCombination_AllLoaded, DisplayCombinationOrigin.BuiltIn) with
        {
            Description = Strings.DisplayCombination_AllLoaded_Description,
        };

        return [charted, bathymetry, pilotage, planning, all];
    }

    private static IReadOnlySet<string> Products(params string[] keys) =>
        new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
}
