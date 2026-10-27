using System.Globalization;
using System.Xml;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.DynamicSources.Ndbc;

/// <summary>
/// The NDBC station table: every active station with its position, read from
/// <c>https://www.ndbc.noaa.gov/activestations.xml</c>.
/// </summary>
/// <remarks>
/// <para>
/// The file is a flat list of <c>&lt;station&gt;</c> elements under
/// <c>&lt;stations&gt;</c>:
/// </para>
/// <code>
/// &lt;station id="44013" lat="42.346" lon="-70.651" elev="0" name="BOSTON 16 NM East of Boston, MA"
///          owner="NDBC" pgm="NDBC Meteorological/Ocean" type="buoy" met="y" currents="n"
///          waterquality="n" dart="n"/&gt;
/// </code>
/// <para>
/// Stations with no id or an unreadable position are skipped. Ids are upper
/// cased, to match the <c>realtime2</c> file names.
/// </para>
/// </remarks>
public sealed class NdbcStationTable
{
    /// <summary>Creates a table of <paramref name="stations"/>.</summary>
    /// <param name="stations">The stations.</param>
    public NdbcStationTable(IEnumerable<NdbcStation> stations)
    {
        ArgumentNullException.ThrowIfNull(stations);
        Stations = stations.ToArray();
    }

    /// <summary>The stations, in file order.</summary>
    public IReadOnlyList<NdbcStation> Stations { get; }

    /// <summary>The stations with meteorological data whose position is inside <paramref name="area"/>, in table order.</summary>
    /// <param name="area">The area; its west edge may be east of its east edge when it crosses the antimeridian.</param>
    public IEnumerable<NdbcStation> MeteorologicalStationsWithin(BoundingBox area)
    {
        ArgumentNullException.ThrowIfNull(area);
        return Stations.Where(s => s.HasMeteorology && Contains(area, s.Latitude, s.Longitude));
    }

    /// <summary>Reads an <c>activestations.xml</c> document.</summary>
    /// <param name="stream">The document.</param>
    /// <exception cref="XmlException">The document isn't well-formed XML.</exception>
    public static NdbcStationTable Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };

        var stations = new List<NdbcStation>();
        using var reader = XmlReader.Create(stream, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "station")
                continue;

            string? id = reader.GetAttribute("id");
            if (string.IsNullOrWhiteSpace(id)
                || !TryParseCoordinate(reader.GetAttribute("lat"), 90, out double lat)
                || !TryParseCoordinate(reader.GetAttribute("lon"), 180, out double lon))
            {
                continue;
            }

            stations.Add(new NdbcStation(
                id.Trim().ToUpperInvariant(),
                reader.GetAttribute("name")?.Trim() ?? id.Trim(),
                lat,
                lon)
            {
                Type = reader.GetAttribute("type"),
                Owner = reader.GetAttribute("owner"),
                Program = reader.GetAttribute("pgm"),
                HasMeteorology = string.Equals(reader.GetAttribute("met"), "y", StringComparison.OrdinalIgnoreCase),
            });
        }

        return new NdbcStationTable(stations);
    }

    /// <summary>Whether (<paramref name="latitude"/>, <paramref name="longitude"/>) is inside <paramref name="area"/>, edges included.</summary>
    internal static bool Contains(BoundingBox area, double latitude, double longitude)
    {
        if (latitude < area.SouthLatitude || latitude > area.NorthLatitude)
            return false;
        return area.WestLongitude <= area.EastLongitude
            ? longitude >= area.WestLongitude && longitude <= area.EastLongitude
            : longitude >= area.WestLongitude || longitude <= area.EastLongitude;
    }

    private static bool TryParseCoordinate(string? text, double limit, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value)
        && Math.Abs(value) <= limit;
}
