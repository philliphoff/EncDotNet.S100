using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation;

/// <summary>A water level to apply at a point.</summary>
/// <param name="HeightMeters">
/// Height of the water above the vertical datum in metres (positive up;
/// S-104 Ed 2.0.0 Table 5-1). The adjusted depth is the charted depth plus
/// this height, so a negative height makes the water shallower.
/// </param>
/// <param name="UncertaintyMeters">The uncertainty of the height (a ± value), or <c>null</c> when the S-104 dataset doesn't carry one.</param>
/// <param name="Source">The series the height was read from.</param>
/// <param name="StepTime">The time step nearest the time the height is for.</param>
public readonly record struct AppliedWaterLevel(
    double HeightMeters,
    double? UncertaintyMeters,
    WaterLevelSourceInfo Source,
    DateTime StepTime);

/// <summary>
/// What a <see cref="IWaterLevelField"/> found at a point: whether a water
/// level applies, and if not, why.
/// </summary>
/// <param name="Status">Whether the water level applies, and if not, why.</param>
/// <param name="Source">
/// The series that was chosen, also when its value was NoData or its datum
/// differs from the depth's; <c>null</c> when no series covers the point and time.
/// </param>
/// <param name="Sample">
/// The water level read, for <see cref="WaterLevelStatus.Applied"/> and
/// <see cref="WaterLevelStatus.DatumMismatch"/> (reported, not added); otherwise <c>null</c>.
/// </param>
/// <param name="Datum">
/// How the depth's datum compares with the chosen series' datum, when a value
/// was read; otherwise <c>null</c>.
/// </param>
public sealed record WaterLevelAtPoint(
    WaterLevelStatus Status,
    WaterLevelSourceInfo? Source = null,
    AppliedWaterLevel? Sample = null,
    DatumAgreement? Datum = null);

/// <summary>Which time a <see cref="IWaterLevelField"/> is for.</summary>
public enum WaterLevelBasisKind
{
    /// <summary>The water level at one instant (<see cref="WaterLevelSnapshot"/>).</summary>
    AtTime,

    /// <summary>
    /// The lowest water level over a period (<see cref="WaterLevelPeriodMinimum"/>;
    /// S-98 Appendix D-2.5).
    /// </summary>
    LowestOverPeriod,
}

/// <summary>What a <see cref="IWaterLevelField"/> applies: the time or period, and the datasets it reads.</summary>
/// <param name="Kind">An instant or a period.</param>
/// <param name="Start">The instant (UTC), or the start of the period.</param>
/// <param name="End">The instant again, or the end of the period (UTC).</param>
/// <param name="Sources">The S-104 datasets the field reads, one entry per dataset; empty when the source can't list them.</param>
public sealed record WaterLevelBasis(
    WaterLevelBasisKind Kind,
    DateTime Start,
    DateTime End,
    IReadOnlyList<WaterLevelSourceInfo> Sources);

/// <summary>
/// The water level to apply across an area, for one time or period: the model
/// the pick report, and later the chart, read, built on the same
/// <see cref="IWaterLevelSource"/> and series rule as the passage check so the
/// three give one answer for a point and time.
/// </summary>
/// <remarks>
/// <para>
/// At a point the most preferred series from <see cref="IWaterLevelSource.Locate"/>
/// whose time range covers the time (or the whole period) is used. When a
/// depth datum is given, a series whose datum is known to differ is skipped in
/// favour of a later one that covers too, as the passage check does. Times
/// outside a series' range are never clamped to its first or last step.
/// </para>
/// <para>
/// Implementations must be safe to call from several threads at once.
/// </para>
/// </remarks>
public interface IWaterLevelField
{
    /// <summary>
    /// The water level to apply at a point, or <c>null</c> where none
    /// applies. No depth datum is compared; use <see cref="Resolve"/> to
    /// compare one.
    /// </summary>
    /// <param name="position">The point, in WGS-84.</param>
    /// <returns>The water level, or <c>null</c>.</returns>
    AppliedWaterLevel? Sample(GeoPosition position);

    /// <summary>
    /// The water level at a point for a depth on <paramref name="depthDatum"/>,
    /// with the reason when none applies.
    /// </summary>
    /// <param name="position">The point, in WGS-84.</param>
    /// <param name="depthDatum">The charted depth's vertical datum as an S-100 code, or <c>null</c> when it isn't declared.</param>
    /// <returns>The outcome.</returns>
    WaterLevelAtPoint Resolve(GeoPosition position, int? depthDatum);

    /// <summary>
    /// The areas where the field can apply, in WGS-84, for the adjusted-area
    /// boundary. Empty when the source can't describe them.
    /// </summary>
    IReadOnlyList<CoverageArea> Footprint { get; }

