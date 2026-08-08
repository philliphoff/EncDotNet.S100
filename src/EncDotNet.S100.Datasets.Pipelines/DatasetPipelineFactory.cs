using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Features;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector.Caching;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Detects dataset type from file extension and creates
/// the appropriate <see cref="IDatasetProcessor"/>.
/// </summary>
public sealed class DatasetPipelineFactory : IDatasetProcessorFactory
{
    private readonly S100ProductRegistry _registry;
    private readonly DatasetProcessorServices _services;

    /// <summary>
    /// Creates a new factory. The supplied
    /// <paramref name="featureCatalogueManager"/> is shared across every
    /// processor this factory produces, so its FC parse cache survives
    /// for the lifetime of the manager — not just the lifetime of the
    /// factory. The supplied <paramref name="authorityProvider"/> is
    /// forwarded to every GML-based processor so they resolve the
    /// default S-98 display plane through the host's DI container rather
    /// than a static singleton.
    /// </summary>
    /// <param name="catalogueManager">Portrayal catalogue manager shared by every processor.</param>
    /// <param name="luaEngine">Lua engine for Part 9A portrayal.</param>
    /// <param name="crsTransformFactory">CRS transform factory for coverage products.</param>
    /// <param name="featureCatalogueManager">Feature catalogue manager (shared FC parse cache).</param>
    /// <param name="authorityProvider">Default S-98 display-plane authority provider.</param>
    /// <param name="sharedInstructionCache">
    /// Optional process-wide portrayal-instruction cache (e.g. a
    /// <see cref="EncDotNet.S100.Pipelines.Vector.Caching.DiskPortrayalInstructionCache"/>)
    /// shared by every S-101 processor this factory produces, so a fresh open
    /// of a previously-portrayed cell skips the multi-second MoonSharp Part 9A
    /// Lua run. When <see langword="null"/> each S-101 processor falls back to a
    /// bounded per-processor in-memory cache — the behaviour used by tools and
    /// tests.
    /// </param>
    /// <param name="sharedLineLodCache">
    /// Optional process-wide line-LOD cache (e.g. an
    /// <see cref="EncDotNet.S100.Pipelines.Vector.Caching.InMemoryLineLodCache"/>
    /// or <see cref="EncDotNet.S100.Pipelines.Vector.Caching.DiskLineLodCache"/>)
    /// shared by every S-101 processor this factory produces. When present, the
    /// processor pre-builds the Douglas–Peucker LOD pyramid for every line
    /// feature at open (issue #489, PR-3) so first-paint skips the per-frame
    /// simplification pass. When <see langword="null"/> the renderer's
    /// fast-line path falls back to today's inline simplification.
    /// </param>
    /// <param name="productRegistry">
    /// Optional set of products this factory can construct. When
    /// <see langword="null"/> a default registry with every built-in product is
    /// used (<see cref="S100Products.CreateDefaultRegistry"/>), preserving the
    /// historical behaviour. Supply a custom registry to enable only a subset of
    /// products (e.g. an S-101-only host) or to add a product of your own.
    /// </param>
    public DatasetPipelineFactory(
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        ICrsTransformFactory crsTransformFactory,
        FeatureCatalogueManager featureCatalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        IPortrayalInstructionCache? sharedInstructionCache = null,
        ILineLodCache? sharedLineLodCache = null,
        S100ProductRegistry? productRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(catalogueManager);
        ArgumentNullException.ThrowIfNull(luaEngine);
        ArgumentNullException.ThrowIfNull(crsTransformFactory);
        ArgumentNullException.ThrowIfNull(featureCatalogueManager);
        ArgumentNullException.ThrowIfNull(authorityProvider);

        _services = new DatasetProcessorServices
        {
            CatalogueManager = catalogueManager,
            LuaEngine = luaEngine,
            CrsTransformFactory = crsTransformFactory,
            FeatureCatalogueManager = featureCatalogueManager,
            AuthorityProvider = authorityProvider,
            SharedInstructionCache = sharedInstructionCache,
            SharedLineLodCache = sharedLineLodCache,
        };
        _registry = productRegistry ?? S100Products.CreateDefaultRegistry();
    }

