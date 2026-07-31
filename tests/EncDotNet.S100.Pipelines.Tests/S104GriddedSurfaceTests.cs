using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S104.Tests.Fixtures;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the <see cref="S104DatasetProcessor.IsGriddedSurface"/>
/// discriminator that lets the viewer default the synthesised dcf2
/// colour-band water-level surface to hidden while keeping dcf8
/// fixed-station glyphs visible (issue #483).
/// </summary>
public class S104GriddedSurfaceTests
{
    private sealed class IdentityFactory : ICrsTransformFactory
    {
        public static readonly IdentityFactory Instance = new();
        public ICrsTransform Create(string sourceCrs, string targetCrs) => IdentityCrsTransform.Instance;
    }

    [Fact]
    public void IsGriddedSurface_True_ForDcf2GriddedCoverage()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");
        try
        {
            var values = new[]
            {
                new S104FixtureBuilder.SpecRow { WaterLevelHeight = 1.5f, WaterLevelTrend = 2 },
            };
            S104FixtureBuilder.WriteFile(path, values, 1, 1, useF64GridAttrs: false, useUnsignedCounts: false);

            var processor = new S104DatasetProcessor(path, IdentityFactory.Instance);

            Assert.True(processor.IsGriddedSurface);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsGriddedSurface_False_ForDcf8StationSeries()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");
        try
        {
            var stations = new[]
            {
                new S104Dcf8FixtureBuilder.Station<S104Dcf8FixtureBuilder.SpecValueRow>
                {
                    Identifier = "Alpha",
                    Latitude = 51.5f,
                    Longitude = -0.1f,
                    StartDateTime = "20240101T000000Z",
                    EndDateTime = "20240101T010000Z",
                    TimeRecordInterval = 3600,
                    Values =
                    [
                        new() { WaterLevelHeight = 1.0f, WaterLevelTrend = 1 },
                        new() { WaterLevelHeight = 1.5f, WaterLevelTrend = 2 },
                    ],
                },
            };
            S104Dcf8FixtureBuilder.WriteFile(path, stations, waterLevelTrendThreshold: 0.5);

            var processor = new S104DatasetProcessor(path, IdentityFactory.Instance);

            Assert.False(processor.IsGriddedSurface);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
