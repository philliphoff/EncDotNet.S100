using System.Linq;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S104.Tests.Fixtures;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pipeline-level tests for the S-104 dcf8 station-series branch of
/// <see cref="S104DatasetProcessor"/>. Verifies that the processor
/// emits a <see cref="MemoryLayer"/> tagged with one feature per
/// station and that each feature carries the
/// <c>"station:&lt;id&gt;"</c> ref recognised by the pick router.
/// </summary>
public class S104Dcf8ProcessorTests
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
            new S104Dcf8FixtureBuilder.Station<S104Dcf8FixtureBuilder.SpecValueRow>
            {
                Identifier = "Alpha",
                Latitude = 51.5f,
                Longitude = -0.1f,
                StartDateTime = "20240101T000000Z",
                EndDateTime = "20240101T020000Z",
                TimeRecordInterval = 3600,
                Values =
                [
                    new() { WaterLevelHeight = 1.0f, WaterLevelTrend = 1 },
                    new() { WaterLevelHeight = 1.5f, WaterLevelTrend = 2 },
                    new() { WaterLevelHeight = 1.2f, WaterLevelTrend = 3 },
                ],
            },
            new S104Dcf8FixtureBuilder.Station<S104Dcf8FixtureBuilder.SpecValueRow>
            {
                Identifier = "Bravo",
                Latitude = 51.6f,
                Longitude = -0.2f,
                StartDateTime = "20240101T000000Z",
                EndDateTime = "20240101T020000Z",
                TimeRecordInterval = 3600,
                Values =
                [
                    new() { WaterLevelHeight = -0.3f, WaterLevelTrend = 2 },
                    new() { WaterLevelHeight = 0.1f, WaterLevelTrend = 2 },
                    new() { WaterLevelHeight = 0.4f, WaterLevelTrend = 3 },
                ],
            },
        };

        S104Dcf8FixtureBuilder.WriteFile(path, stations, waterLevelTrendThreshold: 0.5);
        return path;
    }

    [Fact]
    public void Render_Dcf8_EmitsMemoryLayer_WithFeaturePerStation_TaggedByFeatureRefKey()
    {
        var path = WriteFixture();
        try
        {
            using var processor = (System.IDisposable?)null; // placeholder so analyzers don't complain about disposable processors
            var p = new S104DatasetProcessor(path, IdentityFactory.Instance);

            var result = p.Render();

            Assert.Single(result.Layers);
            var memoryLayer = Assert.IsType<MemoryLayer>(result.Layers[0]);

            var features = memoryLayer.Features?.ToList()
                ?? throw new InvalidOperationException("MemoryLayer must expose features.");
            Assert.Equal(2, features.Count);

            var refs = features
                .Select(f => f[MapsuiDisplayListRenderer.FeatureRefKey] as string)
                .ToArray();

            Assert.Contains("station:Alpha", refs);
            Assert.Contains("station:Bravo", refs);
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
            var p = new S104DatasetProcessor(path, IdentityFactory.Instance);

            // Render at the second time-step; GetFeatureInfo should report
            // the value at the same step (height = 1.5, trend = 2 for Alpha).
            var secondStep = new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc);
            _ = p.Render(new S104RenderContext { TimeStep = secondStep });

            var info = p.GetFeatureInfo("station:Alpha");

            Assert.NotNull(info);
            Assert.Equal("WaterLevel", info!.FeatureType);
            Assert.Equal("station:Alpha", info.FeatureRef);

            var attrs = info.Attributes.ToDictionary(a => a.Code, a => a);
            Assert.Equal("Alpha", attrs["stationIdentification"].RawValue);
            Assert.Equal("1.5", attrs["waterLevelHeight"].RawValue);
            Assert.Equal("2", attrs["waterLevelTrend"].RawValue);
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
            var p = new S104DatasetProcessor(path, IdentityFactory.Instance);
            _ = p.Render();

            Assert.Null(p.GetFeatureInfo("station:Missing"));
            Assert.Null(p.GetFeatureInfo("not-a-station-ref"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
