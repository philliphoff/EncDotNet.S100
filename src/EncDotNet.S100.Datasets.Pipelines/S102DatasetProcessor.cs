using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting;
using Mapsui;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S102DatasetProcessor : IDatasetProcessor
{
    private readonly S102Dataset _dataset;
    private readonly S102CoverageSource _source;
    private readonly S102PortrayalCatalogue _catalogue;
    private readonly ICrsTransformFactory _crsTransformFactory;
    private readonly string _fileName;

    public SpecRef Spec => new("S-102", default);

    public S102DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        ICrsTransformFactory crsTransformFactory)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, luaEngine, crsTransformFactory)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S102DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/> (e.g. a <c>FileSystemAssetSource</c> or
    /// <c>ZipAssetSource</c>). Used by exchange-set bulk loading where
    /// a dataset's bytes live inside a ZIP archive.
    /// </summary>
    public S102DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        ICrsTransformFactory crsTransformFactory)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            luaEngine,
            crsTransformFactory)
    {
    }

    private S102DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        ILuaEngine luaEngine,
        ICrsTransformFactory crsTransformFactory)
    {
        ArgumentNullException.ThrowIfNull(datasetStream);
        _fileName = fileName;
        _crsTransformFactory = crsTransformFactory;

        using (datasetStream)
        using (var hdf5 = PureHdfFile.Open(datasetStream))
        {
            _dataset = S102DatasetReader.Read(hdf5);
        }
        _source = new S102CoverageSource(_dataset);

        var provider = catalogueManager.GetProvider("S-102");
        _catalogue = new S102PortrayalCatalogue(luaEngine, provider) { FourShades = true };

        Diagnostics.CatalogueResolutionDiagnostics.Report(this, Spec, _catalogue.CatalogueRef, "portrayal");
    }

    public DatasetResult Render(RenderContext? context = null)
    {
        _catalogue.SwitchPalette(context?.Palette ?? PaletteType.Day);
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
        var layer = pipeline.ProcessAsync(_source, _catalogue, context?.Mariner ?? MarinerSettings.Default)
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
            Spec = new SpecRef("S-102", default),
        };
    }

    public FeatureInfo? GetFeatureInfo(string featureRef) => null;

    /// <summary>
    /// Samples the bathymetric surface at the supplied geographic
    /// position. Returns a synthetic feature carrying depth and
    /// uncertainty pick attributes; the <paramref name="time"/> argument
    /// is ignored because S-102 surfaces are time-invariant.
    /// </summary>
    /// <remarks>
    /// NoData cells (S-100 Part 10c §11; S-102 sentinel
    /// <c>1_000_000f</c>) yield <c>"—"</c> for the affected attribute
    /// rather than the raw fill value. Out-of-extent clicks return
    /// <c>null</c>.
    /// </remarks>
    public FeatureInfo? GetCoverageInfo(double latitude, double longitude, DateTime? time)
    {
        var sample = CoveragePickHelper.Sample(_source, _crsTransformFactory, latitude, longitude);
        if (sample is null)
            return null;

        var depth = sample.Values.TryGetValue("depth", out var d) ? d : sample.NoDataValue;
        var uncertainty = sample.Values.TryGetValue("uncertainty", out var u) ? u : sample.NoDataValue;
        var attrs = new List<PickAttribute>
        {
            new()
            {
                Code = "depth",
                Name = "Depth",
                RawValue = FormatFloat(depth, sample.NoDataValue),
                DisplayValue = depth == sample.NoDataValue ? "—" : $"{depth.ToString("0.##", CultureInfo.InvariantCulture)} m",
            },
            new()
            {
                Code = "uncertainty",
                Name = "Uncertainty",
                RawValue = FormatFloat(uncertainty, sample.NoDataValue),
                DisplayValue = uncertainty == sample.NoDataValue ? "—" : $"{uncertainty.ToString("0.##", CultureInfo.InvariantCulture)} m",
            },
        };

        return new FeatureInfo
        {
            FeatureRef = $"({sample.Row},{sample.Col})",
            FeatureType = "BathymetryCoverage",
            FeatureTypeName = "Bathymetry Coverage",
            Attributes = attrs,
        };
    }

    private static string FormatFloat(float value, float noData)
        => value == noData
            ? "NoData"
            : value.ToString("0.##########", CultureInfo.InvariantCulture);
}