    /// <summary>What the field applies: the time or period, and the datasets.</summary>
    WaterLevelBasis Basis { get; }
}

/// <summary>The water level at one instant, from an <see cref="IWaterLevelSource"/>.</summary>
/// <remarks>
/// The value is interpolated linearly between the bracketing steps
/// (<see cref="IWaterLevelSeries.ValueAt"/>). A point whose series don't
/// cover the time isn't adjusted.
/// </remarks>
public sealed class WaterLevelSnapshot : IWaterLevelField
{
    private readonly IWaterLevelSource _source;

    /// <summary>Creates the field.</summary>
    /// <param name="source">The water-level source.</param>
    /// <param name="utc">The time; a local time is converted, an unspecified one is taken as UTC.</param>
    public WaterLevelSnapshot(IWaterLevelSource source, DateTime utc)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        Time = WaterLevelSelection.ToUtc(utc);
        Basis = new WaterLevelBasis(WaterLevelBasisKind.AtTime, Time, Time, source.Datasets);
    }

    /// <summary>The time the field is for (UTC).</summary>
    public DateTime Time { get; }

    /// <inheritdoc />
    public IReadOnlyList<CoverageArea> Footprint => _source.Footprint;

    /// <inheritdoc />
    public WaterLevelBasis Basis { get; }

    /// <inheritdoc />
    public AppliedWaterLevel? Sample(GeoPosition position) =>
        Resolve(position, depthDatum: null) is { Status: WaterLevelStatus.Applied, Sample: { } sample } ? sample : null;

    /// <inheritdoc />
    public WaterLevelAtPoint Resolve(GeoPosition position, int? depthDatum) =>
        Resolve(_source.Locate(position), Time, depthDatum);

    /// <summary>
    /// The water level at <paramref name="utc"/> from series already located
    /// at a point, most preferred first, by the rule every
    /// <see cref="IWaterLevelField"/> and the passage check share.
    /// </summary>
    /// <param name="series">The series at the point, most preferred first.</param>
    /// <param name="utc">The time (UTC).</param>
    /// <param name="depthDatum">The charted depth's datum, or <c>null</c>.</param>
    /// <returns>The outcome.</returns>
    public static WaterLevelAtPoint Resolve(IReadOnlyList<IWaterLevelSeries> series, DateTime utc, int? depthDatum)
    {
        ArgumentNullException.ThrowIfNull(series);
        utc = WaterLevelSelection.ToUtc(utc);
        var choice = WaterLevelSelection.Choose(series, utc, utc, depthDatum);
        if (choice.Outcome is { } missing)
        {
            return new WaterLevelAtPoint(missing);
        }

        var used = choice.Used!;
        if (used.ValueAt(utc) is not { } reading)
        {
            return new WaterLevelAtPoint(WaterLevelStatus.NoData, used.Source);
        }

        return new WaterLevelAtPoint(
            choice.Chosen is null ? WaterLevelStatus.DatumMismatch : WaterLevelStatus.Applied,
            used.Source,
            new AppliedWaterLevel(reading.HeightMeters, reading.UncertaintyMeters, used.Source, reading.StepTime),
            VerticalDatumCheck.Compare(depthDatum, used.Source.VerticalDatum));
    }
}

/// <summary>
/// The lowest water level at each point over a period (S-98 Appendix D-2.5,
/// S-164 WLATimePeriod): the conservative choice for a passage window.
/// </summary>
/// <remarks>
/// At a point the series must cover the whole period. The minimum is taken
/// over the values at every step inside the period and at the two
/// interpolated ends. When any of those values is NoData the point isn't
/// adjusted (<see cref="WaterLevelStatus.NoData"/>), since the lowest water
/// can't be known. The uncertainty is the one at the lowest value.
/// </remarks>
public sealed class WaterLevelPeriodMinimum : IWaterLevelField
{
    private readonly IWaterLevelSource _source;

    /// <summary>Creates the field.</summary>
    /// <param name="source">The water-level source.</param>
    /// <param name="start">The start of the period; a local time is converted, an unspecified one is taken as UTC.</param>
    /// <param name="end">The end of the period, at or after <paramref name="start"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="end"/> is before <paramref name="start"/>.</exception>
    public WaterLevelPeriodMinimum(IWaterLevelSource source, DateTime start, DateTime end)
    {
        ArgumentNullException.ThrowIfNull(source);
        start = WaterLevelSelection.ToUtc(start);
        end = WaterLevelSelection.ToUtc(end);
        if (end < start)
        {
            throw new ArgumentException("The period ends before it starts.", nameof(end));
        }

        _source = source;
        Start = start;
        End = end;
        Basis = new WaterLevelBasis(WaterLevelBasisKind.LowestOverPeriod, start, end, source.Datasets);
    }

