using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Pipeline processor for S-111 surface-currents datasets. Branches
/// between dcf2 (regular grid → coverage + arrow layers, see existing
/// portrayal catalogue) and dcf8 (time series at fixed stations →
/// station-arrow point layer; see S-111 Edition 2.0.0 §10.2.3 /
/// §10.2.7).
/// </summary>
public sealed class S111DatasetProcessor : IDatasetProcessor
{
    // dcf2 only
    private readonly S111CoverageSource? _source;
    private readonly S111PortrayalCatalogue? _catalogue;
    private readonly PortrayalCatalogueProvider? _provider;
    private readonly S111Dataset? _dataset;

    // dcf8 only
    private readonly S111StationSeriesDataset? _stationSeries;
    private readonly IReadOnlyList<DateTime> _stationTimes = Array.Empty<DateTime>();
    private readonly Dictionary<string, SurfaceCurrentStation> _stationsById = new(StringComparer.Ordinal);

    /// <summary>
    /// Last time-step selected via <see cref="Render"/> for dcf8 station
    /// series. Cached so <see cref="GetFeatureInfo"/> reports the sample
    /// at the same time the rendered arrow is showing. <c>null</c> until
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
    private readonly S111DatasetData _data;
    private readonly string _fileName;

    public SpecRef Spec => new("S-111", default);

    /// <summary>Available forecast time steps in this dataset.</summary>
    public IReadOnlyList<DateTime> AvailableTimes =>
        _source?.AvailableTimes ?? _stationTimes;

