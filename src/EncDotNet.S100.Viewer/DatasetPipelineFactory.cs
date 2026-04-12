using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Result of processing a dataset through the portrayal pipeline.
/// </summary>
internal sealed class DatasetResult
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
internal sealed class DatasetPipelineFactory
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
            using var reader = System.Xml.XmlReader.Create(path, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit });
            while (reader.Read())
            {
                if (reader.NodeType == System.Xml.XmlNodeType.Element)
                {
                    // S-124 datasets have a root element in the S-124 namespace
                    if (reader.NamespaceURI.Contains("S-124", StringComparison.OrdinalIgnoreCase)
                        || reader.LocalName.Contains("S124", StringComparison.OrdinalIgnoreCase)
                        || reader.LocalName.Equals("DataSet", StringComparison.OrdinalIgnoreCase))
                    {
                        return "S-124";
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
            "S-124" => new S124DatasetProcessor(path, _catalogueManager),
            _ => throw new NotSupportedException($"Pipeline not implemented for {spec}."),
        };
    }

    /// <summary>
    /// Convenience method: creates a processor and renders with default context.
    /// </summary>
    public DatasetResult Process(string path) => CreateProcessor(path).Render();
}
