using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// A native S-101 cell must keep drawing at an in-band scale while S-102 and
/// S-111 datasets are loaded, hidden, shown again and closed beside it. The
/// viewer used to render only the basemap there: R-101-102-B suppressed every
/// S-101 depth area and contour as soon as any S-102 was visible, wherever it
/// was, and the suppressed layer lost its tiled scene, so it painted only its
/// pick targets. Built from the committed IHO cell 101AA00DS0020 and stub
/// coverages.
/// </summary>
public sealed class S101WithS102SessionTests
{
    private const string Cell = "101AA00DS0020.000";
    private const int InBandScaleDenominator = 50000;

    private static readonly MapDatasetId S101Id = new(Cell);
    private static readonly MapDatasetId S102Id = new("s102.h5");
    private static readonly MapDatasetId S111Id = new("s111.h5");

    [Fact]
    public async Task S101_keeps_its_depth_shading_beside_an_s102_elsewhere_through_toggles_and_a_close()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        await LoadS101Async(session, owner);
        var alone = SceneOpCounts(session);

        // An S-102 tile far from the cell, and an S-111 beside it.
        await LoadCoverageAsync(session, owner, S102Id, "S-102", new GeographicBounds(100.0, 10.0, 100.5, 10.5));
        await LoadCoverageAsync(session, owner, S111Id, "S-111", new GeographicBounds(100.0, 10.0, 100.5, 10.5));
        ToggleAndClose(session);

