using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// The state of a sample, a leg or a stretch, in the handoff's words.
/// </summary>
/// <remarks>
/// <para>
/// The order runs from best to worst for display: a leg's worst state is the
/// highest value among its samples, so <see cref="BelowSetting"/> always wins
/// (see <see cref="UkcStates.Worst"/>).
/// </para>
/// <para>
/// Confidence is a separate dimension (<see cref="ConfidenceReasons"/>): a
/// sample that meets your settings on S-101 depth, or where the datums can't
/// be confirmed, is still <see cref="MeetsSettings"/> or <see cref="Tight"/>,
/// with its low-confidence reasons alongside.
/// </para>
/// </remarks>
public enum UkcState
{
    /// <summary>Meets your settings: clearance is at least 0,5 m above the required UKC.</summary>
    MeetsSettings,

    /// <summary>Tight: clearance meets the required UKC with less than 0,5 m to spare.</summary>
    Tight,

    /// <summary>No data: there is no charted depth, or the water level can't be applied (outside the S-104 extent or time range, masked, or on a different datum).</summary>
    NoData,

    /// <summary>Below your UKC setting: clearance is less than the required UKC.</summary>
    BelowSetting,
}

/// <summary>Helpers for <see cref="UkcState"/>.</summary>
public static class UkcStates
{
    /// <summary>
    /// The worse of two states. <see cref="UkcState.BelowSetting"/> beats
    /// everything, then <see cref="UkcState.NoData"/>,
    /// <see cref="UkcState.Tight"/> and <see cref="UkcState.MeetsSettings"/>.
    /// </summary>
    /// <param name="a">A state.</param>
    /// <param name="b">Another state.</param>
    /// <returns>The worse state.</returns>
    public static UkcState Worst(UkcState a, UkcState b) => (UkcState)Math.Max((int)a, (int)b);
}

/// <summary>
/// Why a sample, a leg or a departure is lower confidence. Confidence is shown
/// alongside the clearance band, never in place of it.
/// </summary>
[Flags]
public enum ConfidenceReasons
{
    /// <summary>Full confidence: S-102 depth on a confirmed datum.</summary>
    None = 0,

    /// <summary>The charted depth comes from S-101 rather than S-102.</summary>
    ChartedDepthFromS101 = 1,

    /// <summary>
    /// The charted-depth or water-level datum isn't declared, so the depth
    /// and water level can't be confirmed to share a reference.
    /// </summary>
    DatumUnconfirmed = 2,
}

/// <summary>How the clearance compares with the required UKC, ignoring confidence.</summary>
public enum ClearanceBand
{
    /// <summary>At least the tight margin above the required UKC.</summary>
    Meets,

    /// <summary>At or above the required UKC, but within the tight margin.</summary>
    Tight,

    /// <summary>Below the required UKC.</summary>
    Below,
}

/// <summary>Whether the water level was applied at a sample, and if not, why.</summary>
public enum WaterLevelStatus
{
    /// <summary>A water level was found and added to the charted depth.</summary>
    Applied,

    /// <summary>The sample is outside every S-104 dataset's extent ("tide not applied").</summary>
    NoCoverage,

    /// <summary>The sample's time is before the first S-104 time step.</summary>
    BeforeStart,

    /// <summary>The sample's time is after the last S-104 time step.</summary>
    AfterEnd,

    /// <summary>The covering S-104 value is NoData (masked) at that time.</summary>
    NoData,

    /// <summary>
    /// The S-104 datum differs from the charted depth's datum, so the water
    /// level is reported but not added (S-104 Ed 2.0.0 §5.2).
    /// </summary>
    DatumMismatch,
}

/// <summary>The headline answer for a passage, in the handoff's C3 title priority.</summary>
public enum PassageVerdict
{
    /// <summary>Meets your settings everywhere.</summary>
    MeetsSettings,

    /// <summary>Meets your settings, but tight somewhere.</summary>
    MeetsSettingsTight,

