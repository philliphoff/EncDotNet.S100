using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S125;
using EncDotNet.S100.Datasets.S127;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Datasets.S131;
using EncDotNet.S100.Datasets.S201;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The built-in per-product <see cref="ProductRegistration"/>s for every
/// S-100 product this library ships a processor for, and helpers to register
/// them. This is the one place that knows how each product's processor is
/// constructed, how its files are recognised, and its traits (metadata reader,
/// catalog payload, default S-98 planes, portrayal basis); it replaces the
/// former hard-coded <c>switch</c>es in <see cref="DatasetPipelineFactory"/>
/// and the hosts. A host that wants every product calls
/// <see cref="ProductRegistryExtensions.AddAllS100Products"/> (the default
/// <see cref="ProductRegistry.Default"/> does this, and adds the weather formats
/// of <see cref="WeatherProducts"/>); a host that wants a subset registers only
/// the products it needs.
/// </summary>
public static class S100Products
{
    /// <summary>
    /// Creates a registry pre-loaded with every built-in product: the S-100
    /// products here and the weather formats in <see cref="WeatherProducts"/>.
    /// </summary>
    public static ProductRegistry CreateDefaultRegistry() =>
        new ProductRegistry().AddAllS100Products().AddWeatherProducts();

    /// <summary>Registers every built-in product with <paramref name="registry"/>.</summary>
    public static void RegisterAll(ProductRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        foreach (var registration in All)
            registry.Register(registration);
    }

    // S-98 Annex A §A-6.9.1: the base chart is split between fills and line
    // work so S-102 lands between them. S-401 is the inland base chart and
    // occupies the same planes; S-57 is portrayed as S-101.
    private static readonly IReadOnlyDictionary<string, S98DisplayPlane> BaseChartPlanes = Planes(
        ("area", S98DisplayPlane.BaseChartUnder),
        (ProductRegistration.AnyLayerKind, S98DisplayPlane.BaseChartOver));

    // Out-of-S-98-scope products: MSC.530(106)/Rev.1 §Appendix 2 default plane
    // assignment.
    private static readonly IReadOnlyDictionary<string, S98DisplayPlane> OverlayPlanes = Planes(
        (ProductRegistration.AnyLayerKind, S98DisplayPlane.OtherChartOverlays));

