using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// The numbers behind a <see cref="RouteFinding"/>, by kind, so a client
/// gets the depth that fired or an area's restrictions without parsing its
/// text: <see cref="DangerDetails"/>, <see cref="ShoalDetails"/>,
/// <see cref="AreaDetails"/>, <see cref="WarningDetails"/> or
/// <see cref="ClearanceDetails"/>.
/// </summary>
/// <remarks>
/// Findings about land, drying ground and chart coverage carry no details:
/// their code, location and <see cref="RouteFinding.Feature"/> say it all.
/// </remarks>
public abstract class RouteFindingDetails
{
    private protected RouteFindingDetails()
    {
    }
}

/// <summary>The depth a charted depth was found shallower than.</summary>
public enum RouteFindingThreshold
{
    /// <summary>The safety contour (<see cref="RouteCheckSettings.SafetyContour"/>).</summary>
    SafetyContour,

    /// <summary>The safety depth (<see cref="RouteCheckSettings.SafetyDepth"/>).</summary>
    SafetyDepth,

    /// <summary>The leg's draft + squat + required UKC.</summary>
    Keel,
}

/// <summary>Which side of the track something is on.</summary>
public enum RouteFindingSide
{
    /// <summary>The track passes over it.</summary>
    OnTrack,

    /// <summary>To port of the track.</summary>
    Port,

    /// <summary>To starboard of the track.</summary>
    Starboard,
}

/// <summary>
/// What kind of navigational hazard a feature is (#1127): what the mariner
/// asks about it. A feature type <see cref="RouteHazardClasses"/> doesn't
/// know is a <see cref="Structure"/>, so no hazard is lost.
/// </summary>
public enum RouteHazardClass
{
    /// <summary>A wreck, obstruction or rock: is there water over it for the keel?</summary>
    Danger,

    /// <summary>A buoy, beacon, daymark, light float or vessel, or AIS aid: is it passed on the right side?</summary>
    AidToNavigation,

    /// <summary>A pile, dolphin, platform, turbine, fishing facility and the like: is it kept clear of?</summary>
    Structure,

    /// <summary>A bridge, span, overhead cable or pipeline, or conveyor: does the vessel fit under it?</summary>
    Overhead,
}

/// <summary>Where something lies from the track (#1127).</summary>
public enum RouteFindingPosition
{
    /// <summary>Within the track clearance (<see cref="RouteCheckSettings.TrackClearance"/>) of the track line.</summary>
    OnTrack,

    /// <summary>Inside the cross-track limits.</summary>
    InCorridor,

    /// <summary>Beyond the limits, within the near band (<see cref="RouteCheckSettings.NearbyDistance"/>).</summary>
    Near,
}

/// <summary>
/// A charted navigational hazard other than a danger in or near the corridor:
/// an aid to navigation, a structure or an overhead feature whose S-101
/// portrayal raises the <c>NavHazard</c> alert (#1127).
/// </summary>
public sealed class HazardDetails : RouteFindingDetails
{
    /// <summary>The feature type, such as <c>LateralBuoy</c> or <c>Pile</c>.</summary>
    public required string FeatureType { get; init; }

    /// <summary>Its hazard class.</summary>
    public required RouteHazardClass HazardClass { get; init; }

    /// <summary>Where it lies from the track.</summary>
    public required RouteFindingPosition Position { get; init; }

    /// <summary>Which side of the track it's on.</summary>
    public RouteFindingSide Side { get; init; }

    /// <summary>How far it is from the track (zero on the track).</summary>
    public Length Offset { get; init; }
}

/// <summary>
/// A charted danger in or near the corridor (<see cref="RouteFindingCodes.DangerInCorridor"/>,
/// <see cref="RouteFindingCodes.DangerNearRoute"/>).
/// </summary>
public sealed class DangerDetails : RouteFindingDetails
{
    /// <summary>The danger's feature type: <c>Wreck</c>, <c>Obstruction</c> or <c>UnderwaterAwashRock</c>.</summary>
    public required string FeatureType { get; init; }

    /// <summary>Its charted depth (<c>valueOfSounding</c>), or <c>null</c> when unknown.</summary>
    public Depth? Depth { get; init; }

    /// <summary>
    /// Its <c>waterLevelEffect</c> code, which says why its depth is unknown
    /// (1 or 2 above water, 4 covers and uncovers, 5 awash), or <c>null</c>.
    /// </summary>
    public int? WaterLevelEffect { get; init; }

