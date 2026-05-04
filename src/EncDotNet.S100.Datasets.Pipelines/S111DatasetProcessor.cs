using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S111DatasetProcessor : IDatasetProcessor
{
    private readonly S111CoverageSource _source;
    private readonly S111PortrayalCatalogue _catalogue;
    private readonly ICrsTransformFactory _crsTransformFactory;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly S111Dataset _dataset;
    private readonly string _fileName;

    public string ProductSpec => "S-111";

    /// <summary>Available forecast time steps in this dataset.</summary>
    public IReadOnlyList<DateTime> AvailableTimes => _source.AvailableTimes;

    public S111DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        ICrsTransformFactory crsTransformFactory)
    {
        _fileName = Path.GetFileName(path);
        _crsTransformFactory = crsTransformFactory;

        using var hdf5 = PureHdfFile.Open(path);
        _dataset = S111DatasetReader.Read(hdf5);
        _source = new S111CoverageSource(_dataset);

        _provider = catalogueManager.HasCatalogue("S-111")
            ? catalogueManager.GetProvider("S-111")
            : throw new InvalidOperationException(
                "S-111 portrayal catalogue is not registered. " +
                "Ensure the S-111 portrayal catalogue is loaded before opening S-111 datasets.");
        _catalogue = new S111PortrayalCatalogue(_provider);
    }

    public DatasetResult Render(RenderContext? context = null)
    {
        _catalogue.SwitchPalette(context?.Palette ?? PaletteType.Day);

        // Select the requested time step, defaulting to the first
        DateTime selectedTime;
        if (context is S111RenderContext { TimeStep: { } timeStep })
        {
            _source.SelectTime(timeStep);
            selectedTime = timeStep;
        }
        else
        {
            selectedTime = _source.AvailableTimes[0];
            _source.SelectTime(selectedTime);
        }

        var metadata = _source.Metadata;

        var viewport = new EncDotNet.S100.Pipelines.Viewport
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

        // Color raster layer
        var colorRenderer = new MapsuiCoverageRenderer(_crsTransformFactory)
        {
            LayerName = $"S-111: {_fileName}",
        };
        var colorLayer = colorRenderer.Render(styledLayer, viewport);
        var extent = colorLayer.Extent ?? new MRect(0, 0, 0, 0);

        var layers = new List<ILayer> { colorLayer };

        // Arrow overlay layer
        var arrowRenderer = new MapsuiCoverageArrowRenderer(_crsTransformFactory)
        {
            LayerName = $"S-111 Arrows: {_fileName}",
            Palette = _catalogue.ActivePalette,
            SymbolProvider = symbolName =>
            {
                var item = _provider.Catalogue.Symbols
                    .FirstOrDefault(s => s.Id.Equals(symbolName, StringComparison.OrdinalIgnoreCase));
                if (item is null) return null;

                using var stream = _provider.FetchAssetAsync(item, "Symbols").GetAwaiter().GetResult();
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            },
        };
        var arrowLayer = arrowRenderer.Render(styledLayer, viewport);
        if (arrowLayer is not null)
        {
            layers.Add(arrowLayer);
        }

        int crs = _dataset.HorizontalCRS ?? 4326;
        var geoId = _dataset.GeographicIdentifier ?? _fileName;
        var timeInfo = _source.AvailableTimes.Count > 1
            ? $", time: {selectedTime:u} ({_source.AvailableTimes.Count} steps)"
            : "";
        var info = $"{geoId} — {metadata.GridMetadata.NumColumns}×{metadata.GridMetadata.NumRows} grid, CRS: EPSG:{crs}{timeInfo}";

        return new DatasetResult
        {
            Layers = layers,
            Extent = extent,
            Info = info,
            ProductSpec = "S-111",
            // Stable sub-layer keys; the viewer maps these to
            // localized display names. The processor itself does not
            // depend on UI string resources.
            LayerNames = arrowLayer is not null
                ? new[] { "s111.color-band", "s111.arrows" }
                : new[] { "s111.color-band" },
        };
    }

    public FeatureInfo? GetFeatureInfo(string featureRef) => null;
}
