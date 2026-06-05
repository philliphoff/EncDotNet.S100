using System.Linq;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S104.Tests.Fixtures;
using EncDotNet.S100.Datasets.S111.Tests.Fixtures;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pipeline-level tests for PR-J station time-series snapshot plumbing.
/// Verifies that S-104 and S-111 dcf=8 processors populate
/// <see cref="FeatureInfo.StationSeries"/> with the full time-step series
/// when asked for a <c>"station:&lt;id&gt;"</c> feature ref.
/// </summary>
public class StationTimeSeriesSnapshotTests
{
    private sealed class IdentityFactory : ICrsTransformFactory
    {
        public static readonly IdentityFactory Instance = new();
        public ICrsTransform Create(string sourceCrs, string targetCrs) => IdentityCrsTransform.Instance;
    }

    private static string WriteS104Fixture()
    {
        var path = Path.GetTempFileName() + ".h5";
        var stations = new[]
        {
            new S104Dcf8FixtureBuilder.Station<S104Dcf8FixtureBuilder.SpecValueRow>
            {
                Identifier = "ST01",
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
        };
        S104Dcf8FixtureBuilder.WriteFile(path, stations, waterLevelTrendThreshold: 0.5);
        return path;
    }

    private static string WriteS111Fixture()
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
        };
        S111Dcf8FixtureBuilder.WriteFile(path, stations);
        return path;
    }

    [Fact]
    public void S104_StationRef_PopulatesStationSeries_WithFullHeightSeries()
    {
        var path = WriteS104Fixture();
        try
        {
            var p = new S104DatasetProcessor(path, IdentityFactory.Instance);
            _ = p.RenderAsync().GetAwaiter().GetResult();
            var info = p.GetFeatureInfo("station:ST01");

            Assert.NotNull(info);
            Assert.NotNull(info!.StationSeries);
            var snap = info.StationSeries!;
            Assert.Equal("ST01", snap.StationId);
            Assert.Equal(3, snap.Times.Count);
            var heights = Assert.Single(snap.Channels);
            Assert.Equal("waterLevelHeight", heights.Key);
            Assert.Equal(new[] { 1.0f, 1.5f, 1.2f }, heights.Values.ToArray());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void S111_StationRef_PopulatesStationSeries_WithSpeedAndDirectionChannels()
    {
        var path = WriteS111Fixture();
        try
        {
            var p = new S111DatasetProcessor(path, new PortrayalCatalogueManager(), IdentityFactory.Instance);
            _ = p.RenderAsync().GetAwaiter().GetResult();
            var info = p.GetFeatureInfo("station:S1");

            Assert.NotNull(info);
            Assert.NotNull(info!.StationSeries);
            var snap = info.StationSeries!;
            Assert.Equal("S1", snap.StationId);
            Assert.Equal(3, snap.Times.Count);
            Assert.Equal(2, snap.Channels.Count);
            var byKey = snap.Channels.ToDictionary(c => c.Key);
            Assert.Equal(new[] { 0.3f, 0.6f, 0.9f }, byKey["surfaceCurrentSpeed"].Values.ToArray());
            Assert.Equal(new[] { 45f, 50f, 60f }, byKey["surfaceCurrentDirection"].Values.ToArray());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void S104_NonStationRef_StationSeriesIsNull()
    {
        var path = WriteS104Fixture();
        try
        {
            var p = new S104DatasetProcessor(path, IdentityFactory.Instance);
            _ = p.RenderAsync().GetAwaiter().GetResult();
            // unknown ref returns null FeatureInfo entirely
            Assert.Null(p.GetFeatureInfo("station:Missing"));
            Assert.Null(p.GetFeatureInfo("not-a-station"));
        }
        finally { File.Delete(path); }
    }
}