    /// <summary>The start of the period (UTC).</summary>
    public DateTime Start { get; }

    /// <summary>The end of the period (UTC).</summary>
    public DateTime End { get; }

    /// <inheritdoc />
    public IReadOnlyList<CoverageArea> Footprint => _source.Footprint;

    /// <inheritdoc />
    public WaterLevelBasis Basis { get; }

    /// <inheritdoc />
    public AppliedWaterLevel? Sample(GeoPosition position) =>
        Resolve(position, depthDatum: null) is { Status: WaterLevelStatus.Applied, Sample: { } sample } ? sample : null;

    /// <inheritdoc />
    public WaterLevelAtPoint Resolve(GeoPosition position, int? depthDatum) =>
        Resolve(_source.Locate(position), Start, End, depthDatum);

    /// <summary>The lowest water level over a period from series already located at a point.</summary>
    /// <param name="series">The series at the point, most preferred first.</param>
    /// <param name="start">The start of the period (UTC).</param>
    /// <param name="end">The end of the period (UTC).</param>
    /// <param name="depthDatum">The charted depth's datum, or <c>null</c>.</param>
    /// <returns>The outcome.</returns>
    public static WaterLevelAtPoint Resolve(IReadOnlyList<IWaterLevelSeries> series, DateTime start, DateTime end, int? depthDatum)
    {
        ArgumentNullException.ThrowIfNull(series);
        start = WaterLevelSelection.ToUtc(start);
        end = WaterLevelSelection.ToUtc(end);
        var choice = WaterLevelSelection.Choose(series, start, end, depthDatum);
        if (choice.Outcome is { } missing)
        {
            return new WaterLevelAtPoint(missing);
        }

        var used = choice.Used!;
        WaterLevelReading? lowest = null;
        foreach (var time in Times(used, start, end))
        {
            if (used.ValueAt(time) is not { } reading)
            {
                return new WaterLevelAtPoint(WaterLevelStatus.NoData, used.Source);
            }

            if (lowest is not { } low || reading.HeightMeters < low.HeightMeters)
            {
                lowest = reading;
            }
        }

        var min = lowest!.Value;
        return new WaterLevelAtPoint(
            choice.Chosen is null ? WaterLevelStatus.DatumMismatch : WaterLevelStatus.Applied,
            used.Source,
            new AppliedWaterLevel(min.HeightMeters, min.UncertaintyMeters, used.Source, min.StepTime),
            VerticalDatumCheck.Compare(depthDatum, used.Source.VerticalDatum));
    }

    private static IEnumerable<DateTime> Times(IWaterLevelSeries series, DateTime start, DateTime end)
    {
        yield return start;
        foreach (var step in series.StepsWithin(start, end))
        {
            yield return step;
        }

        yield return end;
    }
}

/// <summary>
/// The series rule shared by the <see cref="IWaterLevelField"/>s and the
/// passage check, so they can't disagree.
/// </summary>
internal static class WaterLevelSelection
{
    internal readonly record struct Choice(IWaterLevelSeries? Chosen, IWaterLevelSeries? Used, WaterLevelStatus? Outcome);

    /// <summary>
    /// The most preferred series covering [<paramref name="start"/>, <paramref name="end"/>];
    /// among those, one whose datum is known to differ from
    /// <paramref name="depthDatum"/> is skipped when another covers too. When
    /// no series covers, <see cref="Choice.Outcome"/> says why.
    /// </summary>
    public static Choice Choose(IReadOnlyList<IWaterLevelSeries> series, DateTime start, DateTime end, int? depthDatum)
    {
        IWaterLevelSeries? chosen = null;
        IWaterLevelSeries? mismatched = null;
        DateTime? latest = null;
        foreach (var candidate in series)
        {
            latest = latest is { } l && l >= candidate.End ? l : candidate.End;
            if (start < candidate.Start || end > candidate.End)
            {
                continue;
            }

            if (VerticalDatumCheck.Compare(depthDatum, candidate.Source.VerticalDatum) == DatumAgreement.Different)
            {
                mismatched ??= candidate;
                continue;
            }

            chosen = candidate;
            break;
        }

        var used = chosen ?? mismatched;
        if (series.Count == 0)
        {
            return new Choice(null, null, WaterLevelStatus.NoCoverage);
        }

        if (used is null)
        {
            return new Choice(null, null, latest is { } last && end > last ? WaterLevelStatus.AfterEnd : WaterLevelStatus.BeforeStart);
        }

        return new Choice(chosen, used, null);
    }

    public static DateTime ToUtc(DateTime time) => time.Kind switch
    {
        DateTimeKind.Utc => time,
        DateTimeKind.Local => time.ToUniversalTime(),
        _ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
    };
}
