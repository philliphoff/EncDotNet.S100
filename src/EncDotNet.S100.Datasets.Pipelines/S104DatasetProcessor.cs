using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Pipeline processor for S-104 water-level datasets. Branches between
/// dcf2 (regular grid → coverage layer) and dcf8 (time series at fixed
/// stations → station-glyph point layer; see S-104 Edition 2.0.0
/// §10.2.3 / §10.2.7).
/// </summary>
public sealed class S104DatasetProcessor : IDatasetProcessor
{
    // dcf2 only
    private readonly S104CoverageSource? _source;
    private readonly S104PortrayalCatalogue? _catalogue;

    // dcf8 only
    private readonly S104StationSeriesDataset? _stationSeries;
    private readonly IReadOnlyList<DateTime> _stationTimes = Array.Empty<DateTime>();
    private readonly Dictionary<string, WaterLevelStation> _stationsById = new(StringComparer.Ordinal);

    /// <summary>
    /// Last time-step selected via <see cref="Render"/> for dcf8 station
    /// series. Cached so <see cref="GetFeatureInfo"/> reports the sample
    /// at the same time the rendered glyph is showing. <c>null</c> until
    /// the first render.
    /// </summary>
    private DateTime? _stationSelectedTime;

    /// <summary>
    /// Prefix used on <see cref="MapsuiDisplayListRenderer.FeatureRefKey"/>
    /// tags for dcf8 station-series point features. The remainder is the
    /// station identifier. <see cref="GetFeatureInfo"/> recognises this
    /// prefix to route station picks back through this processor.
    /// </summary>
    internal const string StationFeatureRefPrefix = "station:";

    private readonly ICrsTransformFactory _crsTransformFactory;
    private readonly S104DatasetData _data;
    private readonly string _fileName;

    public SpecRef Spec => new("S-104", default);

    /// <summary>Available forecast time steps in this dataset.</summary>
    public IReadOnlyList<DateTime> AvailableTimes =>
        _source?.AvailableTimes ?? _stationTimes;

    public S104DatasetProcessor(
        string path,
        ICrsTransformFactory crsTransformFactory)
        : this(File.OpenRead(path), Path.GetFileName(path), crsTransformFactory)
    {
    }

