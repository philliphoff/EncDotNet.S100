using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S104DatasetProcessor : IDatasetProcessor
{
    private readonly S104CoverageSource _source;
    private readonly S104PortrayalCatalogue _catalogue;
    private readonly ICrsTransformFactory _crsTransformFactory;
    private readonly S104Dataset _dataset;
    private readonly string _fileName;

    public string ProductSpec => "S-104";

    /// <summary>Available forecast time steps in this dataset.</summary>
    public IReadOnlyList<DateTime> AvailableTimes => _source.AvailableTimes;

    public S104DatasetProcessor(
        string path,
        ICrsTransformFactory crsTransformFactory)
    {
        _fileName = Path.GetFileName(path);
        _crsTransformFactory = crsTransformFactory;

        using var hdf5 = PureHdfFile.Open(path);
        _dataset = S104DatasetReader.Read(hdf5);
        _source = new S104CoverageSource(_dataset);
        _catalogue = new S104PortrayalCatalogue();
    }

    public DatasetResult Render(RenderContext? context = null)
    {
        _catalogue.SwitchPalette(context?.Palette ?? PaletteType.Day);

        // Select the requested time step, defaulting to the first
        DateTime selectedTime;
        if (context is S104RenderContext { TimeStep: { } timeStep })
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

        var colorRenderer = new MapsuiCoverageRenderer(_crsTransformFactory)
        {
            LayerName = $"S-104: {_fileName}",
        };
        var colorLayer = colorRenderer.Render(styledLayer, viewport);
        var extent = colorLayer.Extent ?? new MRect(0, 0, 0, 0);

        int crs = _dataset.HorizontalCRS ?? 4326;
        var geoId = _dataset.GeographicIdentifier ?? _fileName;
        var timeInfo = _source.AvailableTimes.Count > 1
            ? $", time: {selectedTime:u} ({_source.AvailableTimes.Count} steps)"
            : "";
        var info = $"{geoId} — {metadata.GridMetadata.NumColumns}×{metadata.GridMetadata.NumRows} grid, CRS: EPSG:{crs}{timeInfo}";

        return new DatasetResult
        {
            Layers = [colorLayer],
            Extent = extent,
            Info = info,
            ProductSpec = "S-104",
        };
    }

    public FeatureInfo? GetFeatureInfo(string featureRef) => null;

    /// <summary>
    /// Samples the water-level grid at the supplied geographic
    /// position and time. <paramref name="time"/> is matched to the
    /// nearest available time step (see
    /// <see cref="S104CoverageSource.SelectTime"/>); when <c>null</c>
    /// the first available step is used.
    /// </summary>
    /// <remarks>
    /// <see cref="S104.WaterLevelValue.Trend"/> is decoded against the
    /// S-104 Edition 2.0.0 trend enumeration (0 nodata, 1 decreasing,
    /// 2 increasing, 3 steady).
    /// </remarks>
    public FeatureInfo? GetCoverageInfo(double latitude, double longitude, DateTime? time)
    {
        if (_source.AvailableTimes.Count == 0)
            return null;

        var selectedTime = time ?? _source.AvailableTimes[0];
        _source.SelectTime(selectedTime);

        var sample = CoveragePickHelper.Sample(_source, _crsTransformFactory, latitude, longitude);
        if (sample is null)
            return null;

        var height = sample.Values.TryGetValue("waterLevelHeight", out var h) ? h : sample.NoDataValue;
        var trend = sample.Values.TryGetValue("waterLevelTrend", out var t) ? t : 0f;

        var attrs = new List<PickAttribute>
        {
            new()
            {
                Code = "waterLevelHeight",
                Name = "Water Level Height",
                RawValue = height == sample.NoDataValue
                    ? "NoData"
                    : height.ToString("0.##########", CultureInfo.InvariantCulture),
                DisplayValue = height == sample.NoDataValue
                    ? "—"
                    : $"{height.ToString("0.##", CultureInfo.InvariantCulture)} m",
            },
            new()
            {
                Code = "waterLevelTrend",
                Name = "Water Level Trend",
                RawValue = ((int)trend).ToString(CultureInfo.InvariantCulture),
                DisplayValue = DecodeTrend((int)trend),
            },
            new()
            {
                Code = "timePoint",
                Name = "Time",
                RawValue = selectedTime.ToString("u", CultureInfo.InvariantCulture),
            },
        };

        return new FeatureInfo
        {
            FeatureRef = $"({sample.Row},{sample.Col})",
            FeatureType = "WaterLevel",
            FeatureTypeName = "Water Level",
            Attributes = attrs,
        };
    }

    private static string DecodeTrend(int code) => code switch
    {
        1 => "Decreasing",
        2 => "Increasing",
        3 => "Steady",
        _ => "Unknown",
    };
}
