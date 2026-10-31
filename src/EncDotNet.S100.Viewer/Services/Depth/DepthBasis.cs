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
}

/// <summary>A dataset that supplies depths or water level in the view.</summary>
/// <param name="DatasetId">The dataset's display name, as the Datasets panel shows it.</param>
/// <param name="Spec">The product: "S-101", "S-102", "S-104", ….</param>
/// <param name="VerticalDatumCode">The S-100 vertical datum code, or <see langword="null"/> when not declared.</param>
/// <param name="Start">S-104 only: the first time step (UTC).</param>
/// <param name="End">S-104 only: the last time step (UTC).</param>
/// <param name="Kind">S-104 only: grid or station.</param>
/// <param name="Run">S-104 only: the run (issue time, UTC), when declared.</param>
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
/// <param name="WaterLevelStepTime">The S-104 step actually used (#1132).</param>
/// <param name="CoversWholeView">False for "part of view" (#1132).</param>
/// <param name="Datum">Whether the depth sources and water-level sources in view share a datum, when both are present.</param>
/// <param name="DepthSources">The depth-bearing datasets in the view.</param>
/// <param name="WaterLevelSources">The active S-104 datasets that meet the view.</param>
internal sealed record DepthBasis(
    DepthBasisKind Kind,
    DepthBasisReason Reason,
    WaterLevelStatus? WaterLevelStatus,
    DateTimeOffset? ViewTime,
    DateTime? WaterLevelStepTime,
    bool CoversWholeView,
    DatumAgreement? Datum,
    IReadOnlyList<DepthBasisSource> DepthSources,
    IReadOnlyList<DepthBasisSource> WaterLevelSources)
{
    /// <summary>No depth-bearing dataset loaded: the indicator is hidden.</summary>
    public static DepthBasis Hidden { get; } = new(
        DepthBasisKind.None, DepthBasisReason.None, null, null, null, true, null, [], []);
}