    public S104DatasetProcessor(
        IAssetSource source,
        string relativePath,
        ICrsTransformFactory crsTransformFactory)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            crsTransformFactory)
    {
    }

    private S104DatasetProcessor(
        Stream datasetStream,
        string fileName,
        ICrsTransformFactory crsTransformFactory)
    {
        ArgumentNullException.ThrowIfNull(datasetStream);
        _fileName = fileName;
        _crsTransformFactory = crsTransformFactory;

        using (datasetStream)
        using (var hdf5 = PureHdfFile.Open(datasetStream))
        {
            try
            {
                _data = S104DatasetReader.ReadAny(hdf5);
            }
            catch (S100DatasetSchemaException ex) when (ex.File is null)
            {
                throw ex.WithFile(_fileName);
            }
            catch (S100DatasetNotSupportedException ex) when (ex.File is null)
            {
                throw ex.WithFile(_fileName);
            }
        }

        switch (_data)
        {
            case S104DatasetData.GriddedCoverage g:
                _source = new S104CoverageSource(g.Dataset);
                _catalogue = new S104PortrayalCatalogue();
                break;
            case S104DatasetData.StationSeries s:
                _stationSeries = s.Dataset;
                _stationTimes = ComputeStationUnionTimes(s.Dataset);
                foreach (var station in s.Dataset.Stations)
                {
                    _stationsById[station.Identifier] = station;
                }
                break;
        }
    }

    public DatasetResult Render(RenderContext? context = null)
    {
        if (_stationSeries is not null)
        {
            return RenderStationSeries(_stationSeries, context);
        }
        return RenderGridded(context);
    }

    private DatasetResult RenderGridded(RenderContext? context)
    {
        var source = _source!;
        var catalogue = _catalogue!;
        catalogue.SwitchPalette(context?.Palette ?? PaletteType.Day);

        DateTime selectedTime;
        if (context is S104RenderContext { TimeStep: { } timeStep })
        {
            source.SelectTime(timeStep);
            selectedTime = timeStep;
        }
        else
        {
            selectedTime = source.AvailableTimes[0];
            source.SelectTime(selectedTime);
        }

        var metadata = source.Metadata;

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
        var layer = pipeline.ProcessAsync(source, catalogue, context?.Mariner ?? MarinerSettings.Default)
            .GetAwaiter().GetResult();
        var styledLayer = (StyledCoverageLayer)layer;

        var colorRenderer = new MapsuiCoverageRenderer(_crsTransformFactory)
        {
            LayerName = $"S-104: {_fileName}",
        };
        var colorLayer = colorRenderer.Render(styledLayer, viewport);
        var extent = colorLayer.Extent ?? new MRect(0, 0, 0, 0);

        var griddedDataset = ((S104DatasetData.GriddedCoverage)_data).Dataset;
        int crs = griddedDataset.HorizontalCRS ?? 4326;
        var geoId = griddedDataset.GeographicIdentifier ?? _fileName;
        var timeInfo = source.AvailableTimes.Count > 1
            ? $", time: {selectedTime:u} ({source.AvailableTimes.Count} steps)"
            : "";
        var info = $"{geoId} — {metadata.GridMetadata.NumColumns}×{metadata.GridMetadata.NumRows} grid, CRS: EPSG:{crs}{timeInfo}";

        return new DatasetResult
        {
            Layers = [colorLayer],
            Extent = extent,
            Info = info,
            Spec = new SpecRef("S-104", default),
        };
    }

    // ---- dcf8 station series rendering ---------------------------------

    private DatasetResult RenderStationSeries(S104StationSeriesDataset ds, RenderContext? context)
    {
        DateTime selectedTime;
        if (context is S104RenderContext { TimeStep: { } timeStep })
        {
            selectedTime = timeStep;
        }
        else
        {
            selectedTime = ds.MinTime ?? DateTime.UtcNow;
        }

        _stationSelectedTime = selectedTime;

        var nativeToMerc = _crsTransformFactory.Create($"EPSG:{ds.HorizontalCRS ?? 4326}", "EPSG:3857");

        var features = new List<IFeature>(ds.Stations.Count);
        double mercMinX = double.PositiveInfinity, mercMinY = double.PositiveInfinity;
        double mercMaxX = double.NegativeInfinity, mercMaxY = double.NegativeInfinity;

        foreach (var station in ds.Stations)
        {
            double mx, my;
            if (nativeToMerc.IsIdentity)
            {
                mx = station.Longitude;
                my = station.Latitude;
            }
            else
            {
                (mx, my) = nativeToMerc.Transform(station.Longitude, station.Latitude);
            }

            if (mx < mercMinX) mercMinX = mx;
            if (mx > mercMaxX) mercMaxX = mx;
            if (my < mercMinY) mercMinY = my;
            if (my > mercMaxY) mercMaxY = my;

            int idx = station.NearestTimeIndex(selectedTime);
            var height = station.Heights[idx];
            var trend = station.Trends[idx];

            var fill = TrendFill(trend);
            var stroke = ResolveStrokeColor(height, ds.WaterLevelTrendThreshold);

            var feature = new GeometryFeature
            {
                Geometry = new Point(mx, my),
            };
            feature[MapsuiDisplayListRenderer.FeatureRefKey] =
                StationFeatureRefPrefix + station.Identifier;
            feature["StationId"] = station.Identifier;
            feature["WaterLevelHeight"] = height;
            feature["WaterLevelTrend"] = (int)trend;
            feature["WaterLevelTrendLabel"] = DecodeTrend(trend);
            feature["SampleTime"] = station.TimeAt(idx);
            feature["Latitude"] = station.Latitude;
            feature["Longitude"] = station.Longitude;

            feature.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                Fill = new Brush(fill),
                Outline = new Pen(stroke, 1.5),
                SymbolScale = 0.7,
            });

            features.Add(feature);
        }

        var layer = new MemoryLayer
        {
            Name = $"S-104: {_fileName}",
            Features = features,
            Style = null,
        };

        var extent = ds.Stations.Count == 0
            ? new MRect(0, 0, 0, 0)
            : new MRect(mercMinX, mercMinY, mercMaxX, mercMaxY);

        var info = $"{ds.GeographicIdentifier ?? _fileName} — {ds.Stations.Count} stations, " +
                   $"time: {selectedTime:u}";

        return new DatasetResult
        {
            Layers = [layer],
            Extent = extent,
            Info = info,
            Spec = new SpecRef("S-104", default),
        };
    }

    private static IReadOnlyList<DateTime> ComputeStationUnionTimes(S104StationSeriesDataset ds)
    {
        var set = new SortedSet<DateTime>();
        foreach (var s in ds.Stations)
        {
            for (int i = 0; i < s.NumberOfTimes; i++)
            {
                set.Add(DateTime.SpecifyKind(s.TimeAt(i), DateTimeKind.Utc));
            }
        }
        return set.ToArray();
    }

    /// <summary>
    /// Trend → fill colour table (S-104 Edition 2.0.0 §10.2.7 trend
    /// enumeration: 0 unknown, 1 decreasing, 2 increasing, 3 steady).
    /// </summary>
    private static Color TrendFill(byte trend) => trend switch
    {
        1 => new Color(42, 111, 151),    // #2a6f97 descending blue
        2 => new Color(193, 18, 31),     // #c1121f ascending red
        3 => new Color(42, 157, 143),    // #2a9d8f neutral teal
        _ => new Color(128, 128, 128),   // #808080 unknown grey
    };

    private static Color ResolveStrokeColor(float height, double? trendThreshold)
    {
        if (trendThreshold is null) return new Color(0, 0, 0);
        return height >= trendThreshold.Value
            ? new Color(255, 255, 255)
            : new Color(0, 0, 0);
    }

    /// <summary>
    /// Resolves dcf8 station picks routed via the Mapsui
    /// <see cref="MapsuiDisplayListRenderer.FeatureRefKey"/> tag the
    /// glyph layer attaches to each station point. Refs are formatted as
    /// <c>"station:&lt;id&gt;"</c> (see <see cref="StationFeatureRefPrefix"/>).
    /// For dcf2 gridded datasets and other refs this returns <c>null</c>;
    /// callers should fall back to <see cref="GetCoverageInfo"/>.
    /// </summary>
    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        ArgumentNullException.ThrowIfNull(featureRef);

        if (_stationSeries is null) return null;
        if (!featureRef.StartsWith(StationFeatureRefPrefix, StringComparison.Ordinal))
            return null;

        var id = featureRef[StationFeatureRefPrefix.Length..];
        if (!_stationsById.TryGetValue(id, out var station))
            return null;

        return BuildStationFeatureInfo(station, _stationSelectedTime);
    }

    private FeatureInfo BuildStationFeatureInfo(WaterLevelStation station, DateTime? time)
    {
        var selectedTime = time ?? station.StartTime;
        int idx = station.NearestTimeIndex(selectedTime);
        var height = station.Heights[idx];
        var trend = station.Trends[idx];
        var sampleTime = station.TimeAt(idx);

        return new FeatureInfo
        {
            FeatureRef = StationFeatureRefPrefix + station.Identifier,
            FeatureType = "WaterLevel",
            FeatureTypeName = "Water Level (Station)",
            StationSeries = BuildStationSeriesSnapshot(station),
            Attributes = new List<PickAttribute>
            {
                new()
                {
                    Code = "stationIdentification",
                    Name = "Station",
                    RawValue = station.Identifier,
                },
                new()
                {
                    Code = "stationPosition",
                    Name = "Position",
                    RawValue = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:0.######},{1:0.######}",
                        station.Latitude, station.Longitude),
                    DisplayValue = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:0.####}°, {1:0.####}°",
                        station.Latitude, station.Longitude),
                },
                new()
                {
                    Code = "waterLevelHeight",
                    Name = "Water Level Height",
                    RawValue = height.ToString("0.##########", CultureInfo.InvariantCulture),
                    DisplayValue = $"{height.ToString("0.##", CultureInfo.InvariantCulture)} m",
                },
                new()
                {
                    Code = "waterLevelTrend",
                    Name = "Water Level Trend",
                    RawValue = ((int)trend).ToString(CultureInfo.InvariantCulture),
                    DisplayValue = DecodeTrend(trend),
                },
                new()
                {
                    Code = "timePoint",
                    Name = "Time",
                    RawValue = sampleTime.ToString("u", CultureInfo.InvariantCulture),
                    DateTimeValue = sampleTime,
                },
                new()
                {
                    Code = "sampleCount",
                    Name = "Sample Count",
                    RawValue = station.NumberOfTimes.ToString(CultureInfo.InvariantCulture),
                },
                new()
                {
                    Code = "timeRange",
                    Name = "Time Range",
                    RawValue = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:u}/{1:u}",
                        station.StartTime, station.EndTime),
                    DisplayValue = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:u} → {1:u}",
                        station.StartTime, station.EndTime),
                    DateTimeRangeValue = (station.StartTime, station.EndTime),
                },
            },
        };
    }

    private static StationTimeSeriesSnapshot BuildStationSeriesSnapshot(WaterLevelStation station)
    {
        var times = new DateTime[station.NumberOfTimes];
        for (var i = 0; i < station.NumberOfTimes; i++)
            times[i] = station.TimeAt(i);

        return new StationTimeSeriesSnapshot
        {
            StationId = station.Identifier,
            StationName = station.Identifier,
            Latitude = station.Latitude,
            Longitude = station.Longitude,
            Times = times,
            Channels = new[]
            {
                new StationTimeSeriesChannel
                {
                    Key = "waterLevelHeight",
                    DisplayName = "Water Level Height",
                    Unit = "m",
                    Values = station.Heights,
                    // S-104 dcf8 producers commonly use -9999 as the
                    // missing-sample sentinel; viewers filter it out so
                    // the chart doesn't spike.
                    FillValue = -9999f,
                },
            },
        };
    }

    public FeatureInfo? GetCoverageInfo(double latitude, double longitude, DateTime? time)
    {
        if (_stationSeries is not null)
        {
            return GetStationInfo(_stationSeries, latitude, longitude, time);
        }

        var source = _source!;
        if (source.AvailableTimes.Count == 0)
            return null;

        var selectedTime = time ?? source.AvailableTimes[0];
        source.SelectTime(selectedTime);

        var sample = CoveragePickHelper.Sample(source, _crsTransformFactory, latitude, longitude);
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
                DateTimeValue = selectedTime,
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

    private FeatureInfo? GetStationInfo(
        S104StationSeriesDataset ds,
        double latitude,
        double longitude,
        DateTime? time)
    {
        if (ds.Stations.Count == 0) return null;

        // Nearest station via small-angle approximation; the viewer's
        // PickService already supplies pixel-tolerant hit-testing.
        WaterLevelStation? best = null;
        double bestSqDeg = double.PositiveInfinity;
        foreach (var s in ds.Stations)
        {
            var dLat = s.Latitude - latitude;
            var dLon = (s.Longitude - longitude) * Math.Cos(latitude * Math.PI / 180.0);
            var d = dLat * dLat + dLon * dLon;
            if (d < bestSqDeg)
            {
                bestSqDeg = d;
                best = s;
            }
        }
        if (best is null) return null;

        var selectedTime = time ?? best.StartTime;
        int idx = best.NearestTimeIndex(selectedTime);
        var height = best.Heights[idx];
        var trend = best.Trends[idx];
        var sampleTime = best.TimeAt(idx);

        return new FeatureInfo
        {
            FeatureRef = best.Identifier,
            FeatureType = "WaterLevel",
            FeatureTypeName = "Water Level (Station)",
            Attributes = new List<PickAttribute>
            {
                new()
                {
                    Code = "stationIdentification",
                    Name = "Station",
                    RawValue = best.Identifier,
                },
                new()
                {
                    Code = "waterLevelHeight",
                    Name = "Water Level Height",
                    RawValue = height.ToString("0.##########", CultureInfo.InvariantCulture),
                    DisplayValue = $"{height.ToString("0.##", CultureInfo.InvariantCulture)} m",
                },
                new()
                {
                    Code = "waterLevelTrend",
                    Name = "Water Level Trend",
                    RawValue = ((int)trend).ToString(CultureInfo.InvariantCulture),
                    DisplayValue = DecodeTrend(trend),
                },
                new()
                {
                    Code = "timePoint",
                    Name = "Time",
                    RawValue = sampleTime.ToString("u", CultureInfo.InvariantCulture),
                    DateTimeValue = sampleTime,
                },
            },
        };
    }

    private static string DecodeTrend(int code) => code switch
    {
        1 => "Decreasing",
        2 => "Increasing",
        3 => "Steady",
        _ => "Unknown",
    };

    private static string DecodeTrend(byte code) => DecodeTrend((int)code);
}