    /// <summary>
    /// Parts of the passage have no data (no charted depth, or no water level
    /// that can be applied) and nothing is below the setting; neutral.
    /// </summary>
    IncompleteData,

    /// <summary>Below your UKC setting in one or more places.</summary>
    BelowSetting,

    /// <summary>
    /// The passage runs past the end of the water-level data; neutral, and
    /// takes priority over every other verdict.
    /// </summary>
    WaterLevelEndsBeforeArrival,
}

/// <summary>Kinds of <see cref="PassageStretch"/>.</summary>
public enum PassageStretchKind
{
    /// <summary>No charted depth from S-102 or S-101.</summary>
    NoChartedDepth,

    /// <summary>Outside every S-104 dataset's extent.</summary>
    NoWaterLevelCoverage,

    /// <summary>Before the first S-104 time step.</summary>
    WaterLevelBeforeStart,

    /// <summary>After the last S-104 time step.</summary>
    WaterLevelAfterEnd,

    /// <summary>The covering S-104 value is NoData.</summary>
    WaterLevelNoData,

    /// <summary>The S-104 and charted-depth datums differ, so the water level is not applied.</summary>
    DatumMismatch,

    /// <summary>The charted depth comes from S-101 (low confidence).</summary>
    ChartedDepthFromS101,

    /// <summary>The charted-depth or water-level datum isn't declared, so agreement can't be confirmed (low confidence).</summary>
    DatumUnconfirmed,
}

/// <summary>A run of consecutive samples that share a gap or a low-confidence reason.</summary>
/// <param name="Kind">What the stretch is.</param>
/// <param name="FirstSample">Index of the first sample in the stretch.</param>
/// <param name="LastSample">Index of the last sample in the stretch.</param>
/// <param name="FirstLeg">Index of the leg the stretch starts on.</param>
/// <param name="LastLeg">Index of the leg the stretch ends on.</param>
/// <param name="StartDistanceMeters">Distance from the start of the passage to the start of the stretch.</param>
/// <param name="EndDistanceMeters">Distance from the start of the passage to the end of the stretch.</param>
/// <param name="StartTime">The time at the first sample.</param>
/// <param name="EndTime">The time at the last sample.</param>
public sealed record PassageStretch(
    PassageStretchKind Kind,
    int FirstSample,
    int LastSample,
    int FirstLeg,
    int LastLeg,
    double StartDistanceMeters,
    double EndDistanceMeters,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime)
{
    /// <summary>Length of the stretch in metres.</summary>
    public double LengthMeters => EndDistanceMeters - StartDistanceMeters;
}

/// <summary>The water level at a sample.</summary>
/// <param name="Status">Whether it was applied.</param>
/// <param name="HeightMeters">The height above the S-104 datum, when one was read (also for <see cref="WaterLevelStatus.DatumMismatch"/>).</param>
/// <param name="Source">The series it came from, when one was found.</param>
/// <param name="StepTime">The S-104 time step nearest the sample's time, when one was read.</param>
/// <param name="Datum">How the S-104 datum compares with the charted depth's, when both were found.</param>
public sealed record UkcWaterLevel(
    WaterLevelStatus Status,
    double? HeightMeters = null,
    WaterLevelSourceInfo? Source = null,
    DateTime? StepTime = null,
    DatumAgreement? Datum = null);

