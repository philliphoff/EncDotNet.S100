using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Collections.Noaa;

/// <summary>
/// Reads a NOAA ENC product catalogue (<c>ENCProdCat.xml</c>).
/// </summary>
/// <remarks>
/// The full catalogue is about 10 MB with more than 7,000 cells, so it is
/// streamed: each <c>&lt;cell&gt;</c> element is materialised on its own and
/// discarded once mapped. Unknown elements are ignored and malformed values
/// read as absent, so a producer-side addition never fails the read.
/// </remarks>
public static class NoaaEncProductCatalogReader
{
    /// <summary>Reads the catalogue at <paramref name="path"/>.</summary>
    public static NoaaEncProductCatalog Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Reads a catalogue from <paramref name="stream"/>.</summary>
    /// <exception cref="XmlException">The content is not a NOAA ENC product catalogue.</exception>
    public static NoaaEncProductCatalog Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Prohibit,
        });

        if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "EncProductCatalog")
            throw new XmlException($"Expected an EncProductCatalog root element, found '{reader.LocalName}'.");

        var header = new NoaaEncCatalogHeader(null, null, null);
        var cells = new List<NoaaEncCell>();

        reader.Read();
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            switch (reader.LocalName)
            {
                case "Header":
                    header = ReadHeader((XElement)XNode.ReadFrom(reader));
                    break;
                case "cell":
                    if (ReadCell((XElement)XNode.ReadFrom(reader)) is { } cell)
                        cells.Add(cell);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new NoaaEncProductCatalog(header, cells);
    }

    private static NoaaEncCatalogHeader ReadHeader(XElement element) =>
        new(
            Text(element, "title"),
            DateTimeOffset.TryParse(
                Text(element, "dt_valid"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var valid) ? valid : null,
            Int(element, "s62AgencyCode"));

    private static NoaaEncCell? ReadCell(XElement element)
    {
        var name = Text(element, "name");
        if (string.IsNullOrEmpty(name))
            return null;

        return new NoaaEncCell
        {
            Name = name,
            LongName = CollapseWhitespace(Text(element, "lname")),
            CompilationScale = Int(element, "cscale"),
            IsCancelled = string.Equals(Text(element, "status"), "Cancelled", StringComparison.OrdinalIgnoreCase),
            CoastGuardDistricts = Ints(element.Element("coast_guard_districts")),
            States = element.Element("states")?.Elements().Select(e => e.Value.Trim())
                .Where(s => s.Length > 0).ToArray() ?? [],
            Regions = Ints(element.Element("regions")),
            ZipUri = Uri.TryCreate(Text(element, "zipfile_location"), UriKind.Absolute, out var uri) ? uri : null,
            ZipDateTime = DateTimeOffset.TryParse(
                Text(element, "zipfile_datetime_iso8601"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var zipTime) ? zipTime : null,
            ZipSize = long.TryParse(
                Text(element, "zipfile_size"), NumberStyles.None, CultureInfo.InvariantCulture, out var size) ? size : null,
            Edition = Int(element, "edtn"),
            Update = Int(element, "updn"),
            UpdateApplicationDate = Date(element, "uadt"),
            IssueDate = Date(element, "isdt"),
            Panels = element.Element("cov")?.Elements("panel").Select(ReadPanel).OfType<NoaaEncPanel>().ToArray() ?? [],
        };
    }

    private static NoaaEncPanel? ReadPanel(XElement panel)
    {
        var vertices = new List<GeoPosition>();
        foreach (var vertex in panel.Elements("vertex"))
        {
            if (!double.TryParse(Text(vertex, "lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(Text(vertex, "long"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                return null;
            }

            vertices.Add(new GeoPosition(lat, lon));
        }

        if (vertices.Count < 3)
            return null;

        return new NoaaEncPanel(
            Int(panel, "panel_no") ?? 0,
            string.Equals(Text(panel, "type"), "I", StringComparison.OrdinalIgnoreCase),
            vertices);
    }

    private static string? Text(XElement parent, string name) =>
        parent.Element(name)?.Value.Trim();

    private static int? Int(XElement parent, string name) =>
        int.TryParse(Text(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static DateOnly? Date(XElement parent, string name) =>
        DateOnly.TryParseExact(Text(parent, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    private static int[] Ints(XElement? list) =>
        list?.Elements()
            .Select(e => int.TryParse(e.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? (int?)n : null)
            .OfType<int>()
            .ToArray() ?? [];

    private static string? CollapseWhitespace(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