    /// <summary>The threshold its depth is shallower than, or <c>null</c> when its depth is unknown (always a danger).</summary>
    public RouteFindingThreshold? ThresholdKind { get; init; }

    /// <summary>The value of <see cref="ThresholdKind"/>, or <c>null</c> when its depth is unknown.</summary>
    public Depth? Threshold { get; init; }

    /// <summary>Which side of the track it's on.</summary>
    public RouteFindingSide Side { get; init; }

    /// <summary>How far it is from the track (zero on the track).</summary>
    public Length Offset { get; init; }
}

/// <summary>
/// Water shallower than a safety setting: depth areas across or inside the
/// safety contour, soundings no deeper than the safety contour, and S-102
/// shoals (<see cref="RouteFindingCodes.CrossesSafetyContour"/> and the like).
/// </summary>
public sealed class ShoalDetails : RouteFindingDetails
{
    /// <summary>The least depth in the span.</summary>
    public required Depth LeastDepth { get; init; }

    /// <summary>The setting it's shallower than.</summary>
    public required RouteFindingThreshold ThresholdKind { get; init; }

    /// <summary>The value of <see cref="ThresholdKind"/>.</summary>
    public required Depth Threshold { get; init; }

    /// <summary>How many soundings the finding stands for, or <c>null</c> when it isn't about soundings.</summary>
    public int? SoundingCount { get; init; }

    /// <summary>
    /// For soundings, the safety depth when the least depth is also shallower
    /// than it (the soundings the chart draws in bold), or <c>null</c>.
    /// </summary>
    public Depth? BelowSafetyDepth { get; init; }

    /// <summary>For an S-102 shoal, the uncertainty of the least depth, when the dataset gives one.</summary>
    public Depth? Uncertainty { get; init; }

    /// <summary>For an S-102 shoal, the dataset of the least depth.</summary>
    public string? DatasetId { get; init; }

    /// <summary>For an S-102 shoal, the grid cell of the least depth (<c>row,col</c>).</summary>
    public string? Cell { get; init; }
}

/// <summary>
/// A regulated area the route meets (<see cref="RouteFindingCategory.Area"/>):
/// what it is, what it restricts and when it's in force.
/// </summary>
public sealed class AreaDetails : RouteFindingDetails
{
    /// <summary>The product it comes from.</summary>
    public required RouteAreaProduct Product { get; init; }

    /// <summary>Its alert-catalogue kind (<see cref="RouteArea.AreaKind"/>), or <c>null</c>.</summary>
    public int? AreaKind { get; init; }

    /// <summary>What it is.</summary>
    public required RouteAreaKind Kind { get; init; }

    /// <summary>Its feature type, such as <c>"RestrictedArea"</c>.</summary>
    public required string FeatureType { get; init; }

    /// <summary>Its name, or <c>null</c>.</summary>
    public string? Name { get; init; }

    /// <summary>Its <c>restriction</c> codes.</summary>
    public IReadOnlyList<int> Restrictions { get; init; } = [];

    /// <summary>Its category codes (<see cref="RouteArea.Categories"/>).</summary>
    public IReadOnlyList<int> Categories { get; init; } = [];

    /// <summary>An S-122 area's IUCN category code, or <c>null</c>.</summary>
    public int? CategoryOfMarineProtectedArea { get; init; }

    /// <summary>When it's valid, or <c>null</c> for always.</summary>
    public RouteAreaDateRange? FixedDateRange { get; init; }

    /// <summary>When it's active each year; empty for all year.</summary>
    public IReadOnlyList<RouteAreaDateRange> PeriodicDateRanges { get; init; } = [];

    /// <summary>How it relates to the route (never <see cref="RouteAreaRelation.Off"/>).</summary>
    public RouteAreaRelation Relation { get; init; }

    /// <summary>Whether it's in force while the vessel is there.</summary>
    public bool InForce { get; init; }

    /// <summary>When the vessel reaches it: the ETA, or the fallback time when <see cref="AtEta"/> is false.</summary>
    public DateTimeOffset From { get; init; }

    /// <summary>When the vessel leaves it; equal to <see cref="From"/> when <see cref="AtEta"/> is false.</summary>
    public DateTimeOffset Until { get; init; }

    /// <summary>Whether <see cref="From"/> and <see cref="Until"/> are ETAs rather than the fallback time.</summary>
    public bool AtEta { get; init; }
}

