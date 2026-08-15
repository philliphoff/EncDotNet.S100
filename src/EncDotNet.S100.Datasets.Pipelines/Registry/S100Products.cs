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
        foreach (var registration in All)
            registry.Register(registration);
    }

    /// <summary>S-101 Electronic Navigational Chart.</summary>
    public static S100ProductRegistration S101 { get; } = new()
    {
        Spec = "S-101",
        CreateFromPath = (s, path) => new S101DatasetProcessor(
            path, s.CatalogueManager, s.LuaEngine, s.FeatureCatalogueManager,
            s.SharedInstructionCache),
        CreateFromSource = (s, r) => new S101DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.LuaEngine,
            s.FeatureCatalogueManager, s.SharedInstructionCache, r.SupportFiles),
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
        // S-57 and S-101 share the ISO 8211 .000 extension; S-57 datasets carry a
        // DSPM field in their DDR that S-101 datasets do not. Contributing this
        // rule on the registration lets the registry-aware DetectProductSpec
        // overload honour the registry's product set — it runs the sniff only
        // when S-57 is registered.
        Discriminate = static path => EncDotNet.S100.Datasets.S57.S57Dataset.IsS57File(path),
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
            s.FeatureCatalogueManager),
        // The 2.0.0 sample dataset is mis-labelled with the S-123 namespace but
        // its <productIdentifier> is "INT.IHO.S-122.x.y.z".
        static d => Ns(d, "S-122", "S122") || Local(d, "S122")
            || d.ContainsProductIdentifier("S-122")
            || DataSetAttr(d, "S122", "S-122"));

    /// <summary>S-124 Navigational Warnings.</summary>
    public static S100ProductRegistration S124 { get; } = Gml(
        "S-124",
        (s, path) => new S124DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S124DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager),
        static d => Ns(d, "S-124") || Local(d, "S124")
            || DataSetAttr(d, "S124", "S-124"));

    /// <summary>S-125 Marine Aids to Navigation.</summary>
    public static S100ProductRegistration S125 { get; } = Gml(
        "S-125",
        (s, path) => new S125DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S125DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager),
        static d => Ns(d, "S-125", "S125") || Local(d, "S125")
            || DataSetAttr(d, "S125", "S-125"));

    /// <summary>S-127 Marine Traffic Management.</summary>
    public static S100ProductRegistration S127 { get; } = Gml(
        "S-127",
        (s, path) => new S127DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S127DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager),
        static d => Ns(d, "S-127", "S127") || Local(d, "S127")
            || DataSetAttr(d, "S127", "S-127"));

    /// <summary>S-128 Catalogue of Nautical Products.</summary>
    public static S100ProductRegistration S128 { get; } = Gml(
        "S-128",
        (s, path) => new S128DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S128DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager),
        static d => Ns(d, "S-128", "S128") || Local(d, "S128")
            || d.ContainsProductIdentifier("S-128")
            || DataSetAttr(d, "S128", "S-128"));

    /// <summary>S-129 Under Keel Clearance Management.</summary>
    public static S100ProductRegistration S129 { get; } = Gml(
        "S-129",
        (s, path) => new S129DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S129DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager),
        static d => Ns(d, "S-129", "S129") || Local(d, "S129")
            || DataSetAttr(d, "S129", "S-129"));

    /// <summary>S-131 Marine Harbour Infrastructure.</summary>
    public static S100ProductRegistration S131 { get; } = new()
    {
        Spec = "S-131",
        CreateFromPath = (s, path) => new S131DatasetProcessor(
            path, s.CatalogueManager, s.LuaEngine, s.FeatureCatalogueManager),
        CreateFromSource = (s, r) => new S131DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.LuaEngine,
            s.FeatureCatalogueManager),
        MatchGml = static d => Ns(d, "S-131", "S131") || Local(d, "S131")
            || DataSetAttr(d, "S131", "S-131"),
    };

    /// <summary>S-201 Aids to Navigation Information.</summary>
    public static S100ProductRegistration S201 { get; } = Gml(
        "S-201",
        (s, path) => new S201DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S201DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager),
        // Real-world S-201 uses one of three application-schema namespaces,
        // including the legacy "http://www.iho.int/201/gml/1.0".
        static d => Ns(d, "S-201", "S201", "/201/gml") || Local(d, "S201")
            || DataSetAttr(d, "S201", "S-201", "/201/gml"));

    /// <summary>S-411 Ice Information (JCOMM).</summary>
    public static S100ProductRegistration S411 { get; } = Gml(
        "S-411",
        (s, path) => new S411DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S411DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager),
        // JCOMM operational shape roots at <ice:IceDataSet
        // xmlns:ice="http://www.jcomm.info/ice">; the IHO 1.2.1 sample shape uses
        // a bare <Dataset> whose spec is declared via <productIdentifier>.
        static d => d.LocalName.Equals("IceDataSet", StringComparison.OrdinalIgnoreCase)
            || d.NamespaceUri.Equals("http://www.jcomm.info/ice", StringComparison.OrdinalIgnoreCase)
            || Ns(d, "S-411", "S411") || Local(d, "S411")
            || d.ContainsProductIdentifier("S-411")
            || DataSetAttr(d, "S411", "S-411"));

    /// <summary>S-421 Route Plan.</summary>
    public static S100ProductRegistration S421 { get; } = Gml(
        "S-421",
        (s, path) => new S421DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S421DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager),
        static d => Ns(d, "S-421", "S421") || Local(d, "S421")
            || DataSetAttr(d, "S421", "S-421"));

    /// <summary>
    /// Every built-in product registration, in a single place. Both
    /// <see cref="RegisterAll"/> and <see cref="KnownSpecs"/> derive from this
    /// list, so a new product is added here once.
    /// </summary>
    public static IReadOnlyList<S100ProductRegistration> All { get; } = Array.AsReadOnly(
        new[]
        {
            S101, S57, S102, S104, S111, S122, S124, S125, S127, S128,
            S129, S131, S201, S411, S421,
        });

    /// <summary>
    /// The canonical spec strings of every built-in product. Backs
    /// <see cref="DatasetPipelineFactory.MapProductIdentifierToSpec"/>'s
    /// known-product allow-list so the set of recognized identifiers is derived
    /// from the registrations rather than hard-coded a second time.
    /// </summary>
    internal static IReadOnlySet<string> KnownSpecs { get; } =
        All.Select(r => r.Spec).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static S100ProductRegistration Gml(
        string spec,
        DatasetProcessorFromPath fromPath,
        DatasetProcessorFromSource fromSource,
        DatasetGmlMatcher matchGml) =>
        new()
        {
            Spec = spec,
            CreateFromPath = fromPath,
            CreateFromSource = fromSource,
            MatchGml = matchGml,
        };

    /// <summary>Whether the root element's namespace URI contains any of <paramref name="tokens"/>.</summary>
    private static bool Ns(GmlRootInfo d, params string[] tokens) =>
        Array.Exists(tokens, t => d.NamespaceUri.Contains(t, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the root element's local name contains <paramref name="token"/>.</summary>
    private static bool Local(GmlRootInfo d, string token) =>
        d.LocalName.Contains(token, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the root is a generic <c>&lt;DataSet&gt;</c> whose declared
    /// namespaces (attribute values) contain any of <paramref name="tokens"/>.
    /// </summary>
    private static bool DataSetAttr(GmlRootInfo d, params string[] tokens) =>
        d.IsDataSetRoot
        && d.AttributeValues.Any(v =>
            Array.Exists(tokens, t => v.Contains(t, StringComparison.OrdinalIgnoreCase)));
}
