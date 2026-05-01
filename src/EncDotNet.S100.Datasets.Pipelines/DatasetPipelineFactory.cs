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
    public required string ProductSpec { get; init; }
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
    private readonly Func<string, Stream?> _featureCatalogueResolver;

    public DatasetPipelineFactory(
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        ICrsTransformFactory crsTransformFactory,
        Func<string, Stream?>? featureCatalogueResolver = null)
    {
        _catalogueManager = catalogueManager;
        _luaEngine = luaEngine;
        _crsTransformFactory = crsTransformFactory;
        _featureCatalogueResolver = featureCatalogueResolver ?? (_ => null);
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

        // S-101: ISO 8211 files
        if (ext.Equals(".000", StringComparison.OrdinalIgnoreCase))
        {
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
            "S-101" => new S101DatasetProcessor(path, _catalogueManager, _luaEngine, _featureCatalogueResolver),
            "S-104" => new S104DatasetProcessor(path, _crsTransformFactory),
            "S-111" => new S111DatasetProcessor(path, _catalogueManager, _crsTransformFactory),
            "S-122" => new S122DatasetProcessor(path, _catalogueManager),
            "S-124" => new S124DatasetProcessor(path, _catalogueManager),
            "S-127" => new S127DatasetProcessor(path, _catalogueManager),
            "S-129" => new S129DatasetProcessor(path, _catalogueManager),
            "S-411" => new S411DatasetProcessor(path, _catalogueManager),
            "S-421" => new S421DatasetProcessor(path, _catalogueManager),
            _ => throw new NotSupportedException($"Pipeline not implemented for {spec}."),
        };
    }

    /// <summary>
    /// Convenience method: creates a processor and renders with default context.
    /// </summary>
    public DatasetResult Process(string path) => CreateProcessor(path).Render();
}