/// <summary>One sample along the passage.</summary>
/// <param name="Index">Index of the sample in <see cref="PassageCheckResult.Samples"/>.</param>
/// <param name="LegIndex">Index of the leg the sample is on.</param>
/// <param name="Position">Where the sample's charted depth was read: the shoalest point in its stretch.</param>
/// <param name="DistanceMeters">Distance from the start of the passage to <paramref name="Position"/>.</param>
/// <param name="StartDistanceMeters">Distance from the start of the passage to the start of the stretch this sample stands for.</param>
/// <param name="EndDistanceMeters">Distance from the start of the passage to the end of the stretch this sample stands for.</param>
/// <param name="Time">The ETA at the sample, or the departure time when ETAs are unknown.</param>
/// <param name="HasEta"><c>true</c> when <paramref name="Time"/> is a real ETA; <c>false</c> when the leg has no speed.</param>
/// <param name="Depth">The charted depth, or <c>null</c> when no source covers the sample.</param>
/// <param name="WaterLevel">The water level and whether it was applied.</param>
/// <param name="DraftMeters">The draft on the sample's leg.</param>
/// <param name="SquatMeters">The squat allowance on the sample's leg.</param>
/// <param name="RequiredUkcMeters">The required UKC on the sample's leg (0 when unset).</param>
/// <param name="ClearanceMeters">
/// UKC = charted depth + water level − draft − squat, or <c>null</c> when
/// either the depth or an applicable water level is missing.
/// </param>
/// <param name="Band">How the clearance compares with the required UKC, or <c>null</c> without a clearance.</param>
/// <param name="State">The sample's state.</param>
/// <param name="Confidence">
/// Why the sample is lower confidence, or <see cref="ConfidenceReasons.None"/>.
/// Independent of <paramref name="State"/>: a no-data sample can carry
/// <see cref="ConfidenceReasons.ChartedDepthFromS101"/> too.
/// </param>
public sealed record UkcSample(
    int Index,
    int LegIndex,
    GeoPosition Position,
    double DistanceMeters,
    double StartDistanceMeters,
    double EndDistanceMeters,
    DateTimeOffset Time,
    bool HasEta,
    ChartedDepth? Depth,
    UkcWaterLevel WaterLevel,
    double DraftMeters,
    double SquatMeters,
    double RequiredUkcMeters,
    double? ClearanceMeters,
    ClearanceBand? Band,
    UkcState State,
    ConfidenceReasons Confidence = ConfidenceReasons.None)
{
    /// <summary><c>true</c> when <see cref="Confidence"/> has any reason.</summary>
    public bool IsLowConfidence => Confidence != ConfidenceReasons.None;
}

/// <summary>The smallest margin over the required UKC, and where and when it occurs.</summary>
/// <param name="SampleIndex">Index of the sample.</param>
/// <param name="LegIndex">Index of the leg.</param>
/// <param name="Position">Where it occurs.</param>
/// <param name="DistanceMeters">Distance from the start of the passage.</param>
/// <param name="Time">When it occurs (the ETA, or the departure time without ETAs).</param>
/// <param name="HasEta"><c>true</c> when <paramref name="Time"/> is a real ETA.</param>
/// <param name="ClearanceMeters">The UKC there.</param>
/// <param name="RequiredUkcMeters">The required UKC there.</param>
/// <param name="State">The sample's state.</param>
/// <param name="Confidence">Why the sample is lower confidence, or <see cref="ConfidenceReasons.None"/>.</param>
public sealed record UkcMinimum(
    int SampleIndex,
    int LegIndex,
    GeoPosition Position,
    double DistanceMeters,
    DateTimeOffset Time,
    bool HasEta,
    double ClearanceMeters,
    double RequiredUkcMeters,
    UkcState State,
    ConfidenceReasons Confidence = ConfidenceReasons.None);

/// <summary>The check result for one leg.</summary>
/// <param name="LegIndex">Index of the leg.</param>
/// <param name="Name">The leg's display name, when the caller gave one.</param>
/// <param name="StartDistanceMeters">Distance from the start of the passage to the start of the leg.</param>
/// <param name="DistanceMeters">Length of the leg.</param>
/// <param name="Start">ETA at the start of the leg, or <c>null</c> when unknown.</param>
/// <param name="End">ETA at the end of the leg, or <c>null</c> when unknown.</param>
/// <param name="FirstSample">Index of the leg's first sample.</param>
/// <param name="SampleCount">Number of samples on the leg.</param>
/// <param name="Minimum">The leg's smallest margin over the required UKC, or <c>null</c> when no sample has a clearance.</param>
/// <param name="WorstState">The worst state among the leg's samples.</param>
/// <param name="MinutesBelow">Time spent below the setting on this leg, in minutes (0 without ETAs).</param>
/// <param name="PlacesBelow">Number of separate stretches below the setting on this leg.</param>
/// <param name="Confidence">The low-confidence reasons of every sample on the leg, combined.</param>
/// <param name="HasNoWaterLevel"><c>true</c> when any sample on the leg has a charted depth but no applied water level.</param>
public sealed record LegUkcResult(
    int LegIndex,
    string? Name,
    double StartDistanceMeters,
    double DistanceMeters,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    int FirstSample,
    int SampleCount,
    UkcMinimum? Minimum,
    UkcState WorstState,
    double MinutesBelow,
    int PlacesBelow,
    ConfidenceReasons Confidence,
    bool HasNoWaterLevel)
{
    /// <summary><c>true</c> when any sample on the leg is lower confidence.</summary>
    public bool HasLowConfidence => Confidence != ConfidenceReasons.None;
}