        // Nothing of the cell lies inside the S-102 extent, so nothing of it is
        // suppressed, and its layers still tile-render pixels in band.
        Assert.Equal(alone, SceneOpCounts(session));
        Assert.True(PaintedPixelsInBand(session) > 0, "The S-101 cell rasterised no pixels in band.");
    }

    [Fact]
    public async Task S101_layers_stay_tile_rendered_when_an_s102_over_the_cell_suppresses_its_depth_features()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var cellBounds = await LoadS101Async(session, owner);
        var alone = SceneOpCounts(session);

        await LoadCoverageAsync(session, owner, S102Id, "S-102", cellBounds);
        await LoadCoverageAsync(session, owner, S111Id, "S-111", cellBounds);
        ToggleAndClose(session);

        // The S-102 covers the whole cell, so its depth features go, but the
        // rest of the cell keeps drawing through the tiled renderer.
        var suppressed = SceneOpCounts(session);
        Assert.True(suppressed.Values.Sum() < alone.Values.Sum(), "No S-101 depth feature was suppressed.");
        Assert.True(suppressed.Values.Sum() > 0, "Suppression removed every S-101 paint op.");
        Assert.True(PaintedPixelsInBand(session) > 0, "The suppressed S-101 cell rasterised no pixels in band.");
    }

    /// <summary>Hides and shows the S-102 and S-111 datasets, then closes the S-111.</summary>
    private static void ToggleAndClose(MapsuiDatasetLayerSession session)
    {
        foreach (var (id, spec) in new[] { (S102Id, "S-102"), (S111Id, "S-111") })
        {
            session.SetDataset(Dataset(id, spec, isVisible: false));
            session.SetDataset(Dataset(id, spec));
        }
        Assert.True(session.RemoveDataset(S111Id));
    }

    private static async Task<GeographicBounds> LoadS101Async(
        MapsuiDatasetLayerSession session, DatasetProcessorOwner owner)
    {
        var processor = CreateFactory().CreateProcessor(Path.Combine(ResolveFixtureDirectory(), Cell));
        Assert.True(owner.TryRegister(S101Id, processor));
        session.SetDataset(Dataset(S101Id, "S-101"));
        var result = await session.RenderAsync(S101Id, MapPresentationState.Default);
        Assert.NotNull(result);

        var (west, south) = SphericalMercator.ToLonLat(result.Extent.MinX, result.Extent.MinY);
        var (east, north) = SphericalMercator.ToLonLat(result.Extent.MaxX, result.Extent.MaxY);
        return new GeographicBounds(west, south, east, north);
    }

    private static async Task LoadCoverageAsync(
        MapsuiDatasetLayerSession session,
        DatasetProcessorOwner owner,
        MapDatasetId id,
        string spec,
        GeographicBounds extent)
    {
        Assert.True(owner.TryRegister(id, new CoverageProcessor(id.Value, spec, extent)));
        session.SetDataset(Dataset(id, spec));
        await session.RenderAsync(id, MapPresentationState.Default);
    }

    /// <summary>The paint-op count of each tiled S-101 layer in the projected stack, by layer name.</summary>
    private static Dictionary<string, int> SceneOpCounts(MapsuiDatasetLayerSession session)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var layer in S101Layers(session))
        {
            Assert.Equal(S100VectorTileRenderer.RendererName, layer.CustomLayerRendererName);
            Assert.True(
                S100VectorTileRenderer.TryGetPartitionedScene(layer, out var baseScene, out var overlayScene),
                $"No scene is bound to '{layer.Name}'.");
            counts[layer.Name] = baseScene.Ops.Count + overlayScene.Ops.Count;
        }
        Assert.NotEmpty(counts);
        return counts;
    }

    private static IEnumerable<ILayer> S101Layers(MapsuiDatasetLayerSession session) =>
        session.GetLayerStackEntries()
            .Where(entry => entry.SourceDatasetId == S101Id.Value)
            .Select(entry => entry.Layer);

    /// <summary>
    /// Rasterises the base-plane tiles over the cell at an in-band scale, as the
    /// tile workers would, for every S-101 layer the map shows at that scale.
    /// </summary>
    private static long PaintedPixelsInBand(MapsuiDatasetLayerSession session)
    {
        var extent = session.GetDataset(S101Id)!.Extent!;
        var latitudeRadians = MapsuiDisplayListRenderer.WebMercatorYToLatitudeRadians(
            (extent.MinY + extent.MaxY) / 2.0);
        var resolution = MapsuiDisplayListRenderer.DenominatorToResolution(InBandScaleDenominator, latitudeRadians);
        var band = TileGrid.BandForResolution(resolution);
        var tiles = TileGrid.VisibleTiles(
            (extent.MinX + extent.MaxX) / 2.0,
            (extent.MinY + extent.MaxY) / 2.0,
            extent.Width / resolution,
            extent.Height / resolution,
            resolution,
            band);

        long painted = 0;
        foreach (var layer in S101Layers(session))
        {
            if (!layer.Enabled || layer.MinVisible > resolution || resolution > layer.MaxVisible
                || !S100VectorTileRenderer.TryGetPartitionedScene(layer, out var baseScene, out _))
            {
                continue;
            }

            foreach (var key in tiles)
            {
                using var bitmap = S100VectorTileRenderer.RasterizeTile(baseScene, baseIndex: null, key, deviceScale: 1f);
                painted += bitmap.Pixels.Count(static p => p.Alpha != 0);
            }
        }
        return painted;
    }

    private static MapsuiDatasetLayerSession CreateSession(Map map, DatasetProcessorOwner owner) =>
        new(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(new ProjNetCrsTransformFactory()),
            new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()));

    private static MapDataset Dataset(MapDatasetId id, string productSpec, bool isVisible = true) =>
        new(
            id,
            id.Value,
            new DatasetMetadata { Spec = new SpecRef(productSpec, new SpecVersion(1, 0, 0)) },
            isVisible);

    private static DatasetPipelineFactory CreateFactory()
    {
        var catalogueManager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                catalogueManager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        return new DatasetPipelineFactory(
            catalogueManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue),
            new DisplayPlaneAuthorityProvider());
    }

    /// <summary>Walks up from the test assembly to find the committed S-101 fixture directory.</summary>
    private static string ResolveFixtureDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", "S101", "S-101", "DATASET_FILES");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Committed S-101 fixture directory not found.");
    }

    /// <summary>
    /// A solid 2×2 EPSG:4326 coverage over <paramref name="extent"/> that
    /// reports it as its <see cref="CoveragePortrayalResult.CoverageExtent"/>,
    /// as the S-102 processor does.
    /// </summary>
    private sealed class CoverageProcessor(string datasetId, string productSpec, GeographicBounds extent)
        : IDatasetProcessor, ICoveragePortrayalSource
    {
        public SpecRef Spec => new(productSpec, new SpecVersion(1, 0, 0));

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public Task<CoveragePortrayalResult> BuildCoveragePortrayalAsync(
            RenderContext? context = null,
            CancellationToken cancellationToken = default)
        {
            var metadata = new GridMetadata
            {
                NumRows = 2,
                NumColumns = 2,
                OriginLatitude = extent.MinLatitude,
                OriginLongitude = extent.MinLongitude,
                SpacingLatitudinal = (extent.MaxLatitude - extent.MinLatitude) / 2.0,
                SpacingLongitudinal = (extent.MaxLongitude - extent.MinLongitude) / 2.0,
            };
            var styled = new StyledCoverageLayer
            {
                Coverage = new SampledCoverage
                {
                    Region = GridRegion.Full,
                    Metadata = metadata,
                    Values = new Dictionary<string, float[]> { ["value"] = [5f, 5f, 5f, 5f] },
                },
                NoDataValue = float.NaN,
                Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
                ColorScheme = new CoverageColorScheme
                {
                    FieldName = "value",
                    Bands = [new ColorBand { MinValue = 0f, MaxValue = 10f, Color = "#3366FF" }],
                },
            };
            var plane = productSpec == "S-102" ? S98DisplayPlane.Bathymetry : S98DisplayPlane.OnDemandSurface;
            return Task.FromResult(new CoveragePortrayalResult
            {
                SubLayers =
                [
                    new GridCoverageSubLayer
                    {
                        LayerKey = "surface",
                        LayerName = $"{datasetId} surface",
                        Plane = plane,
                        Coverage = styled,
                        Viewport = new Viewport
                        {
                            MinLatitude = extent.MinLatitude,
                            MaxLatitude = extent.MaxLatitude,
                            MinLongitude = extent.MinLongitude,
                            MaxLongitude = extent.MaxLongitude,
                            WidthPixels = 2,
                            HeightPixels = 2,
                            ScaleDenominator = InBandScaleDenominator,
                        },
                    },
                ],
                Spec = Spec,
                SourceDatasetId = datasetId,
                Info = "test",
                CoverageExtent = extent,
            });
        }
    }
}
