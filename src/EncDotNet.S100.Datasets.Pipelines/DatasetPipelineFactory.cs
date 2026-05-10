using EncDotNet.S100.Core;
using EncDotNet.S100.Features;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Result of processing a dataset through the portrayal pipeline.
/// </summary>
public sealed class DatasetResult
{
    public required IReadOnlyList<ILayer> Layers { get; init; }
    public required MRect Extent { get; init; }
    public required string Info { get; init; }
    /// <summary>The product specification (name + edition) of the rendered dataset.</summary>
    public required SpecRef Spec { get; init; }

    /// <summary>
    /// Optional human-readable display names for each layer in
    /// <see cref="Layers"/>, parallel by index. Processors that emit
    /// more than one layer (e.g. S-111 with a colour band plus an
    /// arrow overlay) populate this list so the UI can show
    /// per-sub-layer toggles. Single-layer products leave it null
    /// and the disclosure UI is hidden. When non-null, the list
    /// length must match <see cref="Layers"/>.
    /// </summary>
    public IReadOnlyList<string>? LayerNames { get; init; }
}

/// <summary>
/// Detects dataset type from file extension and creates
/// the appropriate <see cref="IDatasetProcessor"/>.
/// </summary>
public sealed class DatasetPipelineFactory
{
    private readonly PortrayalCatalogueManager _catalogueManager;
    private readonly ILuaEngine _luaEngine;
    private readonly ICrsTransformFactory _crsTransformFactory;
    private readonly FeatureCatalogueManager _featureCatalogueManager;

    public DatasetPipelineFactory(
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        ICrsTransformFactory crsTransformFactory,
        Func<string, Stream?>? featureCatalogueResolver = null)
    {
        _catalogueManager = catalogueManager;
        _luaEngine = luaEngine;
        _crsTransformFactory = crsTransformFactory;
        _featureCatalogueManager = new FeatureCatalogueManager(featureCatalogueResolver);
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
            using var stringReader = new StringReader(xml);
            using var reader = System.Xml.XmlReader.Create(stringReader, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit });
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
        var spec = DetectProductSpec(path)
            ?? throw new NotSupportedException($"Unrecognized dataset file: {Path.GetFileName(path)}");

        return spec switch
        {
            "S-102" => new S102DatasetProcessor(path, _catalogueManager, _luaEngine, _crsTransformFactory),
            "S-101" => new S101DatasetProcessor(path, _catalogueManager, _luaEngine, _featureCatalogueManager),
            "S-57" => new S57DatasetProcessor(path, _catalogueManager, _luaEngine, _featureCatalogueManager),
            "S-104" => new S104DatasetProcessor(path, _crsTransformFactory),
            "S-111" => new S111DatasetProcessor(path, _catalogueManager, _crsTransformFactory),
            "S-122" => new S122DatasetProcessor(path, _catalogueManager, _featureCatalogueManager),
            "S-124" => new S124DatasetProcessor(path, _catalogueManager, _featureCatalogueManager),
            "S-125" => new S125DatasetProcessor(path, _catalogueManager, _featureCatalogueManager),
            "S-127" => new S127DatasetProcessor(path, _catalogueManager, _featureCatalogueManager),
            "S-128" => new S128DatasetProcessor(path, _catalogueManager, _featureCatalogueManager),
            "S-129" => new S129DatasetProcessor(path, _catalogueManager, _featureCatalogueManager),
            "S-411" => new S411DatasetProcessor(path, _catalogueManager, _featureCatalogueManager),
            "S-421" => new S421DatasetProcessor(path, _catalogueManager, _featureCatalogueManager),
            _ => throw new NotSupportedException($"Pipeline not implemented for {spec}."),
        };
    }

    /// <summary>
    /// Convenience method: creates a processor and renders with default context.
    /// </summary>
    public DatasetResult Process(string path) => CreateProcessor(path).Render();

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
    public IDatasetProcessor CreateProcessor(
        IAssetSource source,
        string relativePath,
        string? declaredProductSpec = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        var spec = MapProductIdentifierToSpec(declaredProductSpec)
            ?? DetectProductSpecByExtension(relativePath)
            ?? throw new NotSupportedException(
                $"Unable to determine product specification for '{relativePath}' " +
                $"(declared='{declaredProductSpec ?? "<none>"}').");

        return spec switch
        {
            "S-102" => new S102DatasetProcessor(source, relativePath, _catalogueManager, _luaEngine, _crsTransformFactory),
            "S-101" => new S101DatasetProcessor(source, relativePath, _catalogueManager, _luaEngine, _featureCatalogueManager),
            "S-57" => new S57DatasetProcessor(source, relativePath, _catalogueManager, _luaEngine, _featureCatalogueManager),
            "S-104" => new S104DatasetProcessor(source, relativePath, _crsTransformFactory),
            "S-111" => new S111DatasetProcessor(source, relativePath, _catalogueManager, _crsTransformFactory),
            "S-122" => new S122DatasetProcessor(source, relativePath, _catalogueManager, _featureCatalogueManager),
            "S-124" => new S124DatasetProcessor(source, relativePath, _catalogueManager, _featureCatalogueManager),
            "S-125" => new S125DatasetProcessor(source, relativePath, _catalogueManager, _featureCatalogueManager),
            "S-127" => new S127DatasetProcessor(source, relativePath, _catalogueManager, _featureCatalogueManager),
            "S-128" => new S128DatasetProcessor(source, relativePath, _catalogueManager, _featureCatalogueManager),
            "S-129" => new S129DatasetProcessor(source, relativePath, _catalogueManager, _featureCatalogueManager),
            "S-411" => new S411DatasetProcessor(source, relativePath, _catalogueManager, _featureCatalogueManager),
            "S-421" => new S421DatasetProcessor(source, relativePath, _catalogueManager, _featureCatalogueManager),
            _ => throw new NotSupportedException($"Pipeline not implemented for {spec}."),
        };
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
                or "S-129" or "S-411" or "S-421" => normalized,
            _ => null,
        };
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
}
