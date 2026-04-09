using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Features;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;
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
    public required ILayer Layer { get; init; }
    public required MRect Extent { get; init; }
    public required string Info { get; init; }
    public required string ProductSpec { get; init; }
}

/// <summary>
/// Detects dataset type from file extension, opens the dataset,
/// runs the appropriate portrayal pipeline, and produces a Mapsui layer.
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

        // S-102: HDF5 files
        if (ext.Equals(".h5", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".H5", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".hdf5", StringComparison.OrdinalIgnoreCase))
        {
            return "S-102";
        }

        // S-101: ISO 8211 files
        if (ext.Equals(".000", StringComparison.OrdinalIgnoreCase))
        {
            return "S-101";
        }

        return null;
    }

    /// <summary>
    /// Processes a dataset file through the appropriate pipeline.
    /// </summary>
    /// <exception cref="NotSupportedException">The file type is not recognized.</exception>
    /// <exception cref="InvalidOperationException">No portrayal catalogue is configured for the product spec.</exception>
    public DatasetResult Process(string path)
    {
        var spec = DetectProductSpec(path)
            ?? throw new NotSupportedException($"Unrecognized dataset file: {Path.GetFileName(path)}");

        return spec switch
        {
            "S-102" => ProcessS102(path),
            "S-101" => ProcessS101(path),
            _ => throw new NotSupportedException($"Pipeline not implemented for {spec}."),
        };
    }

    private DatasetResult ProcessS102(string path)
    {
        var provider = _catalogueManager.GetProvider("S-102");

        using var hdf5 = PureHdfFile.Open(path);
        var dataset = S102DatasetReader.Read(hdf5);
        var source = new S102CoverageSource(dataset);
        var metadata = source.Metadata;

        var catalogue = new S102PortrayalCatalogue(_luaEngine, provider) { FourShades = true };
        var context = new NavigationContext
        {
            Viewport = new Pipelines.Viewport
            {
                MinLatitude = metadata.Extent.SouthLatitude,
                MaxLatitude = metadata.Extent.NorthLatitude,
                MinLongitude = metadata.Extent.WestLongitude,
                MaxLongitude = metadata.Extent.EastLongitude,
                WidthPixels = metadata.GridMetadata.NumColumns,
                HeightPixels = metadata.GridMetadata.NumRows,
            },
            ScaleDenominator = 50_000,
        };

        var colorScheme = catalogue.ResolveColorScheme(context);
        var sampled = source.Sample(GridRegion.Full);

        var styledLayer = new StyledCoverageLayer
        {
            Coverage = sampled,
            ColorScheme = colorScheme,
            NoDataValue = S102CoverageSource.FillValue,
            Georeferencer = new GridGeoreferencer(
                metadata.GridMetadata,
                metadata.HorizontalCRS),
        };

        var renderer = new MapsuiCoverageRenderer(_crsTransformFactory)
        {
            LayerName = $"S-102: {Path.GetFileName(path)}",
        };

        var mapLayer = renderer.Render(styledLayer, context.Viewport);
        var extent = mapLayer.Extent ?? new MRect(0, 0, 0, 0);

        int crs = dataset.HorizontalCRS ?? 4326;
        var geoId = dataset.GeographicIdentifier ?? Path.GetFileName(path);
        var info = $"{geoId} — {metadata.GridMetadata.NumColumns}×{metadata.GridMetadata.NumRows} grid, CRS: EPSG:{crs}";

        return new DatasetResult
        {
            Layer = mapLayer,
            Extent = extent,
            Info = info,
            ProductSpec = "S-102",
        };
    }

    private DatasetResult ProcessS101(string path)
    {
        var provider = _catalogueManager.GetProvider("S-101");
        var dataset = S101Dataset.Open(path);

        var context = new NavigationContext
        {
            Viewport = new Pipelines.Viewport
            {
                MinLatitude = -90,
                MaxLatitude = 90,
                MinLongitude = -180,
                MaxLongitude = 180,
                WidthPixels = 1024,
                HeightPixels = 768,
            },
            ScaleDenominator = 0,
        };

        // Try the Lua portrayal pipeline if a feature catalogue is available
        var fcPath = _featureCataloguePathResolver("S-101");
        if (fcPath is not null && File.Exists(fcPath))
        {
            try
            {
                Console.WriteLine("[S101-Lua] Starting Lua portrayal pipeline...");
                var fc = FeatureCatalogueReader.Read(fcPath);
                var portrayal = new S101LuaPortrayal(_luaEngine, provider, fc);
                var emitted = portrayal.Execute(dataset, context);
                Console.WriteLine($"[S101-Lua] PortrayalMain completed: {emitted.Count} emitted instructions");

                // Parse emitted instruction strings
                var parsed = new List<ParsedDrawingInstruction>();
                foreach (var e in emitted)
                {
                    parsed.AddRange(DrawingInstructionParser.Parse(e.FeatureRef, e.InstructionString));
                }
                Console.WriteLine($"[S101-Lua] Parsed {parsed.Count} drawing instructions");

                // Render to Mapsui layer
                var vectorRenderer = new MapsuiS101VectorRenderer
                {
                    LayerName = $"S-101: {Path.GetFileName(path)}",
                };
                var mapLayer = vectorRenderer.Render(parsed, dataset);
                var layerExtent = mapLayer.Extent ?? new MRect(0, 0, 0, 0);
                Console.WriteLine($"[S101-Lua] Rendered {parsed.Count} instructions to Mapsui layer");

                var info = $"{dataset.DatasetName} — {dataset.FeatureCount} features, " +
                           $"{emitted.Count} emitted, {parsed.Count} instructions";

                return new DatasetResult
                {
                    Layer = mapLayer,
                    Extent = layerExtent,
                    Info = info,
                    ProductSpec = "S-101",
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[S101-Lua] ERROR: {ex.Message}");
                Console.WriteLine($"[S101-Lua] Falling back to legacy pipeline.");
                // Fall through to legacy pipeline
            }
        }

        // Fallback: use the legacy VectorPipeline (XSLT + per-rule Lua)
        var featureXmlSource = new S101FeatureXmlSource(dataset);
        var catalogue = new S101PortrayalCatalogue(provider, _luaEngine);

        var pipeline = new VectorPipeline(_luaEngine);
        var vectorLayer = pipeline.ProcessAsync(featureXmlSource, catalogue, context)
            .GetAwaiter().GetResult();

        var instructions = vectorLayer.Instructions;

        var fallbackLayer = new MemoryLayer
        {
            Name = $"S-101: {Path.GetFileName(path)}",
        };

        var extent = ComputeVectorExtent(instructions);

        var fallbackInfo = $"{dataset.DatasetName} — {dataset.FeatureCount} features, " +
                   $"{instructions.Count} instructions";

        return new DatasetResult
        {
            Layer = fallbackLayer,
            Extent = extent,
            Info = fallbackInfo,
            ProductSpec = "S-101",
        };
    }

    private static MRect ComputeVectorExtent(IReadOnlyList<DrawingInstruction> instructions)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;

        foreach (var instr in instructions)
        {
            switch (instr)
            {
                case PointInstruction pt:
                    Expand(pt.Longitude, pt.Latitude);
                    break;
                case TextInstruction txt:
                    Expand(txt.Longitude, txt.Latitude);
                    break;
                case LineInstruction line:
                    foreach (var (lon, lat) in line.Geometry)
                        Expand(lon, lat);
                    break;
                case AreaInstruction area:
                    foreach (var ring in area.Rings)
                        foreach (var (lon, lat) in ring)
                            Expand(lon, lat);
                    break;
            }
        }

        if (!any) return new MRect(0, 0, 0, 0);

        // Convert to Mercator for Mapsui
        var (mx1, my1) = Mapsui.Projections.SphericalMercator.FromLonLat(minX, minY);
        var (mx2, my2) = Mapsui.Projections.SphericalMercator.FromLonLat(maxX, maxY);
        return new MRect(mx1, my1, mx2, my2);

        void Expand(double lon, double lat)
        {
            any = true;
            if (lon < minX) minX = lon;
            if (lon > maxX) maxX = lon;
            if (lat < minY) minY = lat;
            if (lat > maxY) maxY = lat;
        }
    }
}
