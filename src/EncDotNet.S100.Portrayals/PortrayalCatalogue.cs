using EncDotNet.S100.Core;

namespace EncDotNet.S100.Portrayals;

public sealed class PortrayalCatalogue
{
    public required string ProductId { get; init; }

    public required string Version { get; init; }

    /// <summary>
    /// The Portrayal Catalogue identity tuple <c>(ProductId, Version)</c>
    /// projected into a strongly-typed <see cref="Core.CatalogueRef"/>, or
    /// <c>null</c> when either field is missing or unparseable. This is the
    /// preferred way to identify a catalogue instance for caching and
    /// compatibility checks (S-100 Edition 5.2.1 Part 2 §6).
    /// </summary>
    public CatalogueRef? CatalogueRef { get; init; }

    public CatalogItem? AlertCatalog { get; init; }

    public IReadOnlyList<CatalogItem> Pixmaps { get; init; } = [];

    public IReadOnlyList<CatalogItem> ColorProfiles { get; init; } = [];

    public IReadOnlyList<CatalogItem> Symbols { get; init; } = [];

    public IReadOnlyList<CatalogItem> StyleSheets { get; init; } = [];

    public IReadOnlyList<CatalogItem> LineStyles { get; init; } = [];

    public IReadOnlyList<CatalogItem> AreaFills { get; init; } = [];

    public IReadOnlyList<ViewingGroup> ViewingGroups { get; init; } = [];

    public IReadOnlyList<string> FoundationModeViewingGroupIds { get; init; } = [];

    public IReadOnlyList<ViewingGroupLayer> ViewingGroupLayers { get; init; } = [];

    public IReadOnlyList<DisplayMode> DisplayModes { get; init; } = [];

    public IReadOnlyList<DisplayPlane> DisplayPlanes { get; init; } = [];

    public IReadOnlyList<ContextParameter> ContextParameters { get; init; } = [];

    public IReadOnlyList<RuleFile> RuleFiles { get; init; } = [];
}
