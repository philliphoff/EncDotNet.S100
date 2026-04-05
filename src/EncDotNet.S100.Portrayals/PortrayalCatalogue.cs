namespace EncDotNet.S100.Portrayals;

public sealed class PortrayalCatalogue
{
    public required string ProductId { get; init; }

    public required string Version { get; init; }

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