    /// <summary>
    /// Returns the product spec identifier for the given file, or null if unrecognized.
    /// </summary>
    public static string? DetectProductSpec(string path)
    {
        var ext = Path.GetExtension(path);

        // HDF5 files: inspect productSpecification attribute to distinguish S-102 vs S-111
        if (ext.Equals(".h5", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".H5", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".hdf5", StringComparison.OrdinalIgnoreCase))
        {
            return DetectHdf5ProductSpec(path);
        }

        // S-101: ISO 8211 files (also S-57 — distinguished by content below).
        if (ext.Equals(".000", StringComparison.OrdinalIgnoreCase))
        {
            // S-57 datasets carry a DSPM field in their ISO 8211 DDR which is
            // not present in S-101 datasets; use that as the discriminator.
            try
            {
                if (EncDotNet.S100.Datasets.S57.S57Dataset.IsS57File(path))
                    return "S-57";
            }
            catch
            {
                // Fall through and treat as S-101.
            }
            return "S-101";
        }

        // S-124: GML encoded files
        if (ext.Equals(".gml", StringComparison.OrdinalIgnoreCase))
        {
            return DetectGmlProductSpec(path);
        }

        return null;
    }

    /// <summary>
    /// Registry-aware form of <see cref="DetectProductSpec(string)"/>: resolves
    /// the ambiguous ISO 8211 <c>.000</c> extension (shared by S-57 and S-101)
    /// using the S-57 content discriminator that <paramref name="registry"/>
    /// actually offers. A registry without S-57 registered treats every
    /// <c>.000</c> file as S-101, so detection never yields a spec the registry
    /// cannot build. All other extensions (HDF5, GML) are product-agnostic and
    /// detected exactly as the parameterless overload does.
    /// </summary>
    internal static string? DetectProductSpec(string path, S100ProductRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var ext = Path.GetExtension(path);
        if (ext.Equals(".000", StringComparison.OrdinalIgnoreCase))
        {
            // S-57 datasets carry a DSPM field in their ISO 8211 DDR which is
            // not present in S-101 datasets; the S-57 registration contributes
            // that content sniff. Only run it when the registry can actually
            // build S-57 — otherwise the file is treated as S-101.
            if (registry.TryResolve("S-57", out var s57) && s57.Discriminate is { } discriminate)
            {
                try
                {
                    if (discriminate(path))
                        return "S-57";
                }
                catch
                {
                    // Fall through and treat as S-101.
                }
            }
            return "S-101";
        }

        return DetectProductSpec(path);
    }

    private static string DetectHdf5ProductSpec(string path)
    {
        try
        {
            using var hdf5 = PureHdfFile.Open(path);
            var root = hdf5.Root;

            if (root.AttributeExists("productSpecification"))
            {
                var spec = root.ReadStringAttribute("productSpecification");
                if (spec.Contains("S-104", StringComparison.OrdinalIgnoreCase))
                    return "S-104";
                if (spec.Contains("S-111", StringComparison.OrdinalIgnoreCase))
                    return "S-111";
            }
        }
        catch
        {
            // Fall through to default
        }

        return "S-102";
    }

    private static string? DetectGmlProductSpec(string path)
    {
        try
        {
            // Some GML files have leading whitespace before the XML declaration;
            // read as text, trim, and parse via a StringReader to tolerate this.
            var xml = File.ReadAllText(path).TrimStart();
            return DetectGmlProductSpecFromXml(xml);
        }
        catch
        {
            // Unable to parse – unknown
        }

        return null;
    }

