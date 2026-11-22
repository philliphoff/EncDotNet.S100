using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.TestSupport;

/// <summary>
/// A fake of the weather services the Library reads (#1075), with no
/// network: NOMADS (the GRIB filter, answering nothing for a subregion the
/// live filter can't cut, and the <c>pub/</c> tree whose
/// <c>.idx</c> files say a run is published), NOAA's GFS bucket on AWS (whole
/// GRIB2 files with generated <c>.idx</c> inventories, honouring
/// <c>Range</c>), <c>api.weather.gov</c> alerts (refusing requests without a
/// <c>User-Agent</c>, as the NWS API does) and an ERDDAP server.
/// </summary>
internal sealed class WeatherFakeServer : HttpMessageHandler
{
    /// <summary>The runs that are published (both NOMADS and AWS), with every forecast hour.</summary>
    public HashSet<DateTimeOffset> Runs { get; } = [];

    /// <summary>
    /// The GRIB2 messages of each forecast hour, by hour: what the filter
    /// returns for that hour, and what an AWS file holds after a filler
    /// message (<c>WIND:surface</c>, never asked for).
    /// </summary>
    public Dictionary<int, IReadOnlyList<(string Variable, string Level, byte[] Bytes)>> Messages { get; } = [];

    /// <summary>The alerts JSON <c>api.weather.gov</c> answers.</summary>
    public string AlertsJson { get; set; } = """{"type":"FeatureCollection","features":[],"updated":"2026-10-25T05:00:00+00:00"}""";

    /// <summary>The <c>info/&lt;id&gt;/index.json</c> ERDDAP answers.</summary>
    public string? ErddapInfoJson { get; set; }

    /// <summary>The NetCDF bytes ERDDAP's <c>griddap/&lt;id&gt;.nc</c> answers.</summary>
    public byte[]? ErddapNetCdf { get; set; }

