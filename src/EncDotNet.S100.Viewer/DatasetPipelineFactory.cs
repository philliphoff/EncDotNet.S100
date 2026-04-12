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
    private readonly Func<string, string?> _featureCataloguePathResolver;

    public DatasetPipelineFactory(
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        ICrsTransformFactory crsTransformFactory,
        Func<string, string?>? featureCataloguePathResolver = null)
    {
        _catalogueManager = catalogueManager;
        _luaEngine = luaEngine;
        _crsTransformFactory = crsTransformFactory;
        _featureCataloguePathResolver = featureCataloguePathResolver ?? (_ => null);
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
            "S-101" => new S101DatasetProcessor(path, _catalogueManager, _luaEngine, _featureCataloguePathResolver),
            "S-104" => new S104DatasetProcessor(path, _crsTransformFactory),
            "S-111" => new S111DatasetProcessor(path, _catalogueManager, _crsTransformFactory),
            _ => throw new NotSupportedException($"Pipeline not implemented for {spec}."),
        };
    }

    /// <summary>
    /// Convenience method: creates a processor and renders with default context.
    /// </summary>
    public DatasetResult Process(string path) => CreateProcessor(path).Render();
}
