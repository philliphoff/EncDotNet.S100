using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.Collections.Usace;

/// <summary>
/// A parsed USACE Inland ENC product catalogue ("USACE IENC Product Catalog
/// Technical Specifications"), such as <c>IENCU37ProductsCatalog.xml</c>
/// (river cells) or <c>IENCBuoyProductsCatalog.xml</c> (the buoy overlay).
/// </summary>
/// <param name="Title">The catalogue title.</param>
/// <param name="CreatedOn">When the catalogue was generated, if given.</param>
/// <param name="Cells">The catalogued cells, in document order.</param>
public sealed record UsaceIencProductCatalog(string? Title, DateOnly? CreatedOn, IReadOnlyList<UsaceIencCell> Cells);

/// <summary>One cell of a USACE Inland ENC product catalogue.</summary>
public sealed record UsaceIencCell
{
    /// <summary>The cell name (e.g. <c>U37AG001</c>).</summary>
    public required string Name { get; init; }

    /// <summary>The river the cell covers (e.g. <c>Ohio</c>), when given.</summary>
    public string? River { get; init; }

    /// <summary>The upstream/downstream description start (e.g. <c>Pittsburgh, PA</c>).</summary>
    public string? From { get; init; }

    /// <summary>The upstream/downstream description end (e.g. <c>Allegheny Lock No. 8</c>).</summary>
    public string? To { get; init; }

    /// <summary>The first river mile covered.</summary>
    public double? RiverMileBegin { get; init; }

    /// <summary>The last river mile covered.</summary>
    public double? RiverMileEnd { get; init; }

    /// <summary>The cell's bounding box (<c>area</c>).</summary>
    public GeoBounds? Bounds { get; init; }

    /// <summary>The edition number (the part of <c>edition</c> before the dot).</summary>
    public int? Edition { get; init; }

    /// <summary>The update number (the part of <c>edition</c> after the dot).</summary>
    public int? Update { get; init; }

    /// <summary>The S-57 exchange-set zip.</summary>
    public Uri? ZipUri { get; init; }

    /// <summary>When the S-57 zip was posted.</summary>
    public DateOnly? PostedOn { get; init; }

    /// <summary>The S-57 zip size in bytes, parsed from text such as <c>7.7 MB</c>.</summary>
    public long? ZipSize { get; init; }
}

/// <summary>
/// Reads USACE Inland ENC product catalogues. Any root element named
/// <c>IENC…ProductCatalog</c> is accepted (river cells, buoy overlay,
/// Southwest Pass); unknown elements are ignored and malformed values read as
/// absent.
/// </summary>
public static partial class UsaceIencProductCatalogReader
{
    /// <summary>Reads the catalogue at <paramref name="path"/>.</summary>
    public static UsaceIencProductCatalog Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Reads a catalogue from <paramref name="stream"/>.</summary>
    /// <exception cref="XmlException">The content is not a USACE IENC product catalogue.</exception>
    public static UsaceIencProductCatalog Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var root = XDocument.Load(stream).Root
            ?? throw new XmlException("Missing root element.");
        if (!IsCatalogueRoot(root.Name.LocalName))
            throw new XmlException($"Expected a USACE IENC product catalogue, found '{root.Name.LocalName}'.");

        var header = root.Element("Header");
        return new UsaceIencProductCatalog(
            Text(header, "title"),
            Date(Text(header, "date_created")),
            root.Elements("Cell").Select(ReadCell).OfType<UsaceIencCell>().ToArray());
    }

    /// <summary>True for a USACE IENC catalogue root element name.</summary>
    public static bool IsCatalogueRoot(string localName) =>
        localName.StartsWith("IENC", StringComparison.Ordinal)
        && localName.EndsWith("ProductCatalog", StringComparison.Ordinal);

    private static UsaceIencCell? ReadCell(XElement cell)
    {
        var name = Text(cell, "name");
        if (string.IsNullOrEmpty(name))
            return null;

        var (edition, update) = ParseEdition(Text(cell, "edition"));
        var s57 = cell.Element("s57_file");
        var area = cell.Element("area");

        return new UsaceIencCell
        {
            Name = name,
            River = Text(cell, "river_name"),
            From = Text(cell.Element("location"), "from"),
            To = Text(cell.Element("location"), "to"),
            RiverMileBegin = Number(Text(cell.Element("river_miles"), "begin")),
            RiverMileEnd = Number(Text(cell.Element("river_miles"), "end")),
            Bounds = Number(Text(area, "south")) is { } s && Number(Text(area, "west")) is { } w
                && Number(Text(area, "north")) is { } n && Number(Text(area, "east")) is { } e
                    ? new GeoBounds(s, w, n, e)
                    : null,
            Edition = edition,
            Update = update,
            ZipUri = Uri.TryCreate(Text(s57, "location"), UriKind.Absolute, out var uri) ? uri : null,
            PostedOn = Date(Text(s57, "date_posted")),
            ZipSize = ParseSize(Text(s57, "file_size")),
        };
    }

    /// <summary>Splits USACE's <c>edition.update</c> (e.g. <c>22.16</c>) into its numbers.</summary>
    internal static (int? Edition, int? Update) ParseEdition(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, null);

        var parts = text.Trim().Split('.');
        int? Part(int i) => i < parts.Length
            && int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
        return (Part(0), Part(1) ?? (Part(0) is null ? null : 0));
    }

    /// <summary>
    /// Parses a human-readable size such as <c>7.7 MB</c> or <c>0.3 MB</c> into
    /// bytes. Stray spaces inside the number (the live catalogue has
    /// <c>7. 7 MB</c>) are tolerated.
    /// </summary>
    internal static long? ParseSize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = SizePattern().Match(text);
        if (!match.Success)
            return null;

        var number = match.Groups["number"].Value.Replace(" ", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;

        var factor = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "KB" => 1024d,
            "MB" => 1024d * 1024,
            "GB" => 1024d * 1024 * 1024,
            _ => 1d,
        };
        return (long)Math.Round(value * factor);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*(?<number>\d+(?:\s*[.,]\s*\d+)?)\s*(?<unit>[KMG]?B)?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex SizePattern();

    private static string? Text(XElement? parent, string name)
    {
        var value = parent?.Element(name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>USACE writes dates as <c>MM/DD/YYYY</c> (sometimes without leading zeros).</summary>
    private static DateOnly? Date(string? text) =>
        DateOnly.TryParseExact(text, ["MM/dd/yyyy", "M/d/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
}
