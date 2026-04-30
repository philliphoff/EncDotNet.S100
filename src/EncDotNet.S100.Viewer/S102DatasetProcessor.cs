using System.IO;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting;
using Mapsui;

namespace EncDotNet.S100.Viewer;

internal sealed class S102DatasetProcessor : IDatasetProcessor
{
    private readonly S102Dataset _dataset;
    private readonly S102CoverageSource _source;
    private readonly S102PortrayalCatalogue _catalogue;
    private readonly ICrsTransformFactory _crsTransformFactory;
    private readonly string _fileName;

    public string ProductSpec => "S-102";

    public S102DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        ICrsTransformFactory crsTransformFactory)
    {
        _fileName = Path.GetFileName(path);
        _crsTransformFactory = crsTransformFactory;

        using var hdf5 = PureHdfFile.Open(path);
        _dataset = S102DatasetReader.Read(hdf5);
        _source = new S102CoverageSource(_dataset);

        var provider = catalogueManager.GetProvider("S-102");
        _catalogue = new S102PortrayalCatalogue(luaEngine, provider) { FourShades = true };
    }

    public DatasetResult Render(RenderContext? context = null)
    {
        _catalogue.SwitchPalette(context?.Palette ?? PaletteType.Day);
        var metadata = _source.Metadata;

        var viewport = new Pipelines.Viewport
        {
            MinLatitude = metadata.Extent.SouthLatitude,
            MaxLatitude = metadata.Extent.NorthLatitude,
            MinLongitude = metadata.Extent.WestLongitude,
            MaxLongitude = metadata.Extent.EastLongitude,
            WidthPixels = metadata.GridMetadata.NumColumns,
            HeightPixels = metadata.GridMetadata.NumRows,
            ScaleDenominator = 50_000,
        };

        var pipeline = new PortrayalPipeline();
        var layer = pipeline.ProcessAsync(_source, _catalogue, MarinerSettings.Default)
            .GetAwaiter().GetResult();
        var styledLayer = (StyledCoverageLayer)layer;

        var renderer = new MapsuiCoverageRenderer(_crsTransformFactory)
        {
            LayerName = $"S-102: {_fileName}",
        };

        var mapLayer = renderer.Render(styledLayer, viewport);
        var extent = mapLayer.Extent ?? new MRect(0, 0, 0, 0);

        int crs = _dataset.HorizontalCRS ?? 4326;
        var geoId = _dataset.GeographicIdentifier ?? _fileName;
        var info = $"{geoId} — {metadata.GridMetadata.NumColumns}×{metadata.GridMetadata.NumRows} grid, CRS: EPSG:{crs}";

        return new DatasetResult
        {
            Layers = [mapLayer],
            Extent = extent,
            Info = info,
            ProductSpec = "S-102",
        };
    }

    public FeatureInfo? GetFeatureInfo(string featureRef) => null;
}