    public S111DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        ICrsTransformFactory crsTransformFactory)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, crsTransformFactory)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S111DatasetProcessor"/> by reading
    /// the HDF5 dataset <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S111DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        ICrsTransformFactory crsTransformFactory)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            crsTransformFactory)
    {
    }

    private S111DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
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
                _data = S111DatasetReader.ReadAny(hdf5);
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
            case S111DatasetData.GriddedCoverage g:
                _dataset = g.Dataset;
                _source = new S111CoverageSource(g.Dataset);
                _provider = catalogueManager.HasCatalogue("S-111")
                    ? catalogueManager.GetProvider("S-111")
                    : throw new InvalidOperationException(
                        "S-111 portrayal catalogue is not registered. " +
                        "Ensure the S-111 portrayal catalogue is loaded before opening S-111 datasets.");
                _catalogue = new S111PortrayalCatalogue(_provider);
                Diagnostics.CatalogueResolutionDiagnostics.Report(this, Spec, _catalogue.CatalogueRef, "portrayal");
                break;
            case S111DatasetData.StationSeries s:
                _stationSeries = s.Dataset;
                _stationTimes = ComputeStationUnionTimes(s.Dataset);
                foreach (var station in s.Dataset.Stations)
                {
                    _stationsById[station.Identifier] = station;
                }
                // dcf8 uses an inline arrow glyph — no portrayal
                // catalogue required.
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
        var provider = _provider!;
        catalogue.SwitchPalette(context?.Palette ?? PaletteType.Day);

        DateTime selectedTime;
        if (context is S111RenderContext { TimeStep: { } timeStep })
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
            LayerName = $"S-111: {_fileName}",
        };
        var colorLayer = colorRenderer.Render(styledLayer, viewport);
        var extent = colorLayer.Extent ?? new MRect(0, 0, 0, 0);

        var layers = new List<ILayer> { colorLayer };

        var arrowRenderer = new MapsuiCoverageArrowRenderer(_crsTransformFactory)
        {
            LayerName = $"S-111 Arrows: {_fileName}",
            Palette = catalogue.ActivePalette,
            SymbolProvider = symbolName =>
            {
                var item = provider.Catalogue.Symbols
                    .FirstOrDefault(s => s.Id.Equals(symbolName, StringComparison.OrdinalIgnoreCase));
                if (item is null) return null;

                using var stream = provider.FetchAssetAsync(item, "Symbols").GetAwaiter().GetResult();
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            },
        };
        var arrowLayer = arrowRenderer.Render(styledLayer, viewport);
        if (arrowLayer is not null)
        {
            layers.Add(arrowLayer);
        }

        int crs = _dataset!.HorizontalCRS ?? 4326;
        var geoId = _dataset.GeographicIdentifier ?? _fileName;
        var timeInfo = source.AvailableTimes.Count > 1
            ? $", time: {selectedTime:u} ({source.AvailableTimes.Count} steps)"
            : "";
        var info = $"{geoId} — {metadata.GridMetadata.NumColumns}×{metadata.GridMetadata.NumRows} grid, CRS: EPSG:{crs}{timeInfo}";

        return new DatasetResult
        {
            Layers = layers,
            Extent = extent,
            Info = info,
            Spec = new SpecRef("S-111", default),
            LayerNames = arrowLayer is not null
                ? new[] { "s111.color-band", "s111.arrows" }
                : new[] { "s111.color-band" },
        };
    }

    // ---- dcf8 station series rendering ---------------------------------

    /// <summary>
    /// Projects each station to a single point feature with an arrow
    /// glyph oriented along <c>DirectionsDegreesTrue</c> and scaled by
    /// speed magnitude. Rebuilt per <see cref="S111RenderContext.TimeStep"/>.
    /// </summary>
    private DatasetResult RenderStationSeries(S111StationSeriesDataset ds, RenderContext? context)
    {
        DateTime selectedTime;
        if (context is S111RenderContext { TimeStep: { } timeStep })
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
            var speed = station.SpeedsMetresPerSecond[idx];
            var direction = station.DirectionsDegreesTrue[idx];

            var feature = new GeometryFeature
            {
                Geometry = new Point(mx, my),
            };
            feature[MapsuiDisplayListRenderer.FeatureRefKey] =
                StationFeatureRefPrefix + station.Identifier;
            feature["StationId"] = station.Identifier;
            feature["SpeedMetresPerSecond"] = speed;
            feature["SpeedKnots"] = speed * 1.9438444924406046;
            feature["DirectionDegreesTrue"] = direction;
            feature["SampleTime"] = station.TimeAt(idx);
            feature["Latitude"] = station.Latitude;
            feature["Longitude"] = station.Longitude;

            var arrowColour = ColorByMagnitude(speed);

            // Symbol orientation in Mapsui follows screen rotation
            // (counter-clockwise positive); compass bearing increases
            // clockwise from north, so negate. Geographic north on
            // screen is "up", which corresponds to a zero-rotation
            // arrow whose default orientation we treat as pointing up.
            feature.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Triangle,
                Fill = new Brush(arrowColour),
                Outline = new Pen(arrowColour, 1.0),
                SymbolScale = SymbolScaleForSpeed(speed),
                SymbolRotation = -direction,
            });

            features.Add(feature);
        }

        var layer = new MemoryLayer
        {
            Name = $"S-111: {_fileName}",
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
            Spec = new SpecRef("S-111", default),
        };
    }

    private static IReadOnlyList<DateTime> ComputeStationUnionTimes(S111StationSeriesDataset ds)
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
    /// Graduated Beaufort-style palette across 0..2.0 m/s with a hot
    /// overflow above 2.0 m/s.
    /// </summary>
    private static Color ColorByMagnitude(float speedMetresPerSecond)
    {
        if (float.IsNaN(speedMetresPerSecond) || speedMetresPerSecond < 0.25f)
            return new Color(0xcf, 0xe2, 0xf3); // very pale blue
        if (speedMetresPerSecond < 0.50f) return new Color(0x6f, 0xa8, 0xdc);
        if (speedMetresPerSecond < 1.00f) return new Color(0x3d, 0x85, 0xc6);
        if (speedMetresPerSecond < 1.50f) return new Color(0x1c, 0x45, 0x87);
        if (speedMetresPerSecond < 2.00f) return new Color(0xa6, 0x4d, 0x79);
        return new Color(0xc1, 0x12, 0x1f);
    }

    /// <summary>
    /// Maps a current speed to a Mapsui <see cref="SymbolStyle.SymbolScale"/>
    /// with a visible floor (~6 px) and ceiling (~24 px at 2 m/s).
    /// </summary>
    private static double SymbolScaleForSpeed(float speedMetresPerSecond)
    {
        const double minScale = 0.30; // ~6 px at default symbol size
        const double maxScale = 1.20; // ~24 px
        const double fastReference = 2.0;
        if (float.IsNaN(speedMetresPerSecond) || speedMetresPerSecond <= 0) return minScale;
        var t = Math.Clamp(speedMetresPerSecond / fastReference, 0.0, 1.0);
        return minScale + (maxScale - minScale) * t;
    }

    /// <summary>
    /// Resolves dcf8 station picks routed via the Mapsui
    /// <see cref="MapsuiDisplayListRenderer.FeatureRefKey"/> tag the
    /// arrow layer attaches to each station point. Refs are formatted as
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

    private FeatureInfo BuildStationFeatureInfo(SurfaceCurrentStation station, DateTime? time)
    {
        var selectedTime = time ?? station.StartTime;
        int idx = station.NearestTimeIndex(selectedTime);
        var speed = station.SpeedsMetresPerSecond[idx];
        var direction = station.DirectionsDegreesTrue[idx];
        var sampleTime = station.TimeAt(idx);
        var speedKnots = speed * 1.9438444924406046;

        return new FeatureInfo
        {
            FeatureRef = StationFeatureRefPrefix + station.Identifier,
            FeatureType = "SurfaceCurrent",
            FeatureTypeName = "Surface Current (Station)",
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
                    Code = "surfaceCurrentSpeed",
                    Name = "Current Speed",
                    RawValue = speed.ToString("0.##########", CultureInfo.InvariantCulture),
                    DisplayValue = $"{speed.ToString("0.##", CultureInfo.InvariantCulture)} m/s ({speedKnots.ToString("0.##", CultureInfo.InvariantCulture)} kn)",
                },
                new()
                {
                    Code = "surfaceCurrentDirection",
                    Name = "Current Direction (going to)",
                    RawValue = direction.ToString("0.##########", CultureInfo.InvariantCulture),
                    DisplayValue = $"{direction.ToString("0.#", CultureInfo.InvariantCulture)}°",
                },
                new()
                {
                    Code = "timePoint",
                    Name = "Time",
                    RawValue = sampleTime.ToString("u", CultureInfo.InvariantCulture),
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
                },
            },
        };
    }

    /// <summary>
    /// Samples the surface-current grid at the supplied geographic
    /// position and time. <paramref name="time"/> is matched to the
    /// nearest available time step; when <c>null</c> the first
    /// available step is used. The reported direction is "going to"
    /// in degrees true (S-111 Edition 2.0.0 §10.2).
    /// </summary>
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

        var speed = sample.Values.TryGetValue("surfaceCurrentSpeed", out var s) ? s : sample.NoDataValue;
        var direction = sample.Values.TryGetValue("surfaceCurrentDirection", out var dir) ? dir : sample.NoDataValue;

        var speedUnit = source.Metadata.ValueFields
            .FirstOrDefault(f => string.Equals(f.Name, "surfaceCurrentSpeed", StringComparison.Ordinal))
            ?.Units ?? "knots";

        var attrs = new List<PickAttribute>
        {
            new()
            {
                Code = "surfaceCurrentSpeed",
                Name = "Current Speed",
                RawValue = speed == sample.NoDataValue
                    ? "NoData"
                    : speed.ToString("0.##########", CultureInfo.InvariantCulture),
                DisplayValue = speed == sample.NoDataValue
                    ? "—"
                    : $"{speed.ToString("0.##", CultureInfo.InvariantCulture)} {speedUnit}",
            },
            new()
            {
                Code = "surfaceCurrentDirection",
                Name = "Current Direction (going to)",
                RawValue = direction == sample.NoDataValue
                    ? "NoData"
                    : direction.ToString("0.##########", CultureInfo.InvariantCulture),
                DisplayValue = direction == sample.NoDataValue
                    ? "—"
                    : $"{direction.ToString("0.#", CultureInfo.InvariantCulture)}°",
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
            FeatureType = "SurfaceCurrent",
            FeatureTypeName = "Surface Current",
            Attributes = attrs,
        };
    }

    private FeatureInfo? GetStationInfo(
        S111StationSeriesDataset ds,
        double latitude,
        double longitude,
        DateTime? time)
    {
        if (ds.Stations.Count == 0) return null;

        SurfaceCurrentStation? best = null;
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
        var speed = best.SpeedsMetresPerSecond[idx];
        var direction = best.DirectionsDegreesTrue[idx];
        var sampleTime = best.TimeAt(idx);
        var speedKnots = speed * 1.9438444924406046;

        return new FeatureInfo
        {
            FeatureRef = best.Identifier,
            FeatureType = "SurfaceCurrent",
            FeatureTypeName = "Surface Current (Station)",
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
                    Code = "surfaceCurrentSpeed",
                    Name = "Current Speed",
                    RawValue = speed.ToString("0.##########", CultureInfo.InvariantCulture),
                    DisplayValue = $"{speed.ToString("0.##", CultureInfo.InvariantCulture)} m/s ({speedKnots.ToString("0.##", CultureInfo.InvariantCulture)} kn)",
                },
                new()
                {
                    Code = "surfaceCurrentDirection",
                    Name = "Current Direction (going to)",
                    RawValue = direction.ToString("0.##########", CultureInfo.InvariantCulture),
                    DisplayValue = $"{direction.ToString("0.#", CultureInfo.InvariantCulture)}°",
                },
                new()
                {
                    Code = "timePoint",
                    Name = "Time",
                    RawValue = sampleTime.ToString("u", CultureInfo.InvariantCulture),
                },
            },
        };
    }
}
