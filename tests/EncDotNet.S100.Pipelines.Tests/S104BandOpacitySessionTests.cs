using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The S-104 water-level band is partly transparent by default
/// (<see cref="S104DatasetProcessor.ColorBandOpacity"/>) so the ENC colour
/// fills under it stay readable (S-98 Main §9.2.1; Annex A §4.4.1). The map
/// session must multiply the mariner's dataset opacity with that portrayal
/// opacity — never replace it, and never compound it across repeated
/// display-state updates, including when the S-98 land-mask rule
/// (R-101-104-B) rebuilds the band's layer.
/// </summary>
public sealed class S104BandOpacitySessionTests
{
    private const double BandOpacity = S104DatasetProcessor.ColorBandOpacity;

    [Fact]
    public async Task Band_draws_at_its_portrayal_opacity_times_the_dataset_opacity()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("s104.h5");
        Assert.True(owner.TryRegister(id, new BandProcessor(BandOpacity)));
        session.SetDataset(Dataset(id, "S-104"));
        await session.RenderAsync(id, MapPresentationState.Default);

        Assert.Equal(BandOpacity, Band(session).Opacity, precision: 10);

        // Repeated updates must not compound the portrayal opacity.
        for (var i = 0; i < 3; i++)
        {
            session.SetDataset(Dataset(id, "S-104", opacity: 0.5));
            Assert.Equal(BandOpacity * 0.5, Band(session).Opacity, precision: 10);
        }

