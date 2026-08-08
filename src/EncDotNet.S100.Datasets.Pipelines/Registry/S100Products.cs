namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The built-in per-product <see cref="S100ProductRegistration"/>s for every
/// S-100 product this library ships a processor for, and helpers to register
/// them. This is the one place that knows how each product's processor is
/// constructed; it replaces the former hard-coded <c>switch</c> in
/// <see cref="DatasetPipelineFactory"/>. A host that wants every product calls
/// <see cref="S100ProductRegistryExtensions.AddAllS100Products"/> (the default
/// <see cref="DatasetPipelineFactory"/> does this); a host that wants a subset
/// registers only the products it needs.
/// </summary>
public static class S100Products
{
    /// <summary>Creates a registry pre-loaded with every built-in product.</summary>
    public static S100ProductRegistry CreateDefaultRegistry() =>
        new S100ProductRegistry().AddAllS100Products();

    /// <summary>Registers every built-in product with <paramref name="registry"/>.</summary>
    public static void RegisterAll(S100ProductRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(S101);
        registry.Register(S57);
        registry.Register(S102);
        registry.Register(S104);
        registry.Register(S111);
        registry.Register(S122);
        registry.Register(S124);
        registry.Register(S125);
        registry.Register(S127);
        registry.Register(S128);
        registry.Register(S129);
        registry.Register(S131);
        registry.Register(S201);
        registry.Register(S411);
        registry.Register(S421);
    }

    /// <summary>S-101 Electronic Navigational Chart.</summary>
    public static S100ProductRegistration S101 { get; } = new()
    {
        Spec = "S-101",
        CreateFromPath = (s, path) => new S101DatasetProcessor(
            path, s.CatalogueManager, s.LuaEngine, s.FeatureCatalogueManager,
            s.SharedInstructionCache, s.SharedLineLodCache),
        CreateFromSource = (s, r) => new S101DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.LuaEngine,
            s.FeatureCatalogueManager, s.SharedInstructionCache, r.SupportFiles,
            s.SharedLineLodCache),
    };

    /// <summary>Legacy S-57 ENC (translated in-memory to S-101).</summary>
    public static S100ProductRegistration S57 { get; } = new()
    {
        Spec = "S-57",
        CreateFromPath = (s, path) => new S57DatasetProcessor(
            path, s.CatalogueManager, s.LuaEngine, s.FeatureCatalogueManager),
        CreateFromSource = (s, r) => new S57DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.LuaEngine,
            s.FeatureCatalogueManager),
    };

    /// <summary>S-102 Bathymetric Surface.</summary>
    public static S100ProductRegistration S102 { get; } = new()
    {
        Spec = "S-102",
        CreateFromPath = (s, path) => new S102DatasetProcessor(
            path, s.CatalogueManager, s.LuaEngine, s.CrsTransformFactory),
        CreateFromSource = (s, r) => new S102DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.LuaEngine,
            s.CrsTransformFactory),
    };

    /// <summary>S-104 Water Level Information for Surface Navigation.</summary>
    public static S100ProductRegistration S104 { get; } = new()
    {
        Spec = "S-104",
        CreateFromPath = (s, path) => new S104DatasetProcessor(
            path, s.CrsTransformFactory),
        CreateFromSource = (s, r) => new S104DatasetProcessor(
            r.Source, r.RelativePath, s.CrsTransformFactory),
    };

    /// <summary>S-111 Surface Currents.</summary>
    public static S100ProductRegistration S111 { get; } = new()
    {
        Spec = "S-111",
        CreateFromPath = (s, path) => new S111DatasetProcessor(
            path, s.CatalogueManager, s.CrsTransformFactory),
        CreateFromSource = (s, r) => new S111DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.CrsTransformFactory),
    };

    /// <summary>S-122 Marine Protected Areas.</summary>
    public static S100ProductRegistration S122 { get; } = Gml(
        "S-122",
        (s, path) => new S122DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S122DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager));

    /// <summary>S-124 Navigational Warnings.</summary>
    public static S100ProductRegistration S124 { get; } = Gml(
        "S-124",
        (s, path) => new S124DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S124DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager));

    /// <summary>S-125 Marine Aids to Navigation.</summary>
    public static S100ProductRegistration S125 { get; } = Gml(
        "S-125",
        (s, path) => new S125DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S125DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager));

    /// <summary>S-127 Marine Traffic Management.</summary>
    public static S100ProductRegistration S127 { get; } = Gml(
        "S-127",
        (s, path) => new S127DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S127DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager));

    /// <summary>S-128 Catalogue of Nautical Products.</summary>
    public static S100ProductRegistration S128 { get; } = Gml(
        "S-128",
        (s, path) => new S128DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S128DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager));

    /// <summary>S-129 Under Keel Clearance Management.</summary>
    public static S100ProductRegistration S129 { get; } = Gml(
        "S-129",
        (s, path) => new S129DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S129DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager));

    /// <summary>S-131 Marine Harbour Infrastructure.</summary>
    public static S100ProductRegistration S131 { get; } = new()
    {
        Spec = "S-131",
        CreateFromPath = (s, path) => new S131DatasetProcessor(
            path, s.CatalogueManager, s.LuaEngine, s.FeatureCatalogueManager),
        CreateFromSource = (s, r) => new S131DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.LuaEngine,
            s.FeatureCatalogueManager),
    };

    /// <summary>S-201 Aids to Navigation Information.</summary>
    public static S100ProductRegistration S201 { get; } = Gml(
        "S-201",
        (s, path) => new S201DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S201DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager));

    /// <summary>S-411 Ice Information (JCOMM).</summary>
    public static S100ProductRegistration S411 { get; } = Gml(
        "S-411",
        (s, path) => new S411DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S411DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager));

    /// <summary>S-421 Route Plan.</summary>
    public static S100ProductRegistration S421 { get; } = Gml(
        "S-421",
        (s, path) => new S421DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S421DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager));

    private static S100ProductRegistration Gml(
        string spec,
        DatasetProcessorFromPath fromPath,
        DatasetProcessorFromSource fromSource) =>
        new() { Spec = spec, CreateFromPath = fromPath, CreateFromSource = fromSource };
}
