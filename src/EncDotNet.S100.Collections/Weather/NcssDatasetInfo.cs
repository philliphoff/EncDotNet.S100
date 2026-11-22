using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.Collections.Weather;

/// <summary>
/// What a THREDDS NetCDF Subset Service grid dataset offers (#1270), from its
/// <c>…/dataset.xml</c>: the NCSS counterpart of an ERDDAP dataset's <c>info</c>.
/// </summary>
/// <param name="Start">The first time the dataset holds (<c>&lt;TimeSpan&gt;&lt;begin&gt;</c>), if stated.</param>
/// <param name="End">The last time it holds (<c>&lt;TimeSpan&gt;&lt;end&gt;</c>), if stated.</param>
/// <param name="LatestRun">
/// For a forecast model run collection, the newest run (the largest value of
/// its <c>RunTime</c> axis, <c>time_run</c>); null when it has none.
/// </param>
/// <param name="LatitudeSpacing">The distance between latitudes, in degrees, if known.</param>
/// <param name="LongitudeSpacing">The distance between longitudes, in degrees, if known.</param>
/// <param name="FirstLevel">The first value of a vertical axis (0 m for an ocean model's top layer), or null for none.</param>
/// <param name="Area">The dataset's area (<c>&lt;LatLonBox&gt;</c>), as the server states it (longitudes may run 0 to 360).</param>
/// <param name="Grids">The names of the grids (variables) it offers.</param>
public sealed record NcssDatasetInfo(
    DateTimeOffset? Start,
    DateTimeOffset? End,
    DateTimeOffset? LatestRun,
    double? LatitudeSpacing,
    double? LongitudeSpacing,
    double? FirstLevel,
    GeoBounds? Area,
    IReadOnlyList<string> Grids)
{
    /// <summary>The span of latitudes the dataset covers, in degrees, if known.</summary>
    public double? LatitudeSpan => Area is { } a ? a.North - a.South : null;

    /// <summary>The span of longitudes the dataset covers, in degrees, if known.</summary>
    public double? LongitudeSpan => Area is { } a ? (a.East >= a.West ? a.East - a.West : a.East + 360 - a.West) : null;

    /// <summary>
    /// Reads a <c>dataset.xml</c>: the <c>&lt;TimeSpan&gt;</c>, the
    /// <c>&lt;LatLonBox&gt;</c>, the spacing of the <c>Lat</c> and <c>Lon</c>
    /// axes (from <c>start</c>/<c>increment</c>, or from their listed values),
    /// a vertical (<c>Height</c>, <c>GeoZ</c>, <c>Pressure</c>) axis's first
    /// value, the newest <c>RunTime</c> value and the grid names.
    /// </summary>
    /// <exception cref="InvalidDataException">The document isn't an NCSS grid dataset description.</exception>
    public static NcssDatasetInfo Parse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"The NCSS dataset description is not XML: {ex.Message}", ex);
        }

        if (document.Root is not { Name.LocalName: "gridDataset" } root)
            throw new InvalidDataException("The NCSS dataset description has no gridDataset; is it a grid dataset?");

        double? latitudeSpacing = null, longitudeSpacing = null, firstLevel = null;
        DateTimeOffset? latestRun = null;
        foreach (var axis in root.Elements("axis"))
        {
            var type = (string?)axis.Attribute("axisType") ?? string.Empty;
            var values = axis.Element("values");
            switch (type)
            {
                case "Lat":
                    latitudeSpacing = Spacing(values);
                    break;
                case "Lon":
                    longitudeSpacing = Spacing(values);
                    break;
                case "Height" or "GeoZ" or "Pressure":
                    firstLevel = Values(values).Cast<double?>().FirstOrDefault();
                    break;
                case "RunTime":
                    var units = axis.Elements("attribute").FirstOrDefault(a => (string?)a.Attribute("name") == "units")?.Attribute("value")?.Value;
                    var last = Values(values).Where(double.IsFinite).DefaultIfEmpty(double.NaN).Max();
                    if (double.IsFinite(last) && TimeOf(units, last) is { } run)
                        latestRun = run;
                    break;
            }
        }

        GeoBounds? area = null;
        if (root.Element("LatLonBox") is { } box
            && Number(box.Element("south")) is { } south && Number(box.Element("west")) is { } west
            && Number(box.Element("north")) is { } north && Number(box.Element("east")) is { } east)
        {
            area = new GeoBounds(south, west, north, east);
        }

        var span = root.Element("TimeSpan");
        return new NcssDatasetInfo(
            Time(span?.Element("begin")),
            Time(span?.Element("end")),
            latestRun,
            latitudeSpacing,
            longitudeSpacing,
            firstLevel,
            area,
            [.. root.Descendants("grid").Select(g => (string?)g.Attribute("name")).OfType<string>()]);
    }

    /// <summary>A time in CF units (<c>hours since 2026-11-12 12:00:00.000 UTC</c>), or null when the units aren't understood.</summary>
    internal static DateTimeOffset? TimeOf(string? units, double value)
    {
        if (units is null)
            return null;
        var since = units.IndexOf(" since ", StringComparison.OrdinalIgnoreCase);
        if (since <= 0)
            return null;
        var origin = units[(since + 7)..].Trim();
        if (origin.EndsWith(" UTC", StringComparison.OrdinalIgnoreCase))
            origin = origin[..^4].Trim();
        if (!DateTimeOffset.TryParse(origin, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start))
            return null;
        TimeSpan? step = units[..since].Trim().ToLowerInvariant() switch
        {
            "days" or "day" => TimeSpan.FromDays(value),
            "hours" or "hour" => TimeSpan.FromHours(value),
            "minutes" or "minute" => TimeSpan.FromMinutes(value),
            "seconds" or "second" => TimeSpan.FromSeconds(value),
            _ => null,
        };
        return step is { } offset ? start + offset : null;
    }

    private static double? Spacing(XElement? values)
    {
        if (values is null)
            return null;
        if (Number(values.Attribute("increment")) is { } increment)
            return Math.Abs(increment);
        var listed = Values(values);
        return listed.Count >= 2 ? Math.Abs(listed[^1] - listed[0]) / (listed.Count - 1) : null;
    }

    private static List<double> Values(XElement? values)
    {
        if (values is null)
            return [];
        if (Number(values.Attribute("start")) is { } start)
        {
            var increment = Number(values.Attribute("increment")) ?? 0;
            var count = (int)(Number(values.Attribute("npts")) ?? 1);
            return [.. Enumerable.Range(0, Math.Max(count, 0)).Select(i => start + (i * increment))];
        }

        return [.. values.Value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN)];
    }

    private static double? Number(XObject? node)
    {
        var text = node switch
        {
            XElement e => e.Value,
            XAttribute a => a.Value,
            _ => null,
        };
        return double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static DateTimeOffset? Time(XElement? element) =>
        element is not null
        && DateTimeOffset.TryParse(element.Value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
            ? time
            : null;
}
