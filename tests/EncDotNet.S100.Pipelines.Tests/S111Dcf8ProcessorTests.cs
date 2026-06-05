using System.Linq;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S111.Tests.Fixtures;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pipeline-level tests for the S-111 dcf8 station-series branch of
/// <see cref="S111DatasetProcessor"/>. Verifies that the processor
/// emits a <see cref="MemoryLayer"/> tagged with one feature per
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
    public void Render_Dcf8_EmitsMemoryLayer_WithFeaturePerStation_TaggedByFeatureRefKey()
    {
        var path = WriteFixture();
        try
        {
            // dcf8 path doesn't consult the portrayal catalogue manager,
            // so an empty manager is fine.
            using var catalogues = new PortrayalCatalogueManager();
            var p = new S111DatasetProcessor(path, catalogues, IdentityFactory.Instance);

            var result = p.RenderAsync().GetAwaiter().GetResult();

            Assert.Single(result.Layers);
            var memoryLayer = Assert.IsType<MemoryLayer>(result.Layers[0]);

            var features = memoryLayer.Features?.ToList()
                ?? throw new InvalidOperationException("MemoryLayer must expose features.");
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
    public void GetFeatureInfo_StationRef_AfterRender_ReturnsCurrentTimeSample()
    {
        var path = WriteFixture();
        try
        {
            using var catalogues = new PortrayalCatalogueManager();
            var p = new S111DatasetProcessor(path, catalogues, IdentityFactory.Instance);

            // Render at the second time-step; expected S1 speed = 0.6, dir = 50.
            var secondStep = new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc);
            _ = p.RenderAsync(new S111RenderContext { TimeStep = secondStep }).GetAwaiter().GetResult();

            var info = p.GetFeatureInfo("station:S1");

            Assert.NotNull(info);
            Assert.Equal("SurfaceCurrent", info!.FeatureType);
            Assert.Equal("station:S1", info.FeatureRef);

            var attrs = info.Attributes.ToDictionary(a => a.Code, a => a);
            Assert.Equal("S1", attrs["stationIdentification"].RawValue);
            Assert.Equal("0.6", attrs["surfaceCurrentSpeed"].RawValue);
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
    public void GetFeatureInfo_UnknownStationRef_ReturnsNull()
    {
        var path = WriteFixture();
        try
        {
            using var catalogues = new PortrayalCatalogueManager();
            var p = new S111DatasetProcessor(path, catalogues, IdentityFactory.Instance);
            _ = p.RenderAsync().GetAwaiter().GetResult();

            Assert.Null(p.GetFeatureInfo("station:Nope"));
            Assert.Null(p.GetFeatureInfo("plain-ref"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
