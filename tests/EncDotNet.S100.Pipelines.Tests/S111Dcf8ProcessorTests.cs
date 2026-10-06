using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S111.Tests.Fixtures;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Validation;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pipeline-level tests for the S-111 dcf8 station-series branch of
/// <see cref="S111DatasetProcessor"/>. Verifies that the processor
/// emits a <see cref="ThinnedSymbolLayer"/> with one feature per
/// station and that each feature carries the
/// <c>"station:&lt;id&gt;"</c> ref recognised by the pick router.
/// </summary>
public class S111Dcf8ProcessorTests
{
    private sealed class IdentityFactory : ICrsTransformFactory
    {
        public static readonly IdentityFactory Instance = new();
        public ICrsTransform Create(string sourceCrs, string targetCrs) => IdentityCrsTransform.Instance;
    }

    private static string WriteFixture()
    {
        var path = Path.GetTempFileName() + ".h5";
        var stations = new[]
        {
            new S111Dcf8FixtureBuilder.Station<S111Dcf8FixtureBuilder.SpecValueRow>
            {
                Identifier = "S1",
                Latitude = 47.6f,
                Longitude = -122.3f,
                StartDateTime = "20240101T000000Z",
                EndDateTime = "20240101T020000Z",
                TimeRecordInterval = 3600,
                Values =
                [
                    new() { SurfaceCurrentSpeed = 0.3f, SurfaceCurrentDirection = 45f },
                    new() { SurfaceCurrentSpeed = 0.6f, SurfaceCurrentDirection = 50f },
                    new() { SurfaceCurrentSpeed = 0.9f, SurfaceCurrentDirection = 60f },
                ],
            },
            new S111Dcf8FixtureBuilder.Station<S111Dcf8FixtureBuilder.SpecValueRow>
            {
                Identifier = "S2",
                Latitude = 47.7f,
                Longitude = -122.4f,
                StartDateTime = "20240101T000000Z",
                EndDateTime = "20240101T020000Z",
                TimeRecordInterval = 3600,
                Values =
                [
                    new() { SurfaceCurrentSpeed = 1.0f, SurfaceCurrentDirection = 180f },
                    new() { SurfaceCurrentSpeed = 1.2f, SurfaceCurrentDirection = 185f },
                    new() { SurfaceCurrentSpeed = 1.1f, SurfaceCurrentDirection = 190f },
                ],
            },
        };

        S111Dcf8FixtureBuilder.WriteFile(path, stations);
        return path;
    }