    /// <summary>S-101 Electronic Navigational Chart.</summary>
    public static ProductRegistration S101 { get; } = new()
    {
        Spec = "S-101",
        CreateFromPath = (s, path) => new S101DatasetProcessor(
            path, s.CatalogueManager, s.LuaEngine, s.FeatureCatalogueManager,
            s.SharedInstructionCache),
        CreateFromSource = (s, r) => new S101DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.LuaEngine,
            s.FeatureCatalogueManager, s.SharedInstructionCache, r.SupportFiles),
        // S-101 declares itself in the ISO 8211 DSID/PRSP subfield
        // (INT.IHO.S-101.…), which is what tells it apart from S-401 — the other
        // S-100 product sharing the .000 extension.
        MatchIso8211 = static root => root.DeclaresProduct("S-101"),
        ReadMetadata = S101Dataset.ReadMetadata,
        OpenData = OpenS101,
        DefaultPlanes = BaseChartPlanes,
    };

    /// <summary>
    /// S-401 Inland ENC (IEHG). Shares the S-100 Part 10a ISO 8211 encoding and
    /// the Part 9A Lua portrayal model with S-101, so it reuses
    /// <see cref="S101DatasetProcessor"/> with its own Feature and Portrayal
    /// Catalogues rather than cloning the pipeline.
    /// </summary>
    public static ProductRegistration S401 { get; } = new()
    {
        Spec = "S-401",
        CreateFromPath = (s, path) => new S101DatasetProcessor(
            path, s.CatalogueManager, s.LuaEngine, s.FeatureCatalogueManager,
            s.SharedInstructionCache, "S-401"),
        CreateFromSource = (s, r) => new S101DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.LuaEngine,
            s.FeatureCatalogueManager, s.SharedInstructionCache, r.SupportFiles,
            "S-401"),
        MatchIso8211 = static root => root.DeclaresProduct("S-401"),
        // Read by the same Part 10a reader and carries the same payload; the
        // catalog keeps its declared identity via the dataset's DSID product
        // specification.
        OpenData = OpenS101,
        DefaultPlanes = BaseChartPlanes,
    };

    /// <summary>Legacy S-57 ENC (translated in-memory to S-101).</summary>
    public static ProductRegistration S57 { get; } = new()
    {
        Spec = "S-57",
        CreateFromPath = (s, path) => new S57DatasetProcessor(
            path, s.CatalogueManager, s.LuaEngine, s.FeatureCatalogueManager,
            s.SharedInstructionCache),
        CreateFromSource = (s, r) => new S57DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.LuaEngine,
            s.FeatureCatalogueManager, s.SharedInstructionCache),
        // S-57 shares the ISO 8211 .000 extension with S-101 and S-401 but
        // declares no S-100 PRSP product identifier (its PRSP is a numeric S-57
        // code); it is instead identified by the S-57-only DSPM field in its DDR.
        // That covers inland ENCs too (PRSP 10): they are S-57 cells, so they
        // keep the S-57 identity. Contributing this rule on the
        // registration lets the registry-aware DetectProductSpec overload honour
        // the registry's product set — a registry without S-57 never claims one.
        MatchIso8211 = static root => root.HasDataSetParameterField,
        // An S-57 cell keeps product identity "S-57" but is portrayed through the
        // S-101 catalogue and conventions (S57DatasetProcessor.PortrayalSpec).
        PortrayalSpec = "S-101",
        ReadMetadata = S57Dataset.ReadMetadata,
        OpenData = OpenS57,
        DefaultPlanes = BaseChartPlanes,
    };

    /// <summary>S-102 Bathymetric Surface.</summary>
    public static ProductRegistration S102 { get; } = new()
    {
        Spec = "S-102",
        CreateFromPath = (s, path) => new S102DatasetProcessor(
            path, s.CatalogueManager, s.LuaEngine, s.CrsTransformFactory),
        CreateFromSource = (s, r) => new S102DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.LuaEngine,
            s.CrsTransformFactory),
        // S-100 Part 10c: the root productSpecification attribute names the
        // product. An HDF5 file that declares none is no longer assumed to be
        // S-102.
        MatchHdf5 = static root => root.DeclaresProduct("S-102"),
        ReadMetadata = static path => ReadHdf5(path, S102DatasetReader.ReadMetadata),
        OpenData = static (stream, _) => OpenS102(stream),
        // S-98 Annex A §A-6.9.1.
        DefaultPlanes = Planes((ProductRegistration.AnyLayerKind, S98DisplayPlane.Bathymetry)),
    };

    /// <summary>
    /// The S-104 portrayal: S-104 Edition 2.0.0 §9 defines none, so the station
    /// gauges and surface isolines are SoundCharts' own (see
    /// <c>S104CoverageStyle</c>).
    /// </summary>
    public static PortrayalBasis S104Portrayal { get; } = PortrayalBasis.Application("S-104 Ed 2.0.0");

    /// <summary>S-104 Water Level Information for Surface Navigation.</summary>
    public static ProductRegistration S104 { get; } = new()
    {
        Spec = "S-104",
        CreateFromPath = (s, path) => new S104DatasetProcessor(
            path, s.CrsTransformFactory),
        CreateFromSource = (s, r) => new S104DatasetProcessor(
            r.Source, r.RelativePath, s.CrsTransformFactory),
        MatchHdf5 = static root => root.DeclaresProduct("S-104"),
        Portrayal = S104Portrayal,
        ReadMetadata = static path => ReadHdf5(path, S104DatasetReader.ReadMetadata),
        OpenData = static (stream, _) => OpenS104(stream),
        // Surface isolines on the on-demand surface plane; station glyphs are
        // point overlays.
        DefaultPlanes = Planes(
            ("s104.stations", S98DisplayPlane.OtherChartOverlays),
            (ProductRegistration.AnyLayerKind, S98DisplayPlane.OnDemandSurface)),
    };

    /// <summary>S-111 Surface Currents.</summary>
    public static ProductRegistration S111 { get; } = new()
    {
        Spec = "S-111",
        CreateFromPath = (s, path) => new S111DatasetProcessor(
            path, s.CatalogueManager, s.CrsTransformFactory),
        CreateFromSource = (s, r) => new S111DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.CrsTransformFactory),
        MatchHdf5 = static root => root.DeclaresProduct("S-111"),
        ReadMetadata = static path => ReadHdf5(path, S111DatasetReader.ReadMetadata),
        OpenData = static (stream, _) => OpenS111(stream),
        // S-111 Edition 2.0.0. The portrayal catalogue gives one SurfaceCurrent
        // instruction for every data coding format, and S-111 §9.2.9 / §9.3.3
        // portray station series and ungeorectified meshes with the same arrows
        // as regular grids, so grid ("s111.arrows") and station/mesh
        // ("s111.stations") arrows share one plane. S-98 assigns no specific
        // plane to current arrows (Main §9.2.1 puts surface currents in layer 6,
        // "official on demand data"); DynamicArrows is this library's choice —
        // see S98DisplayPlane.DynamicArrows.
        DefaultPlanes = Planes((ProductRegistration.AnyLayerKind, S98DisplayPlane.DynamicArrows)),
    };

    /// <summary>S-122 Marine Protected Areas.</summary>
    public static ProductRegistration S122 { get; } = Gml(
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
            || DataSetAttr(d, "S122", "S-122"),
        static (stream, _) => new S122DatasetData(S122Dataset.Open(stream)),
        S122Dataset.ReadMetadata);

    /// <summary>S-124 Navigational Warnings.</summary>
    public static ProductRegistration S124 { get; } = Gml(
        "S-124",
        (s, path) => new S124DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S124DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager),
        static d => Ns(d, "S-124") || Local(d, "S124")
            || DataSetAttr(d, "S124", "S-124"),
        static (stream, _) => new S124DatasetData(S124Dataset.Open(stream)),
        S124Dataset.ReadMetadata,
        // MSC.530(106)/Rev.1 §Appendix 2 layers 3-4; S-98 Main §9.2.1.
        Planes((ProductRegistration.AnyLayerKind, S98DisplayPlane.CautionsAndWarnings)));

    /// <summary>S-125 Marine Aids to Navigation.</summary>
    public static ProductRegistration S125 { get; } = Gml(
        "S-125",
        (s, path) => new S125DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager, s.LuaEngine),
        (s, r) => new S125DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager, s.LuaEngine),
        static d => Ns(d, "S-125", "S125") || Local(d, "S125")
            || DataSetAttr(d, "S125", "S-125"),
        static (stream, _) => new S125DatasetData(S125Dataset.Open(stream)),
        S125Dataset.ReadMetadata);

    /// <summary>S-127 Marine Traffic Management.</summary>
    public static ProductRegistration S127 { get; } = Gml(
        "S-127",
        (s, path) => new S127DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S127DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager),
        static d => Ns(d, "S-127", "S127") || Local(d, "S127")
            || DataSetAttr(d, "S127", "S-127"),
        static (stream, _) => new S127DatasetData(S127Dataset.Open(stream)),
        S127Dataset.ReadMetadata);

    /// <summary>S-128 Catalogue of Nautical Products.</summary>
    public static ProductRegistration S128 { get; } = Gml(
        "S-128",
        (s, path) => new S128DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S128DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager),
        static d => Ns(d, "S-128", "S128") || Local(d, "S128")
            || d.ContainsProductIdentifier("S-128")
            || DataSetAttr(d, "S128", "S-128"),
        static (stream, _) => new S128DatasetData(S128Dataset.Open(stream)),
        readMetadata: null);

    /// <summary>S-129 Under Keel Clearance Management.</summary>
    public static ProductRegistration S129 { get; } = Gml(
        "S-129",
        (s, path) => new S129DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S129DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager),
        static d => Ns(d, "S-129", "S129") || Local(d, "S129")
            || DataSetAttr(d, "S129", "S-129"),
        static (stream, _) => new S129DatasetData(S129Dataset.Open(stream)),
        S129Dataset.ReadMetadata,
        // PR-L1 placement.
        Planes((ProductRegistration.AnyLayerKind, S98DisplayPlane.OnDemandSurface)));

    /// <summary>S-131 Marine Harbour Infrastructure.</summary>
    public static ProductRegistration S131 { get; } = new()
    {
        Spec = "S-131",
        CreateFromPath = (s, path) => new S131DatasetProcessor(
            path, s.CatalogueManager, s.LuaEngine, s.FeatureCatalogueManager),
        CreateFromSource = (s, r) => new S131DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.LuaEngine,
            s.FeatureCatalogueManager),
        MatchGml = static d => Ns(d, "S-131", "S131") || Local(d, "S131")
            || DataSetAttr(d, "S131", "S-131"),
        OpenData = static (stream, _) => new S131DatasetData(S131Dataset.Open(stream)),
        DefaultPlanes = OverlayPlanes,
    };

    /// <summary>S-201 Aids to Navigation Information.</summary>
    public static ProductRegistration S201 { get; } = Gml(
        "S-201",
        (s, path) => new S201DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S201DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager),
        // Real-world S-201 uses one of three application-schema namespaces,
        // including the legacy "http://www.iho.int/201/gml/1.0".
        static d => Ns(d, "S-201", "S201", "/201/gml") || Local(d, "S201")
            || DataSetAttr(d, "S201", "S-201", "/201/gml"),
        static (stream, _) => new S201DatasetData(S201Dataset.Open(stream)),
        S201Dataset.ReadMetadata);

    /// <summary>S-411 Ice Information (JCOMM).</summary>
    public static ProductRegistration S411 { get; } = Gml(
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
            || DataSetAttr(d, "S411", "S-411"),
        static (stream, _) => new S411DatasetData(S411Dataset.Open(stream)),
        S411Dataset.ReadMetadata);

    /// <summary>S-421 Route Plan.</summary>
    public static ProductRegistration S421 { get; } = Gml(
        "S-421",
        (s, path) => new S421DatasetProcessor(
            path, s.CatalogueManager, s.AuthorityProvider, s.FeatureCatalogueManager),
        (s, r) => new S421DatasetProcessor(
            r.Source, r.RelativePath, s.CatalogueManager, s.AuthorityProvider,
            s.FeatureCatalogueManager),
        static d => Ns(d, "S-421", "S421") || Local(d, "S421")
            || DataSetAttr(d, "S421", "S-421"),
        static (stream, _) => new S421DatasetData(S421Dataset.Open(stream)),
        S421Dataset.ReadMetadata);

    /// <summary>
    /// Every built-in product registration, in a single place. Both
    /// <see cref="RegisterAll"/> and <see cref="KnownSpecs"/> derive from this
    /// list, so a new product is added here once.
    /// </summary>
    public static IReadOnlyList<ProductRegistration> All { get; } = Array.AsReadOnly(
        new[]
        {
            S101, S57, S102, S104, S111, S122, S124, S125, S127, S128,
            S129, S131, S201, S401, S411, S421,
        });

    /// <summary>
    /// The canonical spec strings of every built-in product. Backs
    /// <see cref="DatasetPipelineFactory.MapProductIdentifierToSpec"/>'s
    /// known-product allow-list so the set of recognized identifiers is derived
    /// from the registrations rather than hard-coded a second time.
    /// </summary>
    internal static IReadOnlySet<string> KnownSpecs { get; } =
        All.Select(r => r.Spec).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static ProductRegistration Gml(
        string spec,
        DatasetProcessorFromPath fromPath,
        DatasetProcessorFromSource fromSource,
        DatasetGmlMatcher matchGml,
        DatasetDataOpener openData,
        Func<string, DatasetMetadata>? readMetadata,
        IReadOnlyDictionary<string, S98DisplayPlane>? defaultPlanes = null) =>
        new()
        {
            Spec = spec,
            CreateFromPath = fromPath,
            CreateFromSource = fromSource,
            MatchGml = matchGml,
            OpenData = openData,
            ReadMetadata = readMetadata,
            DefaultPlanes = defaultPlanes ?? OverlayPlanes,
        };

    private static IReadOnlyDictionary<string, S98DisplayPlane> Planes(
        params (string Kind, S98DisplayPlane Plane)[] entries)
    {
        var planes = new Dictionary<string, S98DisplayPlane>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, plane) in entries)
            planes[kind] = plane;
        return planes;
    }

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

    private static DatasetMetadata ReadHdf5(string path, Func<IHdf5File, DatasetMetadata> read)
    {
        using var file = PureHdfFile.Open(path);
        return read(file);
    }

    private static LoadedDatasetData OpenS101(Stream stream, Func<string, string?>? externalTextResolver) =>
        new S101DatasetData(S101Dataset.Open(stream), externalTextResolver);

    private static LoadedDatasetData OpenS57(Stream stream, Func<string, string?>? externalTextResolver)
    {
        // Legacy S-57 is a different ISO 8211 profile: read it with the S-57
        // reader and translate into the product the cell declares (S-101 for a
        // maritime ENC, S-401 for an inland ENC, issue #608), exactly as
        // S57DatasetProcessor does. Never open S-57 bytes with S101Dataset. The
        // projector has no portrayal-catalogue context, so unlike the processor
        // it does not fall back to S-101 when S-401 is unavailable.
        var s57 = S57Dataset.Open(stream);
        var translator = S57ToS101Translator.ForTarget(s57.TranslationTarget);
        // The translation drops the compilation scale, which ranks the cell
        // against overlapping cells, so the payload carries it.
        return new S101DatasetData(
            S101Dataset.FromDocument(translator.Translate(s57)),
            externalTextResolver,
            S57Dataset.ResolveCompilationScale(s57.Document));
    }

    private static LoadedDatasetData OpenS102(Stream stream)
    {
        using var file = PureHdfFile.Open(stream);
        var dataset = S102DatasetReader.Read(file);
        return new S102CoverageData(new S102CoverageSource(dataset));
    }

    private static LoadedDatasetData OpenS104(Stream stream)
    {
        using var file = PureHdfFile.Open(stream);
        var data = S104DatasetReader.ReadAny(file);
        return data switch
        {
            S104DatasetData.GriddedCoverage g => new S104CoverageData(new S104CoverageSource(g.Dataset)),
            S104DatasetData.StationSeries s => new S104StationSeriesData(s.Dataset),
            _ => throw new InvalidOperationException(
                $"Unexpected S-104 dataset variant {data.GetType().Name}."),
        };
    }

    private static LoadedDatasetData OpenS111(Stream stream)
    {
        using var file = PureHdfFile.Open(stream);
        var data = S111DatasetReader.ReadAny(file);
        return data switch
        {
            S111DatasetData.GriddedCoverage g => new S111CoverageData(new S111CoverageSource(g.Dataset)),
            S111DatasetData.StationSeries s => new S111StationSeriesData(s.Dataset),
            _ => throw new InvalidOperationException(
                $"Unexpected S-111 dataset variant {data.GetType().Name}."),
        };
    }
}