    /// <summary>
    /// Inspects the root element / declared namespaces / <c>productIdentifier</c>
    /// of an already-loaded GML document body to determine its S-100 product
    /// specification (e.g. <c>"S-411"</c>). Shared by the file-path and
    /// asset-source detection paths so exchange-set datasets whose catalogue
    /// omits a machine-readable <c>productIdentifier</c> (common for JCOMM S-411
    /// sets) are still routed correctly. Returns <c>null</c> when unrecognized.
    /// </summary>
    private static string? DetectGmlProductSpecFromXml(string xml)
    {
        try
        {
            using var stringReader = new StringReader(xml);
            using var reader = System.Xml.XmlReader.Create(stringReader, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
            while (reader.Read())
            {
                if (reader.NodeType == System.Xml.XmlNodeType.Element)
                {
                    // S-124 datasets have a root element in the S-124 namespace
                    if (reader.NamespaceURI.Contains("S-124", StringComparison.OrdinalIgnoreCase)
                        || reader.LocalName.Contains("S124", StringComparison.OrdinalIgnoreCase))
                    {
                        return "S-124";
                    }

                    // S-125 datasets use namespace http://www.iho.int/S125/1.0
                    if (reader.NamespaceURI.Contains("S-125", StringComparison.OrdinalIgnoreCase)
                        || reader.NamespaceURI.Contains("S125", StringComparison.OrdinalIgnoreCase)
                        || reader.LocalName.Contains("S125", StringComparison.OrdinalIgnoreCase))
                    {
                        return "S-125";
                    }

                    // S-128 — Catalogue of Nautical Products. Application
                    // namespace is "http://www.iho.int/S128/2.0".
                    if (reader.NamespaceURI.Contains("S-128", StringComparison.OrdinalIgnoreCase)
                        || reader.NamespaceURI.Contains("S128", StringComparison.OrdinalIgnoreCase)
                        || reader.LocalName.Contains("S128", StringComparison.OrdinalIgnoreCase)
                        || ContainsProductIdentifier(xml, "S-128"))
                    {
                        return "S-128";
                    }

                    // S-127 datasets declare the namespace "http://www.iho.int/S127/2.0".
                    if (reader.NamespaceURI.Contains("S-127", StringComparison.OrdinalIgnoreCase)
                        || reader.NamespaceURI.Contains("S127", StringComparison.OrdinalIgnoreCase)
                        || reader.LocalName.Contains("S127", StringComparison.OrdinalIgnoreCase))
                    {
                        return "S-127";
                    }

                    // S-129 datasets have a root element in the S-129 namespace
                    if (reader.NamespaceURI.Contains("S-129", StringComparison.OrdinalIgnoreCase)
                        || reader.NamespaceURI.Contains("S129", StringComparison.OrdinalIgnoreCase)
                        || reader.LocalName.Contains("S129", StringComparison.OrdinalIgnoreCase))
                    {
                        return "S-129";
                    }

                    // S-131 — Marine Harbour Infrastructure. Application
                    // namespace is "http://www.iho.int/S131/1.0".
                    if (reader.NamespaceURI.Contains("S-131", StringComparison.OrdinalIgnoreCase)
                        || reader.NamespaceURI.Contains("S131", StringComparison.OrdinalIgnoreCase)
                        || reader.LocalName.Contains("S131", StringComparison.OrdinalIgnoreCase))
                    {
                        return "S-131";
                    }

                    // S-201 — Aids to Navigation Information. Real-world
                    // datasets use one of three application-schema
                    // namespaces:
                    //   - "http://www.iho.int/S-201/gml/cs0/1.0" (XSD)
                    //   - "http://www.iho.int/S-201/gml/cs0/2.0" (current)
                    //   - "http://www.iho.int/201/gml/1.0"        (legacy)
                    if (reader.NamespaceURI.Contains("S-201", StringComparison.OrdinalIgnoreCase)
                        || reader.NamespaceURI.Contains("S201", StringComparison.OrdinalIgnoreCase)
                        || reader.NamespaceURI.Contains("/201/gml", StringComparison.OrdinalIgnoreCase)
                        || reader.LocalName.Contains("S201", StringComparison.OrdinalIgnoreCase))
                    {
                        return "S-201";
                    }

                    // S-421 datasets use the S421 namespace prefix and the
                    // namespace URI "http://www.iho.int/S421/gml/cs0/1.0".
                    if (reader.NamespaceURI.Contains("S-421", StringComparison.OrdinalIgnoreCase)
                        || reader.NamespaceURI.Contains("S421", StringComparison.OrdinalIgnoreCase)
                        || reader.LocalName.Contains("S421", StringComparison.OrdinalIgnoreCase))
                    {
                        return "S-421";
                    }

                    // S-411 — JCOMM operational shape: root element is
                    // <ice:IceDataSet xmlns:ice="http://www.jcomm.info/ice">.
                    if (reader.LocalName.Equals("IceDataSet", StringComparison.OrdinalIgnoreCase)
                        || reader.NamespaceURI.Equals("http://www.jcomm.info/ice", StringComparison.OrdinalIgnoreCase)
                        || reader.NamespaceURI.Contains("S-411", StringComparison.OrdinalIgnoreCase)
                        || reader.NamespaceURI.Contains("S411", StringComparison.OrdinalIgnoreCase)
                        || reader.LocalName.Contains("S411", StringComparison.OrdinalIgnoreCase))
                    {
                        return "S-411";
                    }
                    // S-411 — IHO 1.2.1 sample shape: bare <Dataset> root with
                    // no S-411 application-schema namespace; the spec is
                    // declared via <S100:productIdentifier>S-411</S100:productIdentifier>.
                    if (xml.Length > 0 && ContainsProductIdentifier(xml, "S-411"))
                    {
                        return "S-411";
                    }

                    // S-122 — Marine Protected Areas. The 2.0.0 sample dataset
                    // is mis-labelled with the S-123 namespace
                    // (xmlns:S123="http://www.iho.int/S123/gml/1.0") but its
                    // <S100:productIdentifier> is "INT.IHO.S-122.x.y.z", so we
                    // fall back to sniffing the productIdentifier element.
                    if (reader.NamespaceURI.Contains("S-122", StringComparison.OrdinalIgnoreCase)
                        || reader.NamespaceURI.Contains("S122", StringComparison.OrdinalIgnoreCase)
                        || reader.LocalName.Contains("S122", StringComparison.OrdinalIgnoreCase)
                        || ContainsProductIdentifier(xml, "S-122"))
                    {
                        return "S-122";
                    }

                    // Generic GML DataSet fallback — inspect declared namespaces
                    if (reader.LocalName.Equals("DataSet", StringComparison.OrdinalIgnoreCase))
                    {
                        if (reader.MoveToFirstAttribute())
                        {
                            do
                            {
                                if (reader.Value.Contains("S129", StringComparison.OrdinalIgnoreCase)
                                    || reader.Value.Contains("S-129", StringComparison.OrdinalIgnoreCase))
                                {
                                    return "S-129";
                                }

                                if (reader.Value.Contains("S131", StringComparison.OrdinalIgnoreCase)
                                    || reader.Value.Contains("S-131", StringComparison.OrdinalIgnoreCase))
                                {
                                    return "S-131";
                                }

                                if (reader.Value.Contains("S124", StringComparison.OrdinalIgnoreCase)
                                    || reader.Value.Contains("S-124", StringComparison.OrdinalIgnoreCase))
                                {
                                    return "S-124";
                                }

                                if (reader.Value.Contains("S125", StringComparison.OrdinalIgnoreCase)
                                    || reader.Value.Contains("S-125", StringComparison.OrdinalIgnoreCase))
                                {
                                    return "S-125";
                                }

                                if (reader.Value.Contains("S128", StringComparison.OrdinalIgnoreCase)
                                    || reader.Value.Contains("S-128", StringComparison.OrdinalIgnoreCase))
                                {
                                    return "S-128";
                                }

                                if (reader.Value.Contains("S127", StringComparison.OrdinalIgnoreCase)
                                    || reader.Value.Contains("S-127", StringComparison.OrdinalIgnoreCase))
                                {
                                    return "S-127";
                                }

                                if (reader.Value.Contains("S421", StringComparison.OrdinalIgnoreCase)
                                    || reader.Value.Contains("S-421", StringComparison.OrdinalIgnoreCase))
                                {
                                    return "S-421";
                                }

                                if (reader.Value.Contains("S411", StringComparison.OrdinalIgnoreCase)
                                    || reader.Value.Contains("S-411", StringComparison.OrdinalIgnoreCase))
                                {
                                    return "S-411";
                                }

                                if (reader.Value.Contains("S122", StringComparison.OrdinalIgnoreCase)
                                    || reader.Value.Contains("S-122", StringComparison.OrdinalIgnoreCase))
                                {
                                    return "S-122";
                                }

                                if (reader.Value.Contains("S201", StringComparison.OrdinalIgnoreCase)
                                    || reader.Value.Contains("S-201", StringComparison.OrdinalIgnoreCase)
                                    || reader.Value.Contains("/201/gml", StringComparison.OrdinalIgnoreCase))
                                {
                                    return "S-201";
                                }
                            } while (reader.MoveToNextAttribute());
                        }

                        return null;
                    }

                    break;
                }
            }
        }
        catch
        {
            // Unable to parse – unknown
        }

        return null;
    }

    private static bool ContainsProductIdentifier(string xml, string productId)
    {
        // Sniff the first 8KB of the document for an S-100
        // <productIdentifier>{productId}</productIdentifier> element.
        // Used for product specs (e.g. S-411 1.2.1 samples) that don't declare
        // an application-schema namespace on the dataset root.
        var span = xml.AsSpan(0, Math.Min(xml.Length, 8192));
        var marker = "productIdentifier".AsSpan();
        var idx = span.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;
        var rest = span[(idx + marker.Length)..];
        return rest.IndexOf(productId.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Creates a processor for the given dataset file.
    /// The processor can be called multiple times with different contexts.
    /// </summary>
    public IDatasetProcessor CreateProcessor(string path)
    {
        var spec = DetectProductSpec(path, _registry)
            ?? throw new NotSupportedException($"Unrecognized dataset file: {Path.GetFileName(path)}");

        if (!_registry.TryResolve(spec, out var registration))
            throw new NotSupportedException($"Pipeline not implemented for {spec}.");

        return registration.CreateFromPath(_services, path);
    }

    /// <summary>
    /// Creates a processor for a dataset stored inside <paramref name="source"/>
    /// at <paramref name="relativePath"/>. Used by exchange-set bulk loading
    /// where dataset bytes may live inside a ZIP archive.
    /// </summary>
    /// <param name="source">The asset source (folder or ZIP) hosting the dataset.</param>
    /// <param name="relativePath">Path to the dataset, relative to <paramref name="source"/>.</param>
    /// <param name="declaredProductSpec">
    /// Product specification declared by the exchange-set catalogue (e.g. "S-101").
    /// When non-null and recognized, content sniffing is skipped. When null or
    /// unrecognized, falls back to extension-based sniffing on
    /// <paramref name="relativePath"/>.
    /// </param>
    /// <param name="supportFiles">
    /// Optional map of support-file name (case-insensitive) to source-relative
    /// path, from the exchange-set catalogue's
    /// <c>supportFileDiscoveryMetadata</c> (S-100 Edition 5.2.1 Part 17). Lets
    /// S-101 resolve <c>fileReference</c> external text files via the catalogue.
    /// </param>
    public IDatasetProcessor CreateProcessor(
        IAssetSource source,
        string relativePath,
        string? declaredProductSpec = null,
        IReadOnlyDictionary<string, string>? supportFiles = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        var spec = MapProductIdentifierToSpec(declaredProductSpec)
            ?? DetectProductSpecByExtension(relativePath)
            ?? DetectProductSpecFromSource(source, relativePath)
            ?? throw new NotSupportedException(
                $"Unable to determine product specification for '{relativePath}' " +
                $"(declared='{declaredProductSpec ?? "<none>"}').");

        if (!_registry.TryResolve(spec, out var registration))
            throw new NotSupportedException($"Pipeline not implemented for {spec}.");

        return registration.CreateFromSource(
            _services,
            new DatasetProcessorSourceRequest
            {
                Source = source,
                RelativePath = relativePath,
                SupportFiles = supportFiles,
            });
    }

    /// <summary>
    /// Creates an S-101 dataset processor for the base cell at
    /// <paramref name="baseRelativePath"/> with the in-set sequential update
    /// files at <paramref name="updateRelativePaths"/> applied (best-effort)
    /// before portrayal. Used by exchange-set bulk loading to collapse a cell and
    /// its updates into a single up-to-date dataset. S-101 / S-100 Part 10a.
    /// </summary>
    /// <param name="source">The asset source backing the exchange set.</param>
    /// <param name="baseRelativePath">Source-relative path of the base cell (<c>….000</c>).</param>
    /// <param name="updateRelativePaths">Source-relative paths of the update files, in ascending update-number order.</param>
    public IDatasetProcessor CreateS101ProcessorWithUpdates(
        IAssetSource source,
        string baseRelativePath,
        IReadOnlyList<string> updateRelativePaths,
        IReadOnlyDictionary<string, string>? supportFiles = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(baseRelativePath);
        ArgumentNullException.ThrowIfNull(updateRelativePaths);

        return new S101DatasetProcessor(
            source,
            baseRelativePath,
            updateRelativePaths,
            _services.CatalogueManager,
            _services.LuaEngine,
            _services.FeatureCatalogueManager,
            _services.SharedInstructionCache,
            supportFiles,
            _services.SharedLineLodCache);
    }

    /// <summary>
    /// Creates an S-101 dataset processor for the base cell file at
    /// <paramref name="baseFilePath"/> with the sibling sequential update files
    /// at <paramref name="updateFilePaths"/> applied (best-effort) before
    /// portrayal. Used by command-line callers pointed at a loose base cell on
    /// the local file system; the update files must live in the same directory
    /// as the base cell. See <see cref="S101FilesystemUpdateDiscovery"/> for
    /// locating the updates. S-101 / S-100 Part 10a.
    /// </summary>
    /// <param name="baseFilePath">Path to the base cell file (<c>….000</c>).</param>
    /// <param name="updateFilePaths">Paths of the sibling update files, in ascending update-number order.</param>
    public IDatasetProcessor CreateS101ProcessorWithUpdates(
        string baseFilePath,
        IReadOnlyList<string> updateFilePaths)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseFilePath);
        ArgumentNullException.ThrowIfNull(updateFilePaths);

        var fullBase = Path.GetFullPath(baseFilePath);
        var directory = Path.GetDirectoryName(fullBase)
            ?? throw new ArgumentException(
                "Base cell path must include a directory.", nameof(baseFilePath));
        var source = FileSystemAssetSource.Create(directory);

        var baseRelative = Path.GetFileName(fullBase);
        var updateRelatives = updateFilePaths.Select(p => Path.GetFileName(p)).ToList();

        return new S101DatasetProcessor(
            source,
            baseRelative,
            updateRelatives,
            _services.CatalogueManager,
            _services.LuaEngine,
            _services.FeatureCatalogueManager,
            _services.SharedInstructionCache,
            supportFiles: null,
            _services.SharedLineLodCache);
    }

    /// <summary>
    /// Creates an S-57 dataset processor for the base cell at
    /// <paramref name="baseRelativePath"/> with the in-set sequential update
    /// files at <paramref name="updateRelativePaths"/> applied before
    /// translation. Used by exchange-set bulk loading to collapse an S-57 cell
    /// and its updates into a single up-to-date dataset. The updates are folded
    /// into the S-57 document <em>before</em> the S-57 → S-101 translation runs.
    /// S-57 Part 3 (dataset updating).
    /// </summary>
    /// <param name="source">The asset source backing the exchange set.</param>
    /// <param name="baseRelativePath">Source-relative path of the base cell (<c>….000</c>).</param>
    /// <param name="updateRelativePaths">Source-relative paths of the update files, in ascending update-number order.</param>
    public IDatasetProcessor CreateS57ProcessorWithUpdates(
        IAssetSource source,
        string baseRelativePath,
        IReadOnlyList<string> updateRelativePaths)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(baseRelativePath);
        ArgumentNullException.ThrowIfNull(updateRelativePaths);

        return new S57DatasetProcessor(
            source,
            baseRelativePath,
            updateRelativePaths,
            _services.CatalogueManager,
            _services.LuaEngine,
            _services.FeatureCatalogueManager);
    }