/// <summary>The datasets and datums behind a result, for the Sources disclosure.</summary>
/// <param name="BathymetryDatasets">The S-102 datasets that supplied charted depth.</param>
/// <param name="FallbackDatasets">The S-101 cells that supplied charted depth where S-102 didn't.</param>
/// <param name="WaterLevelSources">The distinct water-level series that were applied or read.</param>
/// <param name="DepthDatums">The distinct charted-depth datum codes.</param>
/// <param name="WaterLevelDatums">The distinct water-level datum codes.</param>
/// <param name="MaxUncertaintyMeters">The largest S-102 uncertainty among the samples, or <c>null</c>.</param>
public sealed record PassageSources(
    IReadOnlyList<string> BathymetryDatasets,
    IReadOnlyList<string> FallbackDatasets,
    IReadOnlyList<WaterLevelSourceInfo> WaterLevelSources,
    IReadOnlyList<int> DepthDatums,
    IReadOnlyList<int> WaterLevelDatums,
    double? MaxUncertaintyMeters);

/// <summary>The result of a passage check.</summary>
/// <param name="Departure">The departure time.</param>
/// <param name="Arrival">The arrival time, or <c>null</c> when any leg has no speed.</param>
/// <param name="Verdict">The headline answer.</param>
/// <param name="NoMarginSet"><c>true</c> when any leg has no required UKC, so it was checked against 0 m.</param>
/// <param name="Minimum">The smallest margin over the required UKC, or <c>null</c> when no sample has a clearance.</param>
/// <param name="MinutesBelow">Time spent below the setting, in minutes (0 without ETAs).</param>
/// <param name="PlacesBelow">Number of separate stretches below the setting.</param>
/// <param name="Samples">Every sample in passage order.</param>
/// <param name="Legs">Per-leg results in route order.</param>
/// <param name="Gaps">Stretches with no data, or where the datums don't match.</param>
/// <param name="LowConfidence">Stretches charted from S-101, or where the datums can't be confirmed.</param>
/// <param name="Sources">The datasets and datums behind the result.</param>
/// <param name="Confidence">The low-confidence reasons of every sample, combined.</param>
public sealed record PassageCheckResult(
    DateTimeOffset Departure,
    DateTimeOffset? Arrival,
    PassageVerdict Verdict,
    bool NoMarginSet,
    UkcMinimum? Minimum,
    double MinutesBelow,
    int PlacesBelow,
    IReadOnlyList<UkcSample> Samples,
    IReadOnlyList<LegUkcResult> Legs,
    IReadOnlyList<PassageStretch> Gaps,
    IReadOnlyList<PassageStretch> LowConfidence,
    PassageSources Sources,
    ConfidenceReasons Confidence = ConfidenceReasons.None);

