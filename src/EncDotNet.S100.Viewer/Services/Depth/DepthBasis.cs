using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>What the depths drawn on the chart are relative to (#1191).</summary>
internal enum DepthBasisKind
{
    /// <summary>No depth-bearing dataset is loaded and active: the indicator is hidden.</summary>
    None,

    /// <summary>Charted depths, relative to each dataset's vertical datum.</summary>
    Charted,

    /// <summary>Charted depth plus the S-104 water level (#1132).</summary>
    Adjusted,
}

/// <summary>Why the chart shows charted depths when water level exists.</summary>
internal enum DepthBasisReason
{
    /// <summary>The kind is <see cref="DepthBasisKind.None"/> or <see cref="DepthBasisKind.Adjusted"/>, or no S-104 meets the view.</summary>
    None,

    /// <summary>S-104 meets the view, and chart adjustment is off.</summary>
    NotApplied,

    /// <summary>Adjustment is on but unavailable in the view: see <see cref="DepthBasis.WaterLevelStatus"/>.</summary>
    WaterLevel,

    /// <summary>
    /// Adjustment is on and water level is available, but every depth source
    /// in view is a product the chart draws as charted (translated S-57
    /// cells): see <see cref="DepthBasis.NotAdjustedSources"/>.
    /// </summary>
    ProductNotAdjusted,
}

/// <summary>A dataset that supplies depths or water level in the view.</summary>
/// <param name="DatasetId">The dataset's display name, as the Datasets panel shows it.</param>
/// <param name="Spec">The product: "S-101", "S-102", "S-104", ….</param>
/// <param name="VerticalDatumCode">The S-100 vertical datum code, or <see langword="null"/> when not declared.</param>
/// <param name="Start">S-104 only: the first time step (UTC).</param>
/// <param name="End">S-104 only: the last time step (UTC).</param>
/// <param name="Kind">S-104 only: grid or station.</param>
/// <param name="Run">S-104 only: the run (issue time, UTC), when declared.</param>
/// <summary>How much of an adjusted water level is a tide prediction (tide predictions design D9).</summary>
internal enum PredictedShare
{
    /// <summary>None: S-104 only, or not adjusted.</summary>
    None,

    /// <summary>Part: S-104 covers part of the view and predictions the rest.</summary>
    Part,

    /// <summary>All: only predictions adjust the view.</summary>
    All,
}

internal sealed record DepthBasisSource(
    string DatasetId,
    string Spec,
    int? VerticalDatumCode,
    DateTime? Start = null,
    DateTime? End = null,
    WaterLevelSourceKind? Kind = null,
    DateTime? Run = null);

/// <summary>
/// What the depths and safety contour on the chart are relative to, for the
/// status-bar indicator, the depth card and <c>get_depth_basis</c>. Built by
/// <see cref="DepthBasisEvaluator"/>; turned into text only by
/// <see cref="DepthBasisText"/>.
/// </summary>
/// <param name="Kind">Charted, adjusted, or hidden.</param>
/// <param name="Reason">Why charted when water level exists.</param>
/// <param name="WaterLevelStatus">Set when <paramref name="Reason"/> is <see cref="DepthBasisReason.WaterLevel"/>, or <paramref name="Kind"/> is <see cref="DepthBasisKind.Adjusted"/>.</param>
/// <param name="ViewTime">The Timeline's view time the basis was evaluated at.</param>
/// <param name="WaterLevelStepTime">The time the adjusted layers are for (#1132): the last completed adjustment, not the slider's time while it moves.</param>
/// <param name="CoversWholeView">False for "part of view" (#1132).</param>
/// <param name="Datum">Whether the depth sources and water-level sources in view share a datum, when both are present.</param>
/// <param name="DepthSources">The depth-bearing datasets in the view.</param>
/// <param name="WaterLevelSources">The active S-104 datasets that meet the view.</param>
/// <param name="NotAdjusted">The depth sources in view refused for their datum (#1132) or their product.</param>
internal sealed record DepthBasis(
    DepthBasisKind Kind,
    DepthBasisReason Reason,
    WaterLevelStatus? WaterLevelStatus,
    DateTimeOffset? ViewTime,
    DateTime? WaterLevelStepTime,
    bool CoversWholeView,
    DatumAgreement? Datum,
    IReadOnlyList<DepthBasisSource> DepthSources,
    IReadOnlyList<DepthBasisSource> WaterLevelSources,
    IReadOnlyList<DepthBasisSource>? NotAdjusted = null)
{
    /// <summary>
    /// How much of the adjusted water level is a tide prediction (tide
    /// predictions design D9): none, part (S-104 covers the rest) or all.
    /// </summary>
    public PredictedShare Predicted { get; init; }

    /// <summary>The predicted stations in reach of the view, as "Seattle 9447130", nearest the centre first.</summary>
    public IReadOnlyList<string> PredictedStations { get; init; } = [];

    /// <summary>
    /// Whether the DEPTHS flyout offers <b>Use tide predictions</b> (D2):
    /// predictions are off, no S-104 meets the view and a station is in reach.
    /// </summary>
    public bool OffersPredictions { get; init; }

    /// <summary>Whether predictions are in use and NOAA has newer data than the station list (D15, D21).</summary>
    public bool PredictionsStale { get; init; }

    /// <summary>The station list's date, for the stale notice.</summary>
    public DateTimeOffset? StationListDate { get; init; }

    /// <summary>
    /// The depth sources in view that adjustment refuses for their datum
    /// (#1132); empty when adjustment is off or adjusts every source.
    /// </summary>
    public IReadOnlyList<DepthBasisSource> NotAdjustedSources => NotAdjusted ?? [];

    /// <summary>No depth-bearing dataset loaded: the indicator is hidden.</summary>
    public static DepthBasis Hidden { get; } = new(
        DepthBasisKind.None, DepthBasisReason.None, null, null, null, true, null, [], []);
}
