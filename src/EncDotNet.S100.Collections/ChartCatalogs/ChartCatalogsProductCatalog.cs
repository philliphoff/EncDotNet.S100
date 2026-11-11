using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.Collections.ChartCatalogs;

/// <summary>
/// A parsed community chart list in the <c>chartcatalogs</c> format (root
/// <c>RncProductCatalogChartCatalogs</c>, a subset of NOAA's RNC product
/// catalogue), as published under CC0 at
/// <c>https://github.com/chartcatalogs/catalogs</c>.
/// </summary>
/// <param name="Title">The list title (e.g. <c>Romania IENC Charts</c>).</param>
/// <param name="ValidAt">When the list was generated, if given.</param>
/// <param name="Charts">The listed charts, in document order.</param>
public sealed record ChartCatalogsProductCatalog(string? Title, DateTimeOffset? ValidAt, IReadOnlyList<ChartCatalogsChart> Charts);

/// <summary>
/// One entry of a community chart list. An entry is a <em>download</em> — a
/// zip that may hold one cell, several cells or a whole exchange set, or
/// occasionally a bare <c>.000</c> file — not necessarily a single cell.
/// </summary>
public sealed record ChartCatalogsChart
{
    /// <summary>The entry's identifier (e.g. <c>Base1</c>, <c>BR7P0400</c>); unique within a list in practice.</summary>
    public required string Number { get; init; }

    /// <summary>The entry title.</summary>
    public string? Title { get; init; }

    /// <summary>The download (zip or bare cell).</summary>
    public required Uri DownloadUri { get; init; }

    /// <summary>The file name to save the download as, when the URL does not say.</summary>
    public string? TargetFileName { get; init; }

    /// <summary>When the download was last published.</summary>
    public DateTimeOffset? PublishedAt { get; init; }
}

/// <summary>
/// Reads community chart lists (<c>RncProductCatalogChartCatalogs</c>).
/// Entries without a number or an absolute download URL are skipped; unknown
/// elements are ignored.
/// </summary>
public static class ChartCatalogsProductCatalogReader
{
    /// <summary>The root element name.</summary>
    public const string RootElementName = "RncProductCatalogChartCatalogs";

    /// <summary>Reads the list at <paramref name="path"/>.</summary>
    public static ChartCatalogsProductCatalog Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Reads a list from <paramref name="stream"/>.</summary>
    /// <exception cref="XmlException">The content is not a community chart list.</exception>
    public static ChartCatalogsProductCatalog Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var root = XDocument.Load(stream).Root
            ?? throw new XmlException("Missing root element.");
        if (root.Name.LocalName != RootElementName)
            throw new XmlException($"Expected a {RootElementName} chart list, found '{root.Name.LocalName}'.");

        var header = root.Element("Header");
        var validAt = Instant(Text(header, "dt_valid"))
            ?? (DateOnly.TryParseExact(Text(header, "date_created"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                : null);

        return new ChartCatalogsProductCatalog(
            Text(header, "title"),
            validAt,
            root.Elements("chart").Select(ReadChart).OfType<ChartCatalogsChart>().ToArray());
    }

    private static ChartCatalogsChart? ReadChart(XElement chart)
    {
        var number = Text(chart, "number");
        if (number is null || !Uri.TryCreate(Text(chart, "zipfile_location"), UriKind.Absolute, out var uri))
            return null;

        return new ChartCatalogsChart
        {
            Number = number,
            Title = Text(chart, "title"),
            DownloadUri = uri,
            TargetFileName = Text(chart, "target_filename"),
            PublishedAt = Instant(Text(chart, "zipfile_datetime_iso8601"))
                ?? (DateTime.TryParseExact(Text(chart, "zipfile_datetime"), "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
                    ? new DateTimeOffset(t, TimeSpan.Zero)
                    : null),
        };
    }

    private static DateTimeOffset? Instant(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value.ToUniversalTime()
            : null;

    private static string? Text(XElement? parent, string name)
    {
        var value = parent?.Element(name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
