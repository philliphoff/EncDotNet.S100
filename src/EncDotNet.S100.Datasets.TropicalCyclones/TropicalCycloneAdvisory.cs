using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.TropicalCyclones;

/// <summary>
/// One tropical cyclone advisory: the storm, when the advisory was issued,
/// its forecast (points, track, cone and wind radii) and the storm's track so
/// far. Read from NHC's GIS shapefiles by <see cref="NhcAdvisoryReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// Times are UTC <see cref="DateTime"/>s, except <see cref="IssueTime"/>,
/// which keeps the offset of the local time NHC wrote (EDT, HST and so on).
/// Hosts show times in the viewer's zone, not the advisory's.
/// </para>
/// <para>
/// A final advisory, issued when a storm dissipates or becomes
/// post-tropical, can come with no cone and a single forecast point:
/// <see cref="Cone"/> is then <see langword="null"/>.
/// </para>
/// <para>Not for navigation.</para>
/// </remarks>
public sealed record TropicalCycloneAdvisory
{
    /// <summary>The storm the advisory is for.</summary>
    public required TropicalCycloneStorm Storm { get; init; }

    /// <summary>
    /// The advisory number as NHC writes it, without leading zeros: <c>"20"</c>,
    /// or <c>"20A"</c> for an intermediate advisory.
    /// </summary>
    public required string Advisory { get; init; }

    /// <summary>When the advisory was issued, with the offset of NHC's local time.</summary>
    public required DateTimeOffset IssueTime { get; init; }

    /// <summary>The forecast period the advisory covers, in hours (120 for a five-day forecast).</summary>
    public int ForecastPeriodHours { get; init; }

    /// <summary>The forecast points, in tau order; the first is the storm at the advisory's synoptic time.</summary>
    public required IReadOnlyList<TropicalCycloneForecastPoint> Forecast { get; init; }

    /// <summary>
    /// The forecast track as NHC draws it, in WGS-84 degrees; the forecast
    /// points joined in order when the file has no track line.
    /// </summary>
    public required IReadOnlyList<GeoPosition> ForecastTrack { get; init; }

    /// <summary>The cone of uncertainty, or <see langword="null"/> when the advisory has none (a final advisory).</summary>
    public TropicalCycloneCone? Cone { get; init; }

    /// <summary>
    /// The storm's best track up to the advisory's synoptic time, oldest
    /// first; empty when the best track wasn't read. Points after that time,
    /// which a best track downloaded later holds, are left out.
    /// </summary>
    public IReadOnlyList<TropicalCycloneTrackPoint> PastTrack { get; init; } = [];

    /// <summary>The time of the first forecast point (tau 0), in UTC.</summary>
    public DateTime SynopticTime => Forecast.Count > 0 ? Forecast[0].ValidTime : IssueTime.UtcDateTime;

    /// <summary>The time of the last forecast point, in UTC.</summary>
    public DateTime ForecastEnd => Forecast.Count > 0 ? Forecast[^1].ValidTime : IssueTime.UtcDateTime;

    /// <summary>The time of the first past-track point, or <see cref="SynopticTime"/> when there's no past track, in UTC.</summary>
    public DateTime TrackStart => PastTrack.Count > 0 && PastTrack[0].Time < SynopticTime ? PastTrack[0].Time : SynopticTime;

    /// <summary>The current point: the forecast at tau 0, or <see langword="null"/> when there's no forecast.</summary>
    public TropicalCycloneForecastPoint? Current => Forecast.Count > 0 ? Forecast[0] : null;

    /// <summary>
    /// The storm at <paramref name="time"/> (V11): on a forecast point,
    /// estimated between two, or on the best track before the advisory; or
    /// <see langword="null"/> before the track and after the forecast. See
    /// <see cref="TropicalCycloneStormAt"/>.
    /// </summary>
    /// <param name="time">The time, in UTC.</param>
    public TropicalCycloneStormAt? StormAt(DateTime time) => TropicalCycloneStormAt.Of(this, time);

    /// <summary>The highest maximum wind of the forecast, in knots, or <see langword="null"/>.</summary>
    public int? PeakForecastWindKnots => Forecast.Select(p => p.MaxWindKnots).Max();

    /// <summary>
    /// The advisory's extent in WGS-84 degrees: the cone, the forecast and
    /// past tracks, and the forecast radii; <see langword="null"/> when it has
    /// no positions.
    /// </summary>
    public BoundingBox? Extent
    {
        get
        {
            var positions = ForecastTrack
                .Concat(Forecast.Select(p => p.Position))
                .Concat(PastTrack.Select(p => p.Position))
                .Concat((Cone?.Surfaces ?? []).SelectMany(s => s.ExteriorRing))
                .Concat(Forecast.SelectMany(p => p.Radii).SelectMany(r => r.Surfaces).SelectMany(s => s.ExteriorRing));
            double south = double.MaxValue, west = double.MaxValue, north = double.MinValue, east = double.MinValue;
            foreach (var p in positions)
            {
                south = Math.Min(south, p.Latitude);
                north = Math.Max(north, p.Latitude);
                west = Math.Min(west, p.Longitude);
                east = Math.Max(east, p.Longitude);
            }

            return south <= north ? new BoundingBox(south, west, north, east) : null;
        }
    }
}
