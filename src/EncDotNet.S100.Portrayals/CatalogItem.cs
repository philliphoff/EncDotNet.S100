namespace EncDotNet.S100.Portrayals;

/// <summary>
/// A file-backed asset declared by a <see cref="PortrayalCatalogue"/>, such as
/// a symbol, line style, area fill, colour profile, pixmap, style sheet or the
/// alert catalogue. The asset itself is loaded separately (see
/// <see cref="PortrayalCatalogueProvider"/>) from the catalogue folder that
/// matches its collection (e.g. <c>Symbols/</c>).
/// </summary>
public sealed class CatalogItem
{
    /// <summary>
    /// Item identifier, from the element's <c>@id</c> attribute; empty when the
    /// attribute is missing. Rule and symbol references resolve against this id.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The item's <c>description</c> block.</summary>
    public required Description Description { get; init; }

    /// <summary>
    /// Asset file name relative to its collection folder, from <c>fileName</c>
    /// (e.g. <c>BOYCAN01.svg</c>).
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Kind of file, from <c>fileType</c> as written in the catalogue
    /// (e.g. <c>Symbol</c>, <c>LineStyle</c>, <c>ColorProfile</c>,
    /// <c>AlertCatalog</c>).
    /// </summary>
    public required string FileType { get; init; }

    /// <summary>
    /// Encoding of the file, from <c>fileFormat</c> as written in the catalogue
    /// (e.g. <c>SVG</c>, <c>XML</c>, <c>CSS</c>).
    /// </summary>
    public required string FileFormat { get; init; }
}
