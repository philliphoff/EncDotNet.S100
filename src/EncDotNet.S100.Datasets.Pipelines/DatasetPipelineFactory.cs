using System.Globalization;
using EncDotNet.Iso8211;
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
/// Detects a dataset's product — each <see cref="ProductRegistration"/> claims
/// its own files — and creates the appropriate <see cref="IDatasetProcessor"/>.
/// </summary>
public sealed class DatasetPipelineFactory : IDatasetProcessorFactory
{
    private readonly ProductRegistry _registry;
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
    /// <param name="productRegistry">
    /// Optional set of products this factory can construct. When
    /// <see langword="null"/> the process-wide <see cref="ProductRegistry.Default"/>
    /// is used: every built-in product, plus any a host registered there. Supply a custom registry to enable only a subset of
    /// products (e.g. an S-101-only host) or to add a product of your own.
    /// </param>
    public DatasetPipelineFactory(
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        ICrsTransformFactory crsTransformFactory,
        FeatureCatalogueManager featureCatalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        IPortrayalInstructionCache? sharedInstructionCache = null,
        ProductRegistry? productRegistry = null)
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
        };
        _registry = productRegistry ?? ProductRegistry.Default;
    }

    /// <summary>
    /// The spec an ISO 8211 (<c>.000</c>) cell gets when no registration claims
    /// it — an early or non-conformant cell declaring no <c>PRSP</c>, an
    /// unreadable cell, or a product the registry lacks. The one fallback
    /// detection keeps, because it is what this factory has always answered for
    /// a <c>.000</c> file; every other unclaimed file is unknown.
    /// </summary>
    private const string Iso8211FallbackSpec = "S-101";

    /// <summary>
    /// Returns the product spec identifier for the given file, or null if
    /// unrecognized, using <see cref="ProductRegistry.Default"/> (every built-in
    /// product plus any a host registered there, so a <c>.000</c> file is
    /// disambiguated S-57 vs S-101). Hosts that build a factory from a subset
    /// registry get subset-aware detection through the registry-aware overload
    /// the factory uses internally.
    /// </summary>
    public static string? DetectProductSpec(string path) => DetectProductSpec(path, ProductRegistry.Default);

    /// <summary>
    /// Registry-aware detection — the single implementation behind
    /// <see cref="DetectProductSpec(string)"/>. Each registration claims its own
    /// files; there is no per-product chain here:
    /// <list type="number">
    /// <item><description>
    /// The file's leading bytes are read once (<see cref="DatasetHeader"/>).
    /// </description></item>
    /// <item><description>
    /// The encoding is taken from the extension (<c>.000</c> ISO 8211,
    /// <c>.h5</c>/<c>.hdf5</c> HDF5, <c>.gml</c> GML) or, when the extension is
    /// none of those, from the HDF5 signature in the leading bytes. The
    /// encoding's envelope is read once and offered to every registration's
    /// matcher for it (<see cref="ProductRegistration.MatchIso8211"/>,
    /// <see cref="ProductRegistration.MatchHdf5"/>,
    /// <see cref="ProductRegistration.MatchGml"/>).
    /// </description></item>
    /// <item><description>
    /// A file no envelope matcher claimed is offered to every
    /// <see cref="ProductRegistration.MatchBytes"/>, whatever its extension —
    /// so a GRIB2 download with no useful extension is still recognised.
    /// </description></item>
    /// <item><description>
    /// An unclaimed <c>.000</c> file is <c>"S-101"</c>, as it always has been;
    /// any other unclaimed file — including an HDF5 file whose
    /// <c>productSpecification</c> no product declares — is unknown
    /// (<see langword="null"/>).
    /// </description></item>
    /// </list>
    /// A registry without S-57 registered therefore never reports an S-57
    /// dataset it has no registration to build, and the same holds for every
    /// product. It does not otherwise guarantee the returned spec is buildable
    /// (a registry lacking S-101 can still get <c>"S-101"</c> from the ISO 8211
    /// fallback — construction then validates the spec against the registry and
    /// throws if unregistered).
    /// </summary>
    public static string? DetectProductSpec(string path, ProductRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(path);

        // A folder is a dataset only when a product reads folders (a GRIB2 run, #1075).
        if (Directory.Exists(path))
            return ClaimFolder(path, registry);

        var header = ReadHeader(path);
        var encoding = EncodingOf(header, acceptXmlExtension: false);

        var spec = encoding switch
        {
            DatasetEncoding.Iso8211 => ClaimIso8211(ReadIso8211(path), registry),
            DatasetEncoding.Hdf5 => ClaimHdf5(path, registry),
            DatasetEncoding.Gml => ClaimGml(ReadGmlText(path), registry),
            _ => null,
        };

        return spec
            ?? ClaimBytes(header, registry)
            ?? (encoding == DatasetEncoding.Iso8211 ? Iso8211FallbackSpec : null);
    }

    /// <summary>The encodings whose envelope the factory reads for the matchers.</summary>
    private enum DatasetEncoding
    {
        Unknown,
        Iso8211,
        Hdf5,
        Gml,
    }

    /// <summary>
    /// The encoding of a dataset: from its extension when that names one, else
    /// from the HDF5 signature in its leading bytes. Only the binary HDF5
    /// signature is sniffed: an ISO 8211 update file (<c>.001</c>) or an XML
    /// exchange-set catalogue must not be mistaken for a dataset, so ISO 8211
    /// and GML are recognised by extension alone. The asset-source route also
    /// accepts <c>.xml</c> as GML (<paramref name="acceptXmlExtension"/>), as it
    /// always has, because it only sees datasets an exchange set lists.
    /// </summary>
    private static DatasetEncoding EncodingOf(DatasetHeader header, bool acceptXmlExtension)
    {
        if (header.HasExtension(".000"))
            return DatasetEncoding.Iso8211;
        if (header.HasExtension(".h5") || header.HasExtension(".hdf5"))
            return DatasetEncoding.Hdf5;
        if (header.HasExtension(".gml") || (acceptXmlExtension && header.HasExtension(".xml")))
            return DatasetEncoding.Gml;
        return header.IsHdf5 ? DatasetEncoding.Hdf5 : DatasetEncoding.Unknown;
    }

    /// <summary>
    /// Reads up to <see cref="DatasetHeader.MaxLength"/> leading bytes of the
    /// file at <paramref name="path"/>; an unreadable file has no leading bytes.
    /// </summary>
    private static DatasetHeader ReadHeader(string path)
    {
        var extension = Path.GetExtension(path);
        try
        {
            using var stream = File.OpenRead(path);
            return new DatasetHeader { LeadingBytes = ReadLeadingBytes(stream), Extension = extension };
        }
        catch
        {
            // Missing or unreadable: matchers see no bytes.
            return new DatasetHeader { LeadingBytes = ReadOnlyMemory<byte>.Empty, Extension = extension };
        }
    }

    private static byte[] ReadLeadingBytes(Stream stream)
    {
        var buffer = new byte[DatasetHeader.MaxLength];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
            total += read;
        return total == buffer.Length ? buffer : buffer[..total];
    }

    private static Iso8211Document? ReadIso8211(string path)
    {
        try
        {
            return Iso8211DocumentReader.ReadFromFile(path);
        }
        catch
        {
            // Not readable as ISO 8211 — no envelope.
            return null;
        }
    }

    private static string? ReadGmlText(string path)
    {
        try
        {
            // Some GML files have leading whitespace before the XML declaration;
            // read as text, trim, and parse via a StringReader to tolerate this.
            return File.ReadAllText(path).TrimStart();
        }
        catch
        {
            // Unable to read – unknown.
            return null;
        }
    }

    /// <summary>
    /// Returns the canonical spec of <paramref name="registration"/> (mirroring
    /// how <see cref="ProductRegistry"/> canonicalizes its keys), so a product
    /// registered under a non-canonical identifier (e.g. <c>"s124"</c>) is still
    /// detected as its canonical <c>"S-124"</c>. Built-in registrations already
    /// carry a canonical spec, so this is a no-op for them.
    /// </summary>
    private static string CanonicalSpecOf(ProductRegistration registration) =>
        ProductRegistry.NormalizeSpec(registration.Spec);

    /// <summary>
    /// Offers an ISO 8211 dataset's envelope (see <see cref="TryReadIso8211Root"/>)
    /// to each product in <paramref name="registry"/>, returning the first
    /// claimant's spec, or <see langword="null"/> when <paramref name="iso"/> is
    /// unreadable or nobody claims it.
    /// </summary>
    private static string? ClaimIso8211(Iso8211Document? iso, ProductRegistry registry)
    {
        if (iso is null || !TryReadIso8211Root(iso, out var root))
            return null;

        foreach (var registration in registry.Registrations)
        {
            if (registration.MatchIso8211 is { } match && match(root))
                return CanonicalSpecOf(registration);
        }

        return null;
    }

    /// <summary>
    /// Offers a file's leading bytes to each product's
    /// <see cref="ProductRegistration.MatchBytes"/>, returning the first
    /// claimant's spec or <see langword="null"/>.
    /// </summary>
    /// <summary>
    /// A declared spec the registry has that isn't an S-100 product (<c>GRIB2</c>,
    /// <c>NetCDF</c>, <c>CAP</c>), as a Library item names it (#1075): a GRIB2 run
    /// folder can't be sniffed from a source, so its declared spec is taken.
    /// </summary>
    private string? DeclaredNonS100Spec(string? declared) =>
        !string.IsNullOrWhiteSpace(declared) && _registry.TryResolve(declared.Trim(), out var registration) && !registration.IsS100
            ? registration.Spec
            : null;

    private static string? ClaimFolder(string path, ProductRegistry registry)
    {
        foreach (var registration in registry.Registrations)
        {
            if (registration.MatchFolder is not { } match)
                continue;

            try
            {
                if (match(path))
                    return CanonicalSpecOf(registration);
            }
            catch
            {
                // A faulty matcher must not break detection for every other product.
            }
        }

        return null;
    }

    private static string? ClaimBytes(DatasetHeader header, ProductRegistry registry)
    {
        foreach (var registration in registry.Registrations)
        {
            if (registration.MatchBytes is not { } match)
                continue;

            try
            {
                if (match(header))
                    return CanonicalSpecOf(registration);
            }
            catch
            {
                // A faulty matcher must not break detection for every other product.
            }
        }

        return null;
    }

    /// <summary>
    /// Reads an ISO 8211 dataset's envelope into an <see cref="Iso8211RootInfo"/>
    /// — its declared product and encoding specification (the <c>DSID</c> field's
    /// <c>PRSP</c> / <c>ENSP</c> subfields) and whether the Data Descriptive
    /// Record defines the S-57-only <c>DSPM</c> field — or returns
    /// <see langword="false"/> when the file cannot be read as ISO 8211. The
    /// per-product <see cref="ProductRegistration.MatchIso8211"/> recognizers
    /// consume the result, so the file is parsed once no matter how many products
    /// are registered. S-100 Edition 5.2.1 Part 10a; S-57 Ed 3.1 Appendix B.1.
    /// </summary>
    private static bool TryReadIso8211Root(Iso8211Document iso, out Iso8211RootInfo root)
    {
        root = default;
        try
        {
            if (iso.DataDescriptiveRecord is null)
                return false;

            var ddr = Iso8211DataDescriptiveRecordReader.Read(iso.DataDescriptiveRecord);

            var productSpecification = "";
            var encodingSpecification = "";

            // DSID is the first data record in a conformant cell. S-57 carries a
            // DSID too, but its PRSP is a numeric S-57 product specification code
            // (not an S-100 identifier) and it has no ENSP, which is why the DSPM
            // field below is what identifies it.
            var dsidDefinition = ddr.GetFieldDefinition("DSID");
            if (dsidDefinition is not null)
            {
                foreach (var record in iso.DataRecords)
                {
                    if (record.GetFieldByTag("DSID") is not { } dsid)
                        continue;

                    var reader = new Iso8211FieldReader(dsidDefinition, dsid.Data);
                    if (reader.TryGetSubfield<string>("PRSP", out var prsp))
                        productSpecification = prsp ?? "";
                    if (reader.TryGetSubfield<string>("ENSP", out var ensp))
                        encodingSpecification = ensp ?? "";
                    break;
                }
            }

            root = new Iso8211RootInfo
            {
                ProductSpecification = productSpecification,
                EncodingSpecification = encodingSpecification,
                HasDataSetParameterField = ddr.GetFieldDefinition("DSPM") is not null,
            };
            return true;
        }
        catch
        {
            // Not readable as ISO 8211 — unknown.
            return false;
        }
    }

    private static string? ClaimHdf5(string path, ProductRegistry registry)
    {
        try
        {
            // Own the stream: PureHDF can leave the file handle of a path it
            // opened itself open when the open fails (an .h5 file that isn't
            // HDF5, such as a NetCDF-3 file), which keeps the file locked on
            // Windows. (The signature may sit after a user block, so the
            // leading bytes alone can't rule HDF5 out.)
            using var stream = File.OpenRead(path);
            using var hdf5 = PureHdfFile.Open(stream);
            return ClaimHdf5(hdf5, registry);
        }
        catch
        {
            // Not readable as HDF5: unknown.
            return null;
        }
    }

    /// <summary>
    /// Offers an HDF5 file's root group — its <c>productSpecification</c>
    /// attribute (S-100 Part 10c) read once, and any other root attribute on
    /// demand — to each product's <see cref="ProductRegistration.MatchHdf5"/>,
    /// returning the first claimant's spec. An HDF5 file nobody claims is
    /// unknown (<see langword="null"/>); it is no longer assumed to be S-102.
    /// </summary>
    private static string? ClaimHdf5(PureHdfFile hdf5, ProductRegistry registry)
    {
        Hdf5RootInfo root;
        try
        {
            var group = hdf5.Root;
            var productSpecification = "";
            try
            {
                if (group.AttributeExists("productSpecification"))
                    productSpecification = group.ReadStringAttribute("productSpecification") ?? "";
            }
            catch
            {
                // Unreadable attribute: the file declares no product.
            }

            root = new Hdf5RootInfo { ProductSpecification = productSpecification, Root = group };
        }
        catch
        {
            return null;
        }

        foreach (var registration in registry.Registrations)
        {
            if (registration.MatchHdf5 is not { } match)
                continue;

            try
            {
                if (match(root))
                    return CanonicalSpecOf(registration);
            }
            catch
            {
                // A faulty matcher must not break detection for every other product.
            }
        }

        return null;
    }

    /// <summary>
    /// The message for an HDF5 file whose root has no <c>productSpecification</c>
    /// attribute (S-100 Part 10c Table 10c-6, mandatory). Such a file is unknown:
    /// it isn't assumed to be S-102 (#1092).
    /// </summary>
    internal const string Hdf5NoProductSpecificationMessage =
        "This HDF5 file doesn't say which S-100 product it is (no productSpecification attribute).";

    /// <summary>
    /// Explains why <see cref="DetectProductSpec(string)"/> found no product for
    /// <paramref name="path"/>, as one sentence a host can show, using
    /// <see cref="ProductRegistry.Default"/>. Returns <see langword="null"/>
    /// when there is nothing more specific to say than "not recognised" (the
    /// file isn't HDF5, or a product does claim it).
    /// </summary>
    /// <remarks>
    /// Three HDF5 cases are told apart:
    /// <list type="bullet">
    /// <item><description>
    /// the root has no <c>productSpecification</c> attribute;
    /// </description></item>
    /// <item><description>
    /// it names a product no registration reads, quoting the value;
    /// </description></item>
    /// <item><description>
    /// a file named <c>.h5</c> or <c>.hdf5</c> can't be read as HDF5: with no
    /// HDF5 signature it may be encrypted (S-100 Part 15), otherwise damaged.
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <param name="path">The dataset file.</param>
    public static string? DescribeUnrecognizedDataset(string path) =>
        DescribeUnrecognizedDataset(path, ProductRegistry.Default);

    /// <summary>
    /// Registry-aware <see cref="DescribeUnrecognizedDataset(string)"/>: a value
    /// naming a product <paramref name="registry"/> lacks is reported as
    /// unsupported.
    /// </summary>
    /// <param name="path">The dataset file.</param>
    /// <param name="registry">The products that can be detected.</param>
    public static string? DescribeUnrecognizedDataset(string path, ProductRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(registry);

        if (Directory.Exists(path) || !File.Exists(path))
            return null;

        var header = ReadHeader(path);
        if (EncodingOf(header, acceptXmlExtension: false) != DatasetEncoding.Hdf5)
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            using var hdf5 = PureHdfFile.Open(stream);
            if (ClaimHdf5(hdf5, registry) is not null)
                return null;

            var group = hdf5.Root;
            string? productSpecification = null;
            try
            {
                if (group.AttributeExists("productSpecification"))
                    productSpecification = group.ReadStringAttribute("productSpecification");
            }
            catch
            {
                // Not a string (or unreadable): the file declares no product, as detection reads it.
            }

            return string.IsNullOrWhiteSpace(productSpecification)
                ? Hdf5NoProductSpecificationMessage
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"This HDF5 file declares productSpecification '{productSpecification.Trim()}', which isn't a supported product.");
        }
        catch
        {
            // Not readable as HDF5. An S-100 Part 15 encrypted dataset is
            // ciphertext from its first byte, so it has no HDF5 signature.
            return header.IsHdf5
                ? "This HDF5 file can't be read; it may be damaged or truncated."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"This file is named {header.Extension} but isn't readable as HDF5. It may be encrypted (S-100 Part 15), which opens only from its exchange set with a permit, or it isn't HDF5.");
        }
    }

    /// <summary>
    /// Determines the product specification (e.g. <c>"S-411"</c>) of an
    /// already-loaded GML document body by reading its root element once and
    /// returning the spec of the first product in <paramref name="registry"/>
    /// whose <see cref="ProductRegistration.MatchGml"/> claims it. Each GML
    /// product contributes its own recognition rule (namespace / local name /
    /// <c>productIdentifier</c>) next to its construction registration in
    /// <see cref="S100Products"/>, so there is no central per-product switch
    /// here. Shared by the file-path and asset-source detection paths so
    /// exchange-set datasets whose catalogue omits a machine-readable
    /// <c>productIdentifier</c> (common for JCOMM S-411 sets) are still routed
    /// correctly. Returns <c>null</c> when unrecognized.
    /// </summary>
    private static string? ClaimGml(string? xml, ProductRegistry registry)
    {
        if (xml is null || !TryReadGmlRoot(xml, out var root))
            return null;

        foreach (var registration in registry.Registrations)
        {
            if (registration.MatchGml is { } match && match(root))
                return CanonicalSpecOf(registration);
        }

        return null;
    }

    /// <summary>
    /// Reads the root element of a GML document into a <see cref="GmlRootInfo"/>
    /// (its namespace URI, local name, and the values of its attributes), or
    /// returns <see langword="false"/> when the document has no element or cannot
    /// be parsed. The per-product <see cref="ProductRegistration.MatchGml"/>
    /// recognizers consume the result.
    /// </summary>
    private static bool TryReadGmlRoot(string xml, out GmlRootInfo root)
    {
        root = default;
        try
        {
            using var stringReader = new StringReader(xml);
            using var reader = System.Xml.XmlReader.Create(
                stringReader,
                new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
            while (reader.Read())
            {
                if (reader.NodeType != System.Xml.XmlNodeType.Element)
                    continue;

                IReadOnlyList<string> attributeValues = Array.Empty<string>();
                if (reader.MoveToFirstAttribute())
                {
                    var values = new List<string>();
                    do
                    {
                        values.Add(reader.Value);
                    } while (reader.MoveToNextAttribute());
                    reader.MoveToElement();
                    attributeValues = values;
                }

                root = new GmlRootInfo
                {
                    NamespaceUri = reader.NamespaceURI,
                    LocalName = reader.LocalName,
                    AttributeValues = attributeValues,
                    Xml = xml,
                };
                return true;
            }
        }
        catch
        {
            // Unable to parse – unknown
        }

        return false;
    }

    /// <summary>
    /// Creates a processor for the given dataset file.
    /// The processor can be called multiple times with different contexts.
    /// </summary>
    public IDatasetProcessor CreateProcessor(string path)
    {
        var spec = DetectProductSpec(path, _registry)
            ?? throw new NotSupportedException(
                DescribeUnrecognizedDataset(path, _registry) is { } reason
                    ? $"Unrecognized dataset file: {Path.GetFileName(path)}. {reason}"
                    : $"Unrecognized dataset file: {Path.GetFileName(path)}");

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
    /// unrecognized, the dataset is content-sniffed as described on
    /// <see cref="DetectProductSpecFromSourceAsync(IAssetSource, string, CancellationToken)"/>.
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
            ?? DeclaredNonS100Spec(declaredProductSpec)
            ?? DetectProductSpecFromSource(source, relativePath, _registry)
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
    /// <param name="supportFiles">
    /// Optional map of support-file name to source-relative path, used to
    /// resolve file references the cell makes to its exchange set.
    /// </param>
    /// <param name="spec">
    /// The product whose catalogues portray the cell: <c>"S-101"</c> (default) or
    /// <c>"S-401"</c> for inland ENC, which shares the Part 10a encoding and the
    /// Part 9A Lua portrayal model.
    /// </param>
    public IDatasetProcessor CreateS101ProcessorWithUpdates(
        IAssetSource source,
        string baseRelativePath,
        IReadOnlyList<string> updateRelativePaths,
        IReadOnlyDictionary<string, string>? supportFiles = null,
        string spec = "S-101")
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
            spec);
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
    /// <param name="spec">
    /// The product whose catalogues portray the cell: <c>"S-101"</c> (default) or
    /// <c>"S-401"</c> for inland ENC.
    /// </param>
    public IDatasetProcessor CreateS101ProcessorWithUpdates(
        string baseFilePath,
        IReadOnlyList<string> updateFilePaths,
        string spec = "S-101")
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
            spec: spec);
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
            _services.FeatureCatalogueManager,
            _services.SharedInstructionCache);
    }

    /// <summary>
    /// Creates a processor for the base cell file at
    /// <paramref name="baseFilePath"/>, discovering and applying any sibling
    /// sequential update files (<c>….001</c>, <c>….002</c>, …) that live in the
    /// same directory. This gives a single dropped <c>.000</c> cell the same
    /// up-to-date rendering as one loaded from an exchange set. S-57, S-101, and
    /// S-401 cells (told apart by the ISO 8211 envelope in
    /// <see cref="DetectProductSpec(string)"/>) all apply updates via their respective
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
            case "S-401":
                return CreateS101ProcessorWithUpdates(baseFilePath, updates, spec);
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
    /// <c>"S101"</c>, <c>"s-101"</c>, or the long form
    /// <c>"INT.IHO.S-101.2.0.0"</c>) to the canonical spec strings used
    /// by <see cref="CreateProcessor(string)"/>'s switch (<c>"S-101"</c>, etc.).
    /// Returns <c>null</c> when the identifier is null, blank, or unrecognized.
    /// </summary>
    public static string? MapProductIdentifierToSpec(string? productIdentifier)
    {
        if (string.IsNullOrWhiteSpace(productIdentifier)) return null;
        var trimmed = productIdentifier.Trim();
        if (trimmed.StartsWith("INT.IHO.", StringComparison.OrdinalIgnoreCase))
        {
            // The long form carries the edition too; only the name selects the product.
            return SpecRef.TryParse(trimmed, out var specRef) && S100Products.KnownSpecs.Contains(specRef.Name)
                ? specRef.Name
                : null;
        }

        var normalized = trimmed.StartsWith("S-", StringComparison.OrdinalIgnoreCase)
            ? "S-" + trimmed[2..]
            : trimmed.StartsWith('S') || trimmed.StartsWith('s')
                ? "S-" + trimmed[1..]
                : trimmed;
        normalized = normalized.ToUpperInvariant();
        // The set of recognized identifiers is derived from the built-in product
        // registrations (S100Products.KnownSpecs) rather than hard-coded here, so
        // adding a product does not require editing this allow-list too.
        return S100Products.KnownSpecs.Contains(normalized) ? normalized : null;
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

    /// <summary>
    /// Content-sniffs a dataset stored inside <paramref name="source"/> to
    /// determine its S-100 product specification. This synchronous wrapper
    /// exists for synchronous processor creation; async callers should use
    /// <see cref="DetectProductSpecFromSourceAsync(IAssetSource, string, CancellationToken)"/>, which documents the
    /// detection rules.
    /// </summary>
    /// <param name="source">The asset source (folder or ZIP) hosting the dataset.</param>
    /// <param name="relativePath">Path to the dataset, relative to <paramref name="source"/>.</param>
    /// <returns>The canonical product-specification string, or <c>null</c> when not recognized.</returns>
    public static string? DetectProductSpecFromSource(IAssetSource source, string relativePath) =>
        DetectProductSpecFromSource(source, relativePath, ProductRegistry.Default);

    /// <summary>
    /// Registry-aware synchronous source detection, used by
    /// <see cref="CreateProcessor(IAssetSource, string, string?, IReadOnlyDictionary{string, string}?)"/>
    /// so the ISO 8211 and GML recognizers consult the factory's own product
    /// registry, as path-based detection does.
    /// </summary>
    public static string? DetectProductSpecFromSource(
        IAssetSource source, string relativePath, ProductRegistry registry)
    {
        return DetectProductSpecFromSourceAsync(source, relativePath, registry, CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Asynchronously content-sniffs a dataset stored inside
    /// <paramref name="source"/> to determine its product specification,
    /// applying the same rules as <see cref="DetectProductSpec(string, ProductRegistry)"/>
    /// does for a file on disk — each registration claims its own files:
    /// <list type="bullet">
    /// <item><description>
    /// <c>.000</c> (ISO 8211): the dataset envelope is read and each product
    /// recognizes its own cells (S-57 by its <c>DSPM</c> field, S-101 and S-401
    /// by their declared <c>PRSP</c>); a readable cell that no product claims is
    /// treated as <c>"S-101"</c>.
    /// </description></item>
    /// <item><description>
    /// <c>.h5</c> / <c>.hdf5</c>, or any other extension when the file starts
    /// with the HDF5 signature: each product's
    /// <see cref="ProductRegistration.MatchHdf5"/> reads the root group
    /// (S-102, S-104 and S-111 by their <c>productSpecification</c>); an HDF5
    /// file nobody claims is unknown.
    /// </description></item>
    /// <item><description>
    /// <c>.gml</c> / <c>.xml</c>: the GML root element and namespaces are
    /// matched against each product's <see cref="ProductRegistration.MatchGml"/>,
    /// reading only a bounded, BOM-aware prefix. This also covers catalogues
    /// that omit a machine-readable <c>productIdentifier</c>, such as JCOMM
    /// S-411 exchange sets that declare only a human-readable product name.
    /// </description></item>
    /// <item><description>
    /// Anything nobody has claimed yet: the leading bytes are offered to each
    /// product's <see cref="ProductRegistration.MatchBytes"/>.
    /// </description></item>
    /// </list>
    /// Returns the canonical spec string (e.g. <c>"S-411"</c>) understood by
    /// <see cref="CreateProcessor(IAssetSource, string, string?, IReadOnlyDictionary{string, string}?)"/>.
    /// Uses <see cref="ProductRegistry.Default"/>.
    /// </summary>
    /// <param name="source">The asset source (folder or ZIP) hosting the dataset.</param>
    /// <param name="relativePath">Path to the dataset, relative to <paramref name="source"/>.</param>
    /// <param name="cancellationToken">Cancellation token for reading dataset bytes.</param>
    /// <returns>
    /// The canonical product-specification string, or <c>null</c> when no
    /// product claims the file or the file cannot be read.
    /// </returns>
    public static Task<string?> DetectProductSpecFromSourceAsync(
        IAssetSource source,
        string relativePath,
        CancellationToken cancellationToken = default) =>
        DetectProductSpecFromSourceAsync(source, relativePath, ProductRegistry.Default, cancellationToken);

    private static async Task<string?> DetectProductSpecFromSourceAsync(
        IAssetSource source,
        string relativePath,
        ProductRegistry registry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        var extension = Path.GetExtension(relativePath);
        try
        {
            if (string.Equals(extension, ".000", StringComparison.OrdinalIgnoreCase))
            {
                // The envelope reader needs every byte anyway, so the header is
                // taken from the same read.
                var bytes = await source.ReadAllBytesAsync(relativePath, cancellationToken)
                    .ConfigureAwait(false);
                Iso8211Document? iso;
                try
                {
                    iso = Iso8211DocumentReader.Read(bytes.Bytes.Span);
                }
                catch
                {
                    // Readable but not ISO 8211: same fallback as the path route.
                    iso = null;
                }

                var leading = bytes.Bytes.Length > DatasetHeader.MaxLength
                    ? bytes.Bytes[..DatasetHeader.MaxLength]
                    : bytes.Bytes;
                return ClaimIso8211(iso, registry)
                    ?? ClaimBytes(new DatasetHeader { LeadingBytes = leading, Extension = extension }, registry)
                    ?? Iso8211FallbackSpec;
            }

            DatasetHeader header;
            await using (var stream = await source.OpenAsync(relativePath, cancellationToken)
                .ConfigureAwait(false))
            {
                header = new DatasetHeader
                {
                    LeadingBytes = await ReadLeadingBytesAsync(stream, cancellationToken).ConfigureAwait(false),
                    Extension = extension,
                };
            }

            var spec = EncodingOf(header, acceptXmlExtension: true) switch
            {
                DatasetEncoding.Hdf5 => await ClaimHdf5Async(source, relativePath, registry, cancellationToken)
                    .ConfigureAwait(false),
                DatasetEncoding.Gml => ClaimGml(
                    await ReadGmlPrefixAsync(source, relativePath, cancellationToken).ConfigureAwait(false),
                    registry),
                _ => null,
            };

            return spec ?? ClaimBytes(header, registry);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The dataset could not be read from the source.
            return null;
        }
    }

    private static async Task<byte[]> ReadLeadingBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[DatasetHeader.MaxLength];
        var total = 0;
        int read;
        while (total < buffer.Length
            && (read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
        }

        return total == buffer.Length ? buffer : buffer[..total];
    }

    private static async Task<string?> ClaimHdf5Async(
        IAssetSource source,
        string relativePath,
        ProductRegistry registry,
        CancellationToken cancellationToken)
    {
        await using var stream = await AssetSourceHelpers
            .OpenSeekableAsync(source, relativePath, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            using var hdf5 = PureHdfFile.Open(stream);
            return ClaimHdf5(hdf5, registry);
        }
        catch
        {
            // Readable but not HDF5: unknown, as on the path route.
            return null;
        }
    }

    private static async Task<string> ReadGmlPrefixAsync(
        IAssetSource source,
        string relativePath,
        CancellationToken cancellationToken)
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
        return new string(buffer, 0, read).TrimStart();
    }
}