/// <summary>An S-124 finding's warning, and how it stands on the route.</summary>
public sealed class WarningDetails : RouteFindingDetails
{
    /// <summary>The warning part's message.</summary>
    public required NavigationalWarningInfo Info { get; init; }

    /// <summary>How it relates to the route (never <see cref="RouteWarningRelation.Off"/>).</summary>
    public required RouteWarningRelation Relation { get; init; }

    /// <summary>Whether it applies while the vessel is there.</summary>
    public required RouteWarningStatus Status { get; init; }

    /// <summary>When the vessel reaches it (UTC): the ETA, or the fallback time when <see cref="AtEta"/> is false.</summary>
    public DateTimeOffset From { get; init; }

    /// <summary>When the vessel leaves it; equal to <see cref="From"/> for a crossing point or a near warning.</summary>
    public DateTimeOffset Until { get; init; }

    /// <summary>Whether <see cref="From"/> and <see cref="Until"/> are ETAs rather than the fallback time.</summary>
    public bool AtEta { get; init; }

    /// <summary>
    /// Whether the route goes through one of the warning's areas, rather
    /// than crossing its line: "Inside navigational warning" against
    /// "Crosses navigational warning".
    /// </summary>
    public bool InsideArea { get; init; }

    /// <summary>
    /// Why the warning is <see cref="RouteWarningStatus.Cancelled"/>: its
    /// cancellation date, the warning that cancels it, or the in-force
    /// bulletin that leaves it out (<see cref="NavigationalWarningSet.WithdrawnAt"/>).
    /// <c>null</c> for any other status.
    /// </summary>
    public NavigationalWarningState? Withdrawal { get; init; }

    /// <summary>
    /// The leg where the route leaves the warning, so a warning met on several
    /// legs reads "Crosses route on 2 legs"; <c>null</c> when it is the
    /// finding's <see cref="RouteFinding.LegIndex"/>.
    /// </summary>
    public int? EndLegIndex { get; init; }
}

/// <summary>
/// A finding of the clearance check: a stretch below your UKC setting
/// (<see cref="RouteFindingCodes.BelowSetting"/>), or a stretch of lower
/// confidence or missing data (<see cref="Stretch"/>).
/// </summary>
public sealed class ClearanceDetails : RouteFindingDetails
{
    /// <summary>The kind of stretch for a confidence or data finding; <c>null</c> for one below your UKC setting.</summary>
    public PassageStretchKind? Stretch { get; init; }

    /// <summary>The first leg of the stretch.</summary>
    public int FirstLegIndex { get; init; }

    /// <summary>The last leg of the stretch.</summary>
    public int LastLegIndex { get; init; }

    /// <summary>The stretch's length along the route.</summary>
    public Length Length { get; init; }

    /// <summary>When the vessel reaches the stretch (the departure without ETAs).</summary>
    public DateTimeOffset StartTime { get; init; }

    /// <summary>Below your UKC setting: the least clearance in the stretch, or <c>null</c>.</summary>
    public Depth? MinimumClearance { get; init; }

    /// <summary>Below your UKC setting: the required UKC where the least margin is.</summary>
    public Depth? RequiredUkc { get; init; }

    /// <summary>Below your UKC setting: the least clearance minus the required UKC (negative), or <c>null</c>.</summary>
    public Length? Margin { get; init; }

    /// <summary>Below your UKC setting: when the least margin is reached, or <c>null</c> without an ETA there.</summary>
    public DateTimeOffset? MinimumTime { get; init; }

    /// <summary>Below your UKC setting: minutes spent below it, or <c>null</c> when some of the stretch has no ETA.</summary>
    public double? MinutesBelow { get; init; }

    /// <summary>The state of the stretch: <see cref="UkcState.BelowSetting"/> for a finding below your UKC setting, otherwise <c>null</c>.</summary>
    public UkcState? State { get; init; }
}

/// <summary>
/// Areas of a kind the settings turn off (<see cref="RouteCheckSettings.AreaKindsOff"/>)
/// that the route meets (<see cref="RouteFindingCodes.AreaKindOff"/>, #1127):
/// their indication is off, so the mariner is told so.
/// </summary>
public sealed class AreaKindOffDetails : RouteFindingDetails
{
    /// <summary>The kind (<see cref="RouteAreaKinds"/>).</summary>
    public required int AreaKind { get; init; }

    /// <summary>How many areas of the kind the route meets.</summary>
    public required int Count { get; init; }
}