        session.SetDataset(Dataset(id, "S-104"));
        Assert.Equal(BandOpacity, Band(session).Opacity, precision: 10);
    }

    [Fact]
    public async Task Opaque_grid_surfaces_stay_opaque()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("s102.h5");
        Assert.True(owner.TryRegister(id, new BandProcessor(opacity: 1.0, productSpec: "S-102")));
        session.SetDataset(Dataset(id, "S-102"));
        await session.RenderAsync(id, MapPresentationState.Default);

        Assert.Equal(1.0, Band(session).Opacity, precision: 10);
    }

    [Fact]
    public async Task Land_masked_band_keeps_its_portrayal_opacity_without_compounding()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var enc = new MapDatasetId("s101-cell.000");
        var band = new MapDatasetId("s104.h5");
        Assert.True(owner.TryRegister(enc, new LandProcessor()));
        Assert.True(owner.TryRegister(band, new BandProcessor(BandOpacity)));
        session.SetDataset(Dataset(enc, "S-101"));
        session.SetDataset(Dataset(band, "S-104"));
        await session.RenderAsync(enc, MapPresentationState.Default);
        await session.RenderAsync(band, MapPresentationState.Default);

        // R-101-104-B attached the ENC land to the band, so the stack holds a
        // rebuilt layer rather than the processor's original one.
        var original = Assert.Single(session.GetDataset(band)!.Layers);
        Assert.NotSame(original, Band(session));
        Assert.Equal(BandOpacity, Band(session).Opacity, precision: 10);

        for (var i = 0; i < 3; i++)
        {
            session.SetDataset(Dataset(band, "S-104", opacity: 0.5));
            Assert.Equal(BandOpacity * 0.5, Band(session).Opacity, precision: 10);
        }
    }

    private static ILayer Band(MapsuiDatasetLayerSession session)
        => Assert.Single(
            session.GetLayerStackEntries(),
            entry => entry.Plane is S98DisplayPlane.OnDemandSurface or S98DisplayPlane.Bathymetry).Layer;

    private static MapsuiDatasetLayerSession CreateSession(Map map, DatasetProcessorOwner owner) =>
        new(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(new IdentityCrsTransformFactory()),
            new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()));

    private static MapDataset Dataset(MapDatasetId id, string productSpec, double opacity = 1.0) =>
        new(
            id,
            id.Value,
            new DatasetMetadata { Spec = new SpecRef(productSpec, new SpecVersion(1, 0, 0)) },
            opacity: opacity);

    /// <summary>A solid 8×4 EPSG:4326 colour band (lon 0..8, lat 0..4).</summary>
    private sealed class BandProcessor(double opacity, string productSpec = "S-104")
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
                NumRows = 4,
                NumColumns = 8,
                OriginLatitude = 0.0,
                OriginLongitude = 0.0,
                SpacingLatitudinal = 1.0,
                SpacingLongitudinal = 1.0,
            };
            var values = new float[metadata.NumRows * metadata.NumColumns];
            Array.Fill(values, 5.0f);
            var styled = new StyledCoverageLayer
            {
                Coverage = new SampledCoverage
                {
                    Region = GridRegion.Full,
                    Metadata = metadata,
                    Values = new Dictionary<string, float[]> { ["waterLevelHeight"] = values },
                },
                NoDataValue = float.NaN,
                Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
                ColorScheme = new CoverageColorScheme
                {
                    FieldName = "waterLevelHeight",
                    Bands = [new ColorBand { MinValue = 0f, MaxValue = 10f, Color = "#FF0000" }],
                },
            };
            var plane = productSpec == "S-102" ? S98DisplayPlane.Bathymetry : S98DisplayPlane.OnDemandSurface;
            return Task.FromResult(new CoveragePortrayalResult
            {
                SubLayers =
                [
                    new GridCoverageSubLayer
                    {
                        LayerKey = "band",
                        LayerName = "band",
                        Plane = plane,
                        Coverage = styled,
                        Viewport = new Viewport
                        {
                            MinLatitude = 0.0,
                            MaxLatitude = 4.0,
                            MinLongitude = 0.0,
                            MaxLongitude = 8.0,
                            WidthPixels = 8,
                            HeightPixels = 4,
                            ScaleDenominator = 50_000,
                        },
                        Opacity = opacity,
                    },
                ],
                Spec = Spec,
                SourceDatasetId = productSpec == "S-102" ? "s102.h5" : "s104.h5",
                Info = "test",
            });
        }
    }

    /// <summary>An S-101 cell whose only feature is a LandArea over the band's west half.</summary>
    private sealed class LandProcessor : IDatasetProcessor, IVectorPortrayalSource
    {
        public SpecRef Spec => new("S-101", new SpecVersion(1, 0, 0));

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public Task<VectorPortrayalResult> BuildVectorPortrayalAsync(
            RenderContext? context = null,
            CancellationToken cancellationToken = default)
        {
            const string token = "LANDA";
            return Task.FromResult(new VectorPortrayalResult
            {
                SubLayers =
                [
                    new VectorSubLayer
                    {
                        LayerKey = "s101.areas",
                        LayerName = "S-101 (areas)",
                        Instructions = [new AreaInstruction { FeatureReference = "1", FillColor = token }],
                        Plane = S98DisplayPlane.BaseChartUnder,
                        SourceFeatureType = "area",
                    },
                ],
                Palette = new ColorPalette("test", new Dictionary<string, string> { [token] = "#00FF00" }),
                GeometryProvider = new LandGeometry(),
                Product = "S-101",
                Spec = Spec,
                SourceDatasetId = "s101-cell.000",
                Info = "test",
                FeatureTags = new Dictionary<long, VectorFeatureTag> { [1] = new VectorFeatureTag("LandArea", null) },
            });
        }

        private sealed class LandGeometry : IFeatureGeometryProvider
        {
            private static readonly FeatureGeometry Land = new()
            {
                Type = GeometryType.Surface,
                Coordinates =
                [
                    new GeoPosition(-1.0, -1.0),
                    new GeoPosition(-1.0, 3.5),
                    new GeoPosition(5.0, 3.5),
                    new GeoPosition(5.0, -1.0),
                    new GeoPosition(-1.0, -1.0),
                ],
            };

            public FeatureGeometry? GetGeometry(string featureReference)
                => featureReference == "1" ? Land : null;
        }
    }
}