    [Fact]
    public async Task Render_Dcf8_EmitsThinnedLayer_WithFeaturePerStation_TaggedByFeatureRefKey()
    {
        var path = WriteFixture();
        try
        {
            using var catalogues = S111TestCatalogues.Create();
            var p = new S111DatasetProcessor(path, catalogues, IdentityFactory.Instance);

            var result = await new MapsuiDatasetRenderer(IdentityFactory.Instance).RenderAsync(p, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Single(result.Layers);
            var layer = Assert.IsType<ThinnedSymbolLayer>(result.Layers[0]);

            // Zoomed in far enough (identity "projection", so units are
            // degrees) that the two stations 0.1° apart are both drawn.
            var features = layer.GetFeatures(layer.Extent!.Grow(1), resolution: 1e-6).ToList();
            Assert.Equal(2, features.Count);

            var refs = features
                .Select(f => f[MapsuiDisplayListRenderer.FeatureRefKey] as string)
                .ToArray();

            Assert.Contains("station:S1", refs);
            Assert.Contains("station:S2", refs);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task BuildPortrayal_WithCatalogue_UsesTheSpecArrowForEveryStation()
    {
        // S-111 Ed 2.0.0 §9.2.4: the arrow for a given speed is the same
        // regardless of the source of the data, so dcf8 stations get the
        // catalogue's SCAROW symbol and Eqn 9.1 scale, exactly like dcf2 cells.
        var path = WriteFixture();
        try
        {
            using var catalogues = S111TestCatalogues.Create();
            using var p = new S111DatasetProcessor(path, catalogues, IdentityFactory.Instance);

            var result = await p.BuildCoveragePortrayalAsync(new S111RenderContext { SymbolScale = 1.5 }, TestContext.Current.CancellationToken);

            var sub = Assert.IsType<GlyphCoverageSubLayer>(Assert.Single(result.SubLayers));
            Assert.Equal(2, sub.Glyphs.Count);
            Assert.All(sub.Glyphs, g =>
            {
                Assert.Equal(PointGlyphSymbol.Svg, g.Symbol);
                Assert.StartsWith("svg-content://", g.SvgSource);
                // Bands 1–3 (< 2 kn) use the catalogue's scaleFloor 0.40,
                // times the user's symbol scale — no clamping, no extra factor.
                Assert.Equal(0.40 * 1.5, g.SymbolScale, precision: 5);
                // Degrees true, clockwise — Mapsui's SymbolRotation convention.
                Assert.Equal((float)g.Attributes["DirectionDegreesTrue"], g.Rotation, precision: 3);
                Assert.Equal((float)g.Attributes["SpeedKnots"], g.ThinningPriority, precision: 5);
            });

            var thinning = Assert.IsType<GlyphThinning>(sub.Thinning);
            Assert.Equal(10.0 * 96.0 / 25.4, thinning.SymbolLengthPixelsPerScale, precision: 6);
            Assert.Equal(0.5, thinning.MaxSymbolToSpacingRatio);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task BuildPortrayal_Dcf8_UsesTheGridArrowPlaneAndPriority()
    {
        // #728: station arrows stack like dcf2 grid arrows (DynamicArrows,
        // priority 10) and keep the "s111.stations" key the pick router uses.
        var path = WriteFixture();
        try
        {
            using var catalogues = S111TestCatalogues.Create();
            using var p = new S111DatasetProcessor(path, catalogues, IdentityFactory.Instance);

            var result = await p.BuildCoveragePortrayalAsync(new S111RenderContext(), TestContext.Current.CancellationToken);

            AssertGridArrowPlacement(Assert.Single(result.SubLayers));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task BuildPortrayal_Dcf3_UsesTheGridArrowPlaneAndPriority()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            S111Dcf3FixtureBuilder.WriteFile(
                path,
                [
                    new() { Latitude = 47.6f, Longitude = -122.3f },
                    new() { Latitude = 47.7f, Longitude = -122.4f },
                ],
                [
                    new() { TimePoint = "20240101T000000Z", Values = [new() { SurfaceCurrentSpeed = 0.5f, SurfaceCurrentDirection = 45f }, new() { SurfaceCurrentSpeed = 1.5f, SurfaceCurrentDirection = 90f }] },
                ],
                lastDateTime: "20240101T000000Z");
            using var catalogues = S111TestCatalogues.Create();
            using var p = new S111DatasetProcessor(path, catalogues, IdentityFactory.Instance);

            var result = await p.BuildCoveragePortrayalAsync(new S111RenderContext(), TestContext.Current.CancellationToken);

            AssertGridArrowPlacement(Assert.Single(result.SubLayers));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertGridArrowPlacement(CoverageSubLayerBase subLayer)
    {
        var sub = Assert.IsType<GlyphCoverageSubLayer>(subLayer);
        Assert.Equal("s111.stations", sub.LayerKey);
        Assert.Equal(S98DisplayPlane.DynamicArrows, sub.Plane);
        Assert.Equal(10, sub.WithinPlanePriority);
        Assert.All(sub.Glyphs, g => Assert.StartsWith("station:", g.FeatureRefTag));
    }

    [Fact]
    public void Constructor_WithoutS111Catalogue_Throws()
    {
        // Station series are portrayed with the catalogue's arrows, like dcf2,
        // so there is no catalogue-less fallback.
        var path = WriteFixture();
        try
        {
            using var catalogues = new PortrayalCatalogueManager();
            Assert.Throws<InvalidOperationException>(
                () => new S111DatasetProcessor(path, catalogues, IdentityFactory.Instance));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RenderHeadless_Dcf8_PaintsStationGlyphs()
    {
        var path = WriteFixture();
        try
        {
            using var catalogues = S111TestCatalogues.Create();
            var processor = new S111DatasetProcessor(path, catalogues, IdentityFactory.Instance);

            using var bitmap = await processor.RenderHeadlessAsync(256, 256, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains(
                Enumerable.Range(0, bitmap.Width).SelectMany(x =>
                    Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y))),
                color => color != SKColors.White);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_Dcf8_UsesStationRulesInsteadOfUnsupportedProjection()
    {
        var path = WriteFixture();
        try
        {
            using var catalogues = S111TestCatalogues.Create();
            var processor = new S111DatasetProcessor(path, catalogues, IdentityFactory.Instance);

            var report = Assert.IsType<ValidationReport>(processor.Validate());

            Assert.Empty(report.Findings);
            Assert.Equal(3, report.RulesEvaluated);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetFeatureInfo_StationRef_AfterRender_ReturnsCurrentTimeSample()
    {
        var path = WriteFixture();
        try
        {
            using var catalogues = S111TestCatalogues.Create();
            var p = new S111DatasetProcessor(path, catalogues, IdentityFactory.Instance);

            // Render at the second time-step; expected S1 speed = 0.6, dir = 50.
            var secondStep = new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc);
            _ = await new MapsuiDatasetRenderer(IdentityFactory.Instance).RenderAsync(p, new S111RenderContext { TimeStep = secondStep }, TestContext.Current.CancellationToken);

            var info = p.GetFeatureInfo("station:S1");

            Assert.NotNull(info);
            Assert.Equal("SurfaceCurrent", info!.FeatureType);
            Assert.Equal("station:S1", info.FeatureRef);

            var attrs = info.Attributes.ToDictionary(a => a.Code, a => a);
            Assert.Equal("S1", attrs["stationIdentification"].RawValue);
            Assert.Equal("0.6", attrs["surfaceCurrentSpeed"].RawValue);
            // S-111 encodes surfaceCurrentSpeed in knots; the pick shows the
            // encoded value, not a m/s → knots re-conversion.
            Assert.Equal("0.6 kn", attrs["surfaceCurrentSpeed"].DisplayValue);
            Assert.Equal("50", attrs["surfaceCurrentDirection"].RawValue);
            Assert.Equal("3", attrs["sampleCount"].RawValue);
            Assert.Contains("timePoint", attrs.Keys);
            Assert.Contains("timeRange", attrs.Keys);
            Assert.Contains("stationPosition", attrs.Keys);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetFeatureInfo_UnknownStationRef_ReturnsNull()
    {
        var path = WriteFixture();
        try
        {
            using var catalogues = S111TestCatalogues.Create();
            var p = new S111DatasetProcessor(path, catalogues, IdentityFactory.Instance);
            _ = await new MapsuiDatasetRenderer(IdentityFactory.Instance).RenderAsync(p, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(p.GetFeatureInfo("station:Nope"));
            Assert.Null(p.GetFeatureInfo("plain-ref"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Metadata_Dcf8_DerivesExtentFromStationCoordinates()
    {
        var path = WriteFixture();
        try
        {
            using var catalogues = S111TestCatalogues.Create();
            var p = new S111DatasetProcessor(path, catalogues, IdentityFactory.Instance);

            var metadata = p.Metadata;

            Assert.Equal("S-111", metadata.Spec.Name);
            Assert.NotNull(metadata.Extent);

            // Fixture stations: S1 (47.6, -122.3), S2 (47.7, -122.4).
            var extent = metadata.Extent!;
            Assert.Equal(47.6, extent.SouthLatitude, 4);
            Assert.Equal(47.7, extent.NorthLatitude, 4);
            Assert.Equal(-122.4, extent.WestLongitude, 4);
            Assert.Equal(-122.3, extent.EastLongitude, 4);

            // Memoized: repeat access returns the same instance (issue #467 WS1).
            Assert.Same(metadata, p.Metadata);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