/// <summary>The outcome of one departure in a <see cref="DepartureSweep"/>.</summary>
/// <param name="Departure">The departure time.</param>
/// <param name="Arrival">The arrival time, or <c>null</c> without ETAs.</param>
/// <param name="Verdict">The verdict for this departure.</param>
/// <param name="MinimumClearanceMeters">The UKC at the smallest margin, or <c>null</c> when no sample has a clearance.</param>
/// <param name="MinimumMarginMeters">The smallest clearance minus required UKC, or <c>null</c>.</param>
/// <param name="MinutesBelow">Time below the setting, in minutes.</param>
/// <param name="Confidence">
/// The low-confidence reasons along the passage at this departure, combined.
/// A departure that meets your settings with lower confidence still meets them.
/// </param>
public sealed record DepartureOption(
    DateTimeOffset Departure,
    DateTimeOffset? Arrival,
    PassageVerdict Verdict,
    double? MinimumClearanceMeters,
    double? MinimumMarginMeters,
    double MinutesBelow,
    ConfidenceReasons Confidence = ConfidenceReasons.None)
{
    /// <summary><c>true</c> when the verdict is <see cref="PassageVerdict.MeetsSettings"/> or <see cref="PassageVerdict.MeetsSettingsTight"/>.</summary>
    public bool MeetsSettings => Verdict is PassageVerdict.MeetsSettings or PassageVerdict.MeetsSettingsTight;
}

/// <summary>Verdicts for a range of departures over the same route and depths.</summary>
/// <param name="Options">One entry per departure, in time order.</param>
public sealed record DepartureSweep(IReadOnlyList<DepartureOption> Options)
{
    /// <summary>The time between departures; <see cref="TimeSpan.Zero"/> when unknown.</summary>
    public TimeSpan Step { get; init; }

    /// <summary>
    /// The departures whose arrival is after the water-level data ends: the
    /// trailing run of <see cref="PassageVerdict.WaterLevelEndsBeforeArrival"/>
    /// options, as a (first, last) pair, or <c>null</c> when the last
    /// departure arrives within the data. These never meet your settings.
    /// </summary>
    /// <returns>The span, or <c>null</c>.</returns>
    public (DateTimeOffset First, DateTimeOffset Last)? ArrivalAfterDataEnds()
    {
        var first = Options.Count;
        while (first > 0 && Options[first - 1].Verdict == PassageVerdict.WaterLevelEndsBeforeArrival)
        {
            first--;
        }

        return first < Options.Count ? (Options[first].Departure, Options[^1].Departure) : null;
    }

    /// <summary>
    /// The contiguous runs of departures that meet your settings, as
    /// (first, last) departure pairs.
    /// </summary>
    public IReadOnlyList<(DateTimeOffset First, DateTimeOffset Last)> Windows()
    {
        var windows = new List<(DateTimeOffset, DateTimeOffset)>();
        DateTimeOffset? first = null;
        DateTimeOffset last = default;
        foreach (var option in Options)
        {
            if (option.MeetsSettings)
            {
                first ??= option.Departure;
                last = option.Departure;
            }
            else if (first is { } f)
            {
                windows.Add((f, last));
                first = null;
            }
        }

        if (first is { } open)
        {
            windows.Add((open, last));
        }

        return windows;
    }

    /// <summary>
    /// The low-confidence reasons of the departures that meet your settings,
    /// combined: the caveat on the departure window.
    /// </summary>
    /// <returns>The reasons, or <see cref="ConfidenceReasons.None"/>.</returns>
    public ConfidenceReasons MeetingConfidence()
    {
        var reasons = ConfidenceReasons.None;
        foreach (var option in Options)
        {
            if (option.MeetsSettings)
            {
                reasons |= option.Confidence;
            }
        }

        return reasons;
    }

    /// <summary>The meeting departure nearest <paramref name="departure"/>, or <c>null</c> when none meets.</summary>
    /// <param name="departure">The reference departure.</param>
    /// <returns>The nearest meeting option, or <c>null</c>.</returns>
    public DepartureOption? NearestMeeting(DateTimeOffset departure)
    {
        DepartureOption? best = null;
        foreach (var option in Options)
        {
            if (option.MeetsSettings
                && (best is null || (option.Departure - departure).Duration() < (best.Departure - departure).Duration()))
            {
                best = option;
            }
        }

        return best;
    }
}
