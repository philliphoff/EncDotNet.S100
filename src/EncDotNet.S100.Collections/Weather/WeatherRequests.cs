using System.Globalization;
using System.Text;

namespace EncDotNet.S100.Collections.Weather;

/// <summary>
/// Builds the requests the weather services in the Library answer (#1075;
/// design note weather-sources.md §6.3): GRIB filter URLs, GRIB2 file and
/// <c>.idx</c> URLs, <c>api.weather.gov</c> alert queries and ERDDAP
/// <c>griddap</c> subsets. Pure functions, so every URL can be checked
/// without a network.
/// </summary>
public static class WeatherRequests
{
    /// <summary>The path of one forecast hour's file below the service's data root, from its <see cref="WeatherService.File"/> template.</summary>
    /// <param name="service">A GRIB2 service.</param>
    /// <param name="run">The run (UTC).</param>
    /// <param name="hour">The forecast hour.</param>
    public static string FilePath(WeatherService service, DateTimeOffset run, int hour)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (service.File is not { Length: > 0 } template)
            throw new InvalidOperationException($"The {service.Model} service names no file.");
        var utc = run.ToUniversalTime();
        return template
            .Replace("{date}", utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{cycle}", utc.ToString("HH", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{hour}", hour.ToString("000", CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>The URL of one forecast hour's whole GRIB2 file under the service's data root.</summary>
    public static Uri FileUri(WeatherService service, DateTimeOffset run, int hour)
    {
        ArgumentNullException.ThrowIfNull(service);
        var root = service.DataRoot ?? throw new InvalidOperationException($"The {service.Model} service has no data root.");
        return new Uri(Folder(root), string.Join('/', FilePath(service, run, hour).Split('/').Select(Uri.EscapeDataString)));
    }

    /// <summary>The URL of the <c>.idx</c> inventory beside <see cref="FileUri"/>.</summary>
    public static Uri IndexUri(WeatherService service, DateTimeOffset run, int hour) =>
        new(FileUri(service, run, hour).AbsoluteUri + ".idx");

    /// <summary>The <c>.idx</c> inventory URL beside a GRIB2 file URL.</summary>
    public static Uri IndexUriOf(Uri gribFile)
    {
        ArgumentNullException.ThrowIfNull(gribFile);
        return new Uri(gribFile.AbsoluteUri + ".idx");
    }

    /// <summary>
    /// A NOMADS GRIB filter request for one forecast hour (<c>filter_gfswave.pl?dir=…&amp;file=…</c>):
    /// <c>var_X=on</c> for each parameter, <c>lev_Y=on</c> for each of their
    /// levels (spaces as underscores, as the filter's form names them), and
    /// the area as a subregion. The filter returns the cross product of the
    /// variables and levels, so an extra field can come back; the GRIB2
    /// reader lists it.
    /// </summary>
    /// <remarks>
    /// Checked against the live filters (#1075): they take −180 to 180 or 0 to
    /// 360 longitudes alike, and an area across the antimeridian only as a
    /// west edge with an east edge past 180° (170° to 190°; 170° to −170°
    /// returns nothing, as does a box wider than 360°). The edges are
    /// inclusive except the east one on the GFS-Wave grid, whose longitudes
    /// sit a few millionths of a degree east of the grid (292.000007°), so
    /// <c>rightlon</c> is asked for half a cell further east, but not past
    /// 180° (the reader takes a column at 180.000007° as crossing it) or a
    /// turn past the west edge.
    /// </remarks>
    /// <param name="filterUri">The filter script.</param>
    /// <param name="service">The GRIB2 service.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="area">The area, or <see langword="null"/> for the whole grid.</param>
    /// <param name="run">The run (UTC).</param>
    /// <param name="hour">The forecast hour.</param>
    public static Uri GribFilterUri(
        Uri filterUri, WeatherService service, IReadOnlyList<WeatherServiceParameter> parameters, GeoBounds? area, DateTimeOffset run, int hour)
    {
        ArgumentNullException.ThrowIfNull(filterUri);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(parameters);
        var path = FilePath(service, run, hour);
        var slash = path.LastIndexOf('/');
        var dir = slash < 0 ? "/" : "/" + path[..slash];
        var file = slash < 0 ? path : path[(slash + 1)..];

        var query = new StringBuilder();
        query.Append("dir=").Append(Uri.EscapeDataString(dir));
        query.Append("&file=").Append(Uri.EscapeDataString(file));
        foreach (var variable in parameters.Select(p => p.Id).Distinct(StringComparer.Ordinal))
            query.Append("&var_").Append(Uri.EscapeDataString(variable)).Append("=on");
        foreach (var level in parameters.Select(p => p.Level).Distinct(StringComparer.Ordinal))
            query.Append("&lev_").Append(Uri.EscapeDataString(level.Replace(' ', '_'))).Append("=on");
        if (area is { } box)
        {
            var (south, west, north, east) = Rounded(box, service.GridDegrees);
            if (service.GridDegrees > 0)
                east = Math.Min(east + (service.GridDegrees / 2), east <= 180 ? 180 : west + 360);
            east = Math.Min(east, west + 360);
            query.Append("&subregion=");
            query.Append("&toplat=").Append(Number(north));
            query.Append("&leftlon=").Append(Number(west));
            query.Append("&rightlon=").Append(Number(east));
            query.Append("&bottomlat=").Append(Number(south));
        }

        return new UriBuilder(filterUri) { Query = query.ToString() }.Uri;
    }

    /// <summary>
    /// The area rounded out to whole grid cells (<paramref name="gridDegrees"/>,
    /// 0 for none), clamped to ±90° latitude. Longitudes are −180 to 180, and
    /// an area across the antimeridian keeps its west edge and gets an east
    /// edge past 180° (170° to 190°), as the GRIB filter takes it.
    /// </summary>
    public static (double South, double West, double North, double East) Rounded(GeoBounds area, double gridDegrees)
    {
        double Down(double v) => gridDegrees > 0 ? Math.Floor(v / gridDegrees) * gridDegrees : v;
        double Up(double v) => gridDegrees > 0 ? Math.Ceiling(v / gridDegrees) * gridDegrees : v;
        var east = area.CrossesAntimeridian ? area.East + 360 : area.East;
        return (Math.Max(-90, Down(area.South)), Down(area.West), Math.Min(90, Up(area.North)), Up(east));
    }

    /// <summary>
    /// The part of <paramref name="area"/> one grid download can hold: the
    /// area itself, or, for an area across the antimeridian, its wider side
    /// of 180° (the west side on a tie). The weather readers can't open a
    /// regional grid that crosses 180° (<c>WeatherGridLayout</c>), though the
    /// GRIB filter and ERDDAP would serve one.
    /// </summary>
    /// <param name="area">The chosen area.</param>
    /// <param name="clipped">True when the area crossed the antimeridian and was cut to one side.</param>
    public static GeoBounds OneSideOfAntimeridian(GeoBounds area, out bool clipped)
    {
        clipped = area.CrossesAntimeridian;
        if (!clipped)
            return area;
        return 180 - area.West >= area.East + 180
            ? area with { East = 180 }
            : area with { West = -180 };
    }

    /// <summary>
    /// The active alerts of <c>api.weather.gov</c> (<paramref name="alertsUri"/>,
    /// <c>…/alerts/active</c>) for marine zones: <c>?zone=</c> with up to
    /// <see cref="MaxZonesPerRequest"/> codes, else <c>?area=</c> with their
    /// marine areas (the first two letters of a zone code, <c>AN</c> for
    /// <c>ANZ535</c>), else, with no zones, every marine alert
    /// (<c>?region_type=marine</c>).
    /// </summary>
    public static Uri NwsAlertsUri(Uri alertsUri, IReadOnlyList<string> zones)
    {
        ArgumentNullException.ThrowIfNull(alertsUri);
        ArgumentNullException.ThrowIfNull(zones);
        var codes = zones.Where(z => z.Length >= 3).Select(z => z.ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var query = codes.Length == 0
            ? "region_type=marine"
            : codes.Length <= MaxZonesPerRequest
                ? "zone=" + string.Join(',', codes)
                : "area=" + string.Join(',', codes.Select(c => c[..2]).Distinct(StringComparer.Ordinal));
        return new UriBuilder(alertsUri) { Query = query }.Uri;
    }

    /// <summary>The most zone codes one alerts request names; more are asked for by marine area.</summary>
    public const int MaxZonesPerRequest = 50;

    /// <summary>The <c>info/&lt;id&gt;/index.json</c> metadata of an ERDDAP dataset.</summary>
    /// <param name="erddapUri">The ERDDAP root (<c>…/erddap/</c>).</param>
    /// <param name="datasetId">The dataset id.</param>
    public static Uri ErddapInfoUri(Uri erddapUri, string datasetId)
    {
        ArgumentNullException.ThrowIfNull(erddapUri);
        ArgumentException.ThrowIfNullOrEmpty(datasetId);
        return new Uri(Folder(erddapUri), "info/" + Uri.EscapeDataString(datasetId) + "/index.json");
    }

    /// <summary>
    /// A <c>griddap</c> subset of an ERDDAP dataset as NetCDF (<c>griddap/&lt;id&gt;.nc?…</c>):
    /// each variable over <paramref name="start"/> to <paramref name="end"/>,
    /// the area, and the first index of any other axis (a depth or height of one value).
    /// </summary>
    /// <param name="erddapUri">The ERDDAP root (<c>…/erddap/</c>).</param>
    /// <param name="dataset">The dataset.</param>
    /// <param name="axes">The dataset's axes, in order, as its metadata lists them.</param>
    /// <param name="area">The area, or <see langword="null"/> for all of it.</param>
    /// <param name="start">The first time (UTC).</param>
    /// <param name="end">The last time (UTC).</param>
    public static Uri ErddapGriddapUri(
        Uri erddapUri, WeatherServiceDataset dataset, IReadOnlyList<ErddapAxis> axes, GeoBounds? area, DateTimeOffset start, DateTimeOffset end)
    {
        ArgumentNullException.ThrowIfNull(erddapUri);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(axes);

        var constraint = new StringBuilder();
        foreach (var axis in axes)
        {
            constraint.Append('[');
            if (axis.IsTime)
            {
                constraint.Append('(').Append(Time(start)).Append("):1:(").Append(Time(end)).Append(')');
            }
            else if (axis.IsLatitude && area is { } lat)
            {
                var (south, north) = (lat.South, lat.North);
                if (axis.Descending)
                    (south, north) = (north, south);
                constraint.Append('(').Append(Number(south)).Append("):1:(").Append(Number(north)).Append(')');
            }
            else if (axis.IsLongitude && area is { } lon)
            {
                var (west, east) = (lon.West, lon.CrossesAntimeridian ? lon.East + 360 : lon.East);
                if (axis.Maximum > 180)
                {
                    // A 0 to 360 axis: an area west of Greenwich moves east by a turn.
                    if (west < 0 && east <= 0)
                        (west, east) = (west + 360, east + 360);
                    else if (west < 0)
                        west = 0; // Straddles 0°: keep the part east of it.
                }

                if (axis.Descending)
                    (west, east) = (east, west);
                constraint.Append('(').Append(Number(west)).Append("):1:(").Append(Number(east)).Append(')');
            }
            else if (axis.IsLatitude || axis.IsLongitude)
            {
                constraint.Append("0:1:last");
            }
            else
            {
                constraint.Append('0');
            }

            constraint.Append(']');
        }

        var query = string.Join(',', dataset.Variables.Select(v => Uri.EscapeDataString(v + constraint)));
        return new UriBuilder(new Uri(Folder(erddapUri), "griddap/" + Uri.EscapeDataString(dataset.Id) + ".nc")) { Query = query }.Uri;
    }

    /// <summary>
    /// The runs to try, newest first: the last <paramref name="cadenceHours"/>
    /// cycle at or before <paramref name="now"/>, then each earlier cycle, <paramref name="count"/> in all.
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> CandidateRuns(DateTimeOffset now, int cadenceHours, int count)
    {
        if (cadenceHours <= 0 || count <= 0)
            return [];
        var utc = now.ToUniversalTime();
        var latest = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour - (utc.Hour % cadenceHours), 0, 0, TimeSpan.Zero);
        return [.. Enumerable.Range(0, count).Select(i => latest.AddHours(-cadenceHours * i))];
    }

    /// <summary>A run as files and the Library name it: <c>2026-10-28 00Z</c>.</summary>
    public static string FormatRun(DateTimeOffset run) =>
        run.ToUniversalTime().ToString("yyyy-MM-dd HH'Z'", CultureInfo.InvariantCulture);

    private static Uri Folder(Uri uri) => uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset time) => time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>One axis of an ERDDAP <c>griddap</c> dataset, from its metadata.</summary>
/// <param name="Name">The axis name (<c>time</c>, <c>depth</c>, <c>latitude</c>, <c>longitude</c>).</param>
/// <param name="Descending">True when its values decrease (a negative <c>averageSpacing</c>).</param>
/// <param name="Minimum">The smallest value (<c>actual_range</c>), if known.</param>
/// <param name="Maximum">The largest value (<c>actual_range</c>), if known.</param>
public sealed record ErddapAxis(string Name, bool Descending = false, double? Minimum = null, double? Maximum = null)
{
    /// <summary>True for the time axis.</summary>
    public bool IsTime => Name.Equals("time", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for the latitude axis.</summary>
    public bool IsLatitude => Name.Equals("latitude", StringComparison.OrdinalIgnoreCase) || Name.Equals("lat", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for the longitude axis.</summary>
    public bool IsLongitude => Name.Equals("longitude", StringComparison.OrdinalIgnoreCase) || Name.Equals("lon", StringComparison.OrdinalIgnoreCase);
}