    /// <summary>
    /// A synthetic NCSS <c>dataset.xml</c> shaped like HYCOM ESPC-D-V02's
    /// (#1270): listed <c>lat</c> (0.04°) and <c>lon</c> (0.08°, 0 to 360)
    /// values, a <c>depth</c> axis from 0 m, a 3-hourly <c>time</c> axis
    /// given as start and increment, and daily runs on <c>time_run</c>, the
    /// newest 2026-10-24 12Z. Its time span is 2026-10-15 12Z to 2026-10-31 12Z.
    /// </summary>
    public const string HycomDatasetXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gridDataset location="/thredds/ncss/FMRC_ESPC-D-V02_uv3z/FMRC_ESPC-D-V02_uv3z_best.ncd" path="path">
          <axis name="depth" shape="4" type="double" axisType="Height">
            <attribute name="units" value="m" />
            <values>0.0 2.0 4.0 6.0</values>
          </axis>
          <axis name="lat" shape="5" type="double" axisType="Lat">
            <attribute name="units" value="degrees_north" />
            <values>-80.0 -79.95999908447266 -79.91999816894531 -79.87999725341797 -79.83999633789062</values>
          </axis>
          <axis name="lon" shape="4" type="double" axisType="Lon">
            <attribute name="units" value="degrees_east" />
            <attribute name="modulo" value="360 degrees" />
            <values>0.0 0.0799560546875 0.1600341796875 0.239990234375</values>
          </axis>
          <axis name="time" shape="129" type="double" axisType="Time">
            <attribute name="units" value="hours since 2026-10-15 12:00:00.000 UTC" />
            <values start="0.0" increment="3.0" npts="129" />
          </axis>
          <axis name="time_run" shape="6" type="double" axisType="RunTime">
            <attribute name="standard_name" value="forecast_reference_time" />
            <attribute name="units" value="hours since 2026-10-15 12:00:00.000 UTC" />
            <values>0.0 0.0 24.0 96.0 192.0 216.0</values>
          </axis>
          <gridSet name="time_run time depth lat lon">
            <grid name="water_u" desc="Eastward Water Velocity" shape="time depth lat lon" type="float">
              <attribute name="standard_name" value="eastward_sea_water_velocity" />
            </grid>
            <grid name="water_v" desc="Northward Water Velocity" shape="time depth lat lon" type="float">
              <attribute name="standard_name" value="northward_sea_water_velocity" />
            </grid>
          </gridSet>
          <LatLonBox>
            <west>0.0000</west>
            <east>359.9200</east>
            <south>-80.0000</south>
            <north>90.0000</north>
          </LatLonBox>
          <TimeSpan>
            <begin>2026-10-15T12:00:00Z</begin>
            <end>2026-10-31T12:00:00Z</end>
          </TimeSpan>
        </gridDataset>
        """;

    /// <summary>The <c>dataset.xml</c> HYCOM's NCSS answers (#1270).</summary>
    public string? NcssDatasetXml { get; set; }

    /// <summary>The NetCDF-4 bytes HYCOM's NCSS answers for a subset.</summary>
    public byte[]? NcssNetCdf { get; set; }

    /// <summary>True to answer a ranged request with the whole file (200), as a server that ignores <c>Range</c> does.</summary>
    public bool IgnoreRanges { get; set; }

    private readonly List<(HttpMethod Method, Uri Uri, string? UserAgent, string? Range, string? Accept)> _requests = [];

    /// <summary>
    /// Every request so far, in order: a snapshot, since background reads can
    /// still be arriving while a test enumerates it.
    /// </summary>
    public IReadOnlyList<(HttpMethod Method, Uri Uri, string? UserAgent, string? Range, string? Accept)> Requests
    {
        get
        {
            lock (_requests)
                return [.. _requests];
        }
    }

    /// <summary>The bytes of the fake AWS file for <paramref name="hour"/>: the filler, then the hour's messages.</summary>
    public byte[] AwsFile(int hour) => [.. Filler, .. Messages[hour].SelectMany(m => m.Bytes)];

    /// <summary>The <c>.idx</c> inventory of <see cref="AwsFile"/>.</summary>
    public string AwsIndex(DateTimeOffset run, int hour)
    {
        var text = new StringBuilder();
        var date = run.UtcDateTime.ToString("yyyyMMddHH", CultureInfo.InvariantCulture);
        var fcst = hour == 0 ? "anl" : $"{hour} hour fcst";
        text.Append(CultureInfo.InvariantCulture, $"1:0:d={date}:WIND:surface:{fcst}:\n");
        long offset = Filler.Length;
        var number = 2;
        foreach (var (variable, level, bytes) in Messages[hour])
        {
            text.Append(CultureInfo.InvariantCulture, $"{number++}:{offset}:d={date}:{variable}:{level}:{fcst}:\n");
            offset += bytes.Length;
        }

        return text.ToString();
    }

    /// <summary>Splits appended GRIB2 messages (section 0 gives each one's length).</summary>
    public static IReadOnlyList<byte[]> Split(byte[] grib)
    {
        var messages = new List<byte[]>();
        var position = 0;
        while (position + 16 <= grib.Length)
        {
            long length = 0;
            for (int i = 8; i < 16; i++)
                length = (length << 8) | grib[position + i];
            messages.Add(grib[position..(position + (int)length)]);
            position += (int)length;
        }

        return messages;
    }

    private static readonly byte[] Filler = Encoding.ASCII.GetBytes(new string('x', 97));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var range = request.Headers.TryGetValues("Range", out var ranges) ? ranges.First() : null;
        lock (_requests)
            _requests.Add((request.Method, uri, request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null, range, request.Headers.Accept.ToString()));
        await Task.Yield();

        return uri.Host switch
        {
            "nomads.ncep.noaa.gov" when uri.AbsolutePath.StartsWith("/cgi-bin/", StringComparison.Ordinal) => Filter(uri),
            "nomads.ncep.noaa.gov" => Published(request, uri.AbsolutePath["/pub/data/nccf/com/gfs/prod/".Length..]),
            "noaa-gfs-bdp-pds.s3.amazonaws.com" => Aws(request, uri.AbsolutePath.TrimStart('/'), range),
            "api.weather.gov" => request.Headers.UserAgent.Count == 0
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : Bytes(Encoding.UTF8.GetBytes(AlertsJson), "application/geo+json"),
            "coastwatch.pfeg.noaa.gov" when uri.AbsolutePath.EndsWith("/index.json", StringComparison.Ordinal) && ErddapInfoJson is { } info =>
                Bytes(Encoding.UTF8.GetBytes(info), "application/json"),
            "coastwatch.pfeg.noaa.gov" when uri.AbsolutePath.EndsWith(".nc", StringComparison.Ordinal) && ErddapNetCdf is { } nc =>
                Bytes(nc, "application/x-netcdf"),
            "ncss.hycom.org" when uri.AbsolutePath.EndsWith("/dataset.xml", StringComparison.Ordinal) && NcssDatasetXml is { } xml =>
                Bytes(Encoding.UTF8.GetBytes(xml), "application/xml"),
            "ncss.hycom.org" when uri.AbsolutePath.EndsWith(".ncd", StringComparison.Ordinal) && NcssNetCdf is { } subset =>
                Bytes(subset, "application/x-netcdf4"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private HttpResponseMessage Filter(Uri uri)
    {
        var file = Uri.UnescapeDataString(Regex.Match(uri.Query, "[?&]file=([^&]+)").Groups[1].Value);
        if (HourOf(file) is not { } hour || !Messages.TryGetValue(hour, out var messages))
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        // As the live filters do (#1075): a subregion whose east edge is west of its west
        // edge (170° to −170°) or that spans more than a turn answers 200 with no messages.
        if (Query(uri, "leftlon") is { } left && Query(uri, "rightlon") is { } right && (right < left || right - left > 360))
            return Bytes([], "application/octet-stream");
        return Bytes([.. messages.SelectMany(m => m.Bytes)], "application/octet-stream");
    }

    private static double? Query(Uri uri, string name)
    {
        var match = Regex.Match(uri.Query, $"[?&]{name}=([^&]*)");
        return match.Success && double.TryParse(Uri.UnescapeDataString(match.Groups[1].Value), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private HttpResponseMessage Published(HttpRequestMessage request, string path) =>
        path.EndsWith(".idx", StringComparison.Ordinal) && RunOf(path) is { } run && Runs.Contains(run)
            && HourOf(path[..^4]) is { } hour && Messages.ContainsKey(hour)
            ? request.Method == HttpMethod.Head ? new HttpResponseMessage(HttpStatusCode.OK) : Text(AwsIndex(run, hour))
            : new HttpResponseMessage(HttpStatusCode.NotFound);

    private HttpResponseMessage Aws(HttpRequestMessage request, string path, string? range)
    {
        if (RunOf(path) is not { } run || !Runs.Contains(run))
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        if (path.EndsWith(".idx", StringComparison.Ordinal))
            return Published(request, path);
        if (HourOf(path) is not { } hour || !Messages.ContainsKey(hour))
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        var bytes = AwsFile(hour);
        if (range is null || IgnoreRanges)
            return Bytes(bytes, "binary/octet-stream");
        var match = Regex.Match(range, @"^bytes=(\d+)-(\d*)$");
        var first = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var last = match.Groups[2].Value.Length > 0 ? long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : bytes.Length - 1;
        var response = Bytes(bytes[(int)first..((int)last + 1)], "binary/octet-stream");
        response.StatusCode = HttpStatusCode.PartialContent;
        return response;
    }

    private static DateTimeOffset? RunOf(string path)
    {
        var match = Regex.Match(path, @"gfs\.(\d{8})/(\d{2})/");
        return match.Success
            ? DateTimeOffset.ParseExact(match.Groups[1].Value + match.Groups[2].Value, "yyyyMMddHH", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            : null;
    }

    private static int? HourOf(string file)
    {
        var match = Regex.Match(file, @"\.f(\d{3})(\.grib2)?$");
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static HttpResponseMessage Text(string text) => Bytes(Encoding.ASCII.GetBytes(text), "text/plain");

    private static HttpResponseMessage Bytes(byte[] bytes, string mediaType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