    /// <summary>
    /// Creates a processor for the base cell file at
    /// <paramref name="baseFilePath"/>, discovering and applying any sibling
    /// sequential update files (<c>….001</c>, <c>….002</c>, …) that live in the
    /// same directory. This gives a single dropped <c>.000</c> cell the same
    /// up-to-date rendering as one loaded from an exchange set. S-57 and S-101
    /// cells (told apart by the <c>DSPM</c> content sniff in
    /// <see cref="DetectProductSpec"/>) both apply updates via their respective
    /// <c>*WithUpdates</c> path; any other product, or a base cell with no
    /// updates on disk, falls back to <see cref="CreateProcessor(string)"/>.
    /// S-57 Ed 3.1 App B.1 / S-100 Part 10a.
    /// </summary>
    /// <param name="baseFilePath">Path to the base cell file (<c>….000</c>).</param>
    public IDatasetProcessor CreateProcessorWithFilesystemUpdates(string baseFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseFilePath);

        var updates = S101FilesystemUpdateDiscovery.FindSequentialUpdates(baseFilePath);
        if (updates.Count == 0)
            return CreateProcessor(baseFilePath);

        var spec = DetectProductSpec(baseFilePath, _registry);
        switch (spec)
        {
            case "S-101":
                return CreateS101ProcessorWithUpdates(baseFilePath, updates);
            case "S-57":
                var directory = Path.GetDirectoryName(Path.GetFullPath(baseFilePath))
                    ?? throw new ArgumentException(
                        "Base cell path must include a directory.", nameof(baseFilePath));
                var source = FileSystemAssetSource.Create(directory);
                var updateNames = updates.Select(Path.GetFileName).OfType<string>().ToList();
                return CreateS57ProcessorWithUpdates(
                    source, Path.GetFileName(baseFilePath), updateNames);
            default:
                // Non-ENC products never carry .000 sequential updates.
                return CreateProcessor(baseFilePath);
        }
    }

    /// <summary>
    /// Normalizes an exchange-set product identifier (e.g. <c>"S-101"</c>,
    /// <c>"S101"</c>, <c>"s-101"</c>) to the canonical spec strings used
    /// by <see cref="CreateProcessor(string)"/>'s switch (<c>"S-101"</c>, etc.).
    /// Returns <c>null</c> when the identifier is null, blank, or unrecognized.
    /// </summary>
    public static string? MapProductIdentifierToSpec(string? productIdentifier)
    {
        if (string.IsNullOrWhiteSpace(productIdentifier)) return null;
        var trimmed = productIdentifier.Trim();
        var normalized = trimmed.StartsWith("S-", StringComparison.OrdinalIgnoreCase)
            ? "S-" + trimmed[2..]
            : trimmed.StartsWith('S') || trimmed.StartsWith('s')
                ? "S-" + trimmed[1..]
                : trimmed;
        normalized = normalized.ToUpperInvariant();
        return normalized switch
        {
            "S-57" or "S-101" or "S-102" or "S-104" or "S-111"
                or "S-122" or "S-124" or "S-125" or "S-127" or "S-128"
                or "S-129" or "S-131" or "S-201" or "S-411" or "S-421" => normalized,
            _ => null,
        };
    }

    /// <summary>
    /// Resolves the canonical spec string (<c>"S-101"</c>, etc.) for an
    /// exchange-set <see cref="ProductSpecification"/> entry. Tries, in order,
    /// the <see cref="ProductSpecification.ProductIdentifier"/>, the
    /// <see cref="ProductSpecification.Name"/>, and finally the
    /// <see cref="ProductSpecification.Number"/> (e.g. <c>101</c> → <c>"S-101"</c>).
    /// Many real-world S-100 catalogues (e.g. IC-ENC S-101 sets) populate only
    /// <c>name</c>/<c>number</c> and omit <c>productIdentifier</c>; this overload
    /// keeps such datasets from being reported as an unsupported product
    /// specification. Returns <c>null</c> when none of the fields resolve.
    /// </summary>
    public static string? MapProductSpecificationToSpec(ProductSpecification? productSpecification)
    {
        if (productSpecification is null) return null;

        return MapProductIdentifierToSpec(productSpecification.ProductIdentifier)
            ?? MapProductIdentifierToSpec(productSpecification.Name)
            ?? (productSpecification.Number is int number
                ? MapProductIdentifierToSpec(string.Create(CultureInfo.InvariantCulture, $"S-{number}"))
                : null);
    }

    private static string? DetectProductSpecByExtension(string relativePath)
    {
        var ext = Path.GetExtension(relativePath);
        if (string.Equals(ext, ".000", StringComparison.OrdinalIgnoreCase))
        {
            // Could be S-101 or legacy S-57; without content access we
            // cannot disambiguate cheaply. Caller should supply
            // declaredProductSpec for ISO 8211 datasets.
            return null;
        }
        if (string.Equals(ext, ".h5", StringComparison.OrdinalIgnoreCase))
        {
            // HDF5 product spec cannot be inferred from extension alone.
            return null;
        }
        return null;
    }

    /// <summary>
    /// Content-sniffs a GML dataset stored inside <paramref name="source"/> to
    /// determine its S-100 product specification when the exchange-set catalogue
    /// omits a machine-readable <c>productIdentifier</c>. Real-world JCOMM S-411
    /// exchange sets declare only a human-readable product-specification
    /// <c>name</c> (e.g. "Ice Information Product Specification (JCOMM S-411)")
    /// with no identifier or number, so the declared spec cannot be mapped and
    /// the dataset must be recognized from its GML root element / namespaces.
    /// This synchronous wrapper exists for synchronous processor creation; async
    /// callers should use <see cref="DetectProductSpecFromSourceAsync"/>.
    /// </summary>
    /// <param name="source">The asset source (folder or ZIP) hosting the dataset.</param>
    /// <param name="relativePath">Path to the dataset, relative to <paramref name="source"/>.</param>
    public static string? DetectProductSpecFromSource(IAssetSource source, string relativePath)
    {
        return DetectProductSpecFromSourceAsync(source, relativePath)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Asynchronously content-sniffs a GML dataset stored inside
    /// <paramref name="source"/> to determine its S-100 product specification
    /// when the exchange-set catalogue omits a machine-readable
    /// <c>productIdentifier</c>. Real-world JCOMM S-411 exchange sets declare
    /// only a human-readable product-specification <c>name</c> (e.g. "Ice
    /// Information Product Specification (JCOMM S-411)") with no identifier or
    /// number, so the declared spec cannot be mapped and the dataset must be
    /// recognized from its GML root element / namespaces. Reads only a bounded,
    /// BOM-aware prefix of the dataset. Only <c>.gml</c>/<c>.xml</c> datasets
    /// are sniffed; returns <c>null</c> when the file cannot be read or is
    /// unrecognized. Returns the canonical spec string (e.g. <c>"S-411"</c>)
    /// understood by
    /// <see cref="CreateProcessor(IAssetSource, string, string?, IReadOnlyDictionary{string, string}?)"/>.
    /// </summary>
    /// <param name="source">The asset source (folder or ZIP) hosting the dataset.</param>
    /// <param name="relativePath">Path to the dataset, relative to <paramref name="source"/>.</param>
    /// <param name="cancellationToken">Cancellation token for reading dataset bytes.</param>
    /// <returns>The canonical product-specification string, or <c>null</c> when not recognized.</returns>
    public static async Task<string?> DetectProductSpecFromSourceAsync(
        IAssetSource source,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        var ext = Path.GetExtension(relativePath);
        if (!string.Equals(ext, ".gml", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ext, ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            await using var stream = await source.OpenAsync(relativePath, cancellationToken)
                .ConfigureAwait(false);
            using var streamReader = new StreamReader(
                stream,
                System.Text.Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            const int maxSniffPrefixChars = 64 * 1024;
            var buffer = new char[maxSniffPrefixChars];
            int read = await streamReader.ReadBlockAsync(
                    buffer.AsMemory(0, maxSniffPrefixChars),
                    cancellationToken)
                .ConfigureAwait(false);
            var xml = new string(buffer, 0, read).TrimStart();
            return DetectGmlProductSpecFromXml(xml);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
