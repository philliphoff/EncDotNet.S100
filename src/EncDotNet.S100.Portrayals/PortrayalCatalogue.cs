using EncDotNet.S100.Core;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Parsed model of an S-100 Part 9 Portrayal Catalogue
/// (<c>portrayal_catalogue.xml</c>): the asset inventories, rule files,
/// viewing groups and layers, display modes and planes, and context
/// parameters used to portray one product specification. Produced by
/// <see cref="PortrayalCatalogueReader"/>; the referenced files are loaded
/// through <see cref="PortrayalCatalogueProvider"/>.
/// </summary>
/// <remarks>
/// Collection properties are never <see langword="null"/>; they are empty when
/// the corresponding section is absent from the XML.
/// </remarks>
public sealed class PortrayalCatalogue
{
    /// <summary>
    /// Product specification the catalogue portrays, verbatim from the root
    /// <c>@productId</c> attribute (e.g. <c>S-101</c>); empty when absent.
    /// </summary>
    public required string ProductId { get; init; }

    /// <summary>
    /// Catalogue version, verbatim from the root <c>@version</c> attribute;
    /// empty when absent. See <see cref="CatalogueRef"/> for the parsed form.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// The Portrayal Catalogue identity tuple <c>(ProductId, Version)</c>
    /// projected into a strongly-typed <see cref="Core.CatalogueRef"/>, or
    /// <c>null</c> when either field is missing or unparseable. This is the
    /// preferred way to identify a catalogue instance for caching and
    /// compatibility checks (S-100 Edition 5.2.1 Part 2 §6).
    /// </summary>
    public CatalogueRef? CatalogueRef { get; init; }

    /// <summary>
    /// The alert catalogue file, from <c>alertCatalog</c>, or
    /// <see langword="null"/> when the catalogue declares none.
    /// </summary>
    public CatalogItem? AlertCatalog { get; init; }

    /// <summary>Raster images, from <c>pixmaps/pixmap</c>; loaded from the <c>Pixmaps/</c> folder.</summary>
    public IReadOnlyList<CatalogItem> Pixmaps { get; init; } = [];

    /// <summary>
    /// Colour profiles (palettes mapping colour tokens to day/dusk/night
    /// colours), from <c>colorProfiles/colorProfile</c>; loaded from the
    /// <c>ColorProfiles/</c> folder and parsed by <see cref="ColorProfileReader"/>.
    /// </summary>
    public IReadOnlyList<CatalogItem> ColorProfiles { get; init; } = [];

    /// <summary>SVG point symbols, from <c>symbols/symbol</c>; loaded from the <c>Symbols/</c> folder.</summary>
    public IReadOnlyList<CatalogItem> Symbols { get; init; } = [];

    /// <summary>CSS style sheets referenced by the SVG symbols, from <c>styleSheets/styleSheet</c>; loaded from the <c>StyleSheets/</c> folder.</summary>
    public IReadOnlyList<CatalogItem> StyleSheets { get; init; } = [];

    /// <summary>
    /// Line style definitions, from <c>lineStyles/lineStyle</c>; loaded from
    /// the <c>LineStyles/</c> folder and parsed by <see cref="LineStyleReader"/>.
    /// </summary>
    public IReadOnlyList<CatalogItem> LineStyles { get; init; } = [];

    /// <summary>
    /// Area fill (pattern) definitions, from <c>areaFills/areaFill</c>; loaded
    /// from the <c>AreaFills/</c> folder and parsed by <see cref="AreaFillReader"/>.
    /// </summary>
    public IReadOnlyList<CatalogItem> AreaFills { get; init; } = [];

    /// <summary>
    /// Viewing groups declared under <c>viewingGroups</c>. Only
    /// <c>viewingGroup</c> elements with an <c>@id</c> are included; id-only
    /// references are skipped.
    /// </summary>
    public IReadOnlyList<ViewingGroup> ViewingGroups { get; init; } = [];

    /// <summary>
    /// Ids of the viewing groups in the foundation mode — the base content
    /// that is always displayed — from the text of each
    /// <c>foundationMode/viewingGroup</c> element (trimmed; empty entries dropped).
    /// </summary>
    public IReadOnlyList<string> FoundationModeViewingGroupIds { get; init; } = [];

    /// <summary>Viewing-group layers, from <c>viewingGroupLayers/viewingGroupLayer</c>.</summary>
    public IReadOnlyList<ViewingGroupLayer> ViewingGroupLayers { get; init; } = [];

    /// <summary>Display modes, from <c>displayModes/displayMode</c>.</summary>
    public IReadOnlyList<DisplayMode> DisplayModes { get; init; } = [];

    /// <summary>Display planes, from <c>displayPlanes/displayPlane</c>.</summary>
    public IReadOnlyList<DisplayPlane> DisplayPlanes { get; init; } = [];

    /// <summary>Context parameters with their defaults, from <c>context/parameter</c>.</summary>
    public IReadOnlyList<ContextParameter> ContextParameters { get; init; } = [];

    /// <summary>XSLT or Lua portrayal rule files, from <c>rules/ruleFile</c>.</summary>
    public IReadOnlyList<RuleFile> RuleFiles { get; init; } = [];
}
