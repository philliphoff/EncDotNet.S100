using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S104.Tests.Fixtures;
using EncDotNet.S100.Datasets.S111.Tests.Fixtures;
using EncDotNet.S100.Portrayals;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Headless rendering of fixed-station coverage (issue #191): an S-111 dcf3
/// (ungeorectified nodes) dataset renders on its own, and station glyphs of
/// S-104 / S-111 station series survive the composite path the CLI's
/// <c>--layer</c> and <c>--bbox</c> renders use (<see cref="HeadlessCompositor"/>).
/// The single-dataset dcf8 renders are covered by the dcf8 processor tests.
/// </summary>
public class StationSeriesHeadlessTests
{
    private sealed class IdentityFactory : ICrsTransformFactory
    {
        public static readonly IdentityFactory Instance = new();
        public ICrsTransform Create(string sourceCrs, string targetCrs) => IdentityCrsTransform.Instance;
    }

    // A station at the centre of the composite viewport, and a second one
    // well outside it.
    private const float CentreLat = 50.75f, CentreLon = -1.30f;
    private const float FarLat = 50.95f, FarLon = -0.90f;

    [Fact]
    public async Task S111Dcf3_RenderHeadless_PaintsNodeGlyphs()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            S111Dcf3FixtureBuilder.WriteFile(
                path,
                [
                    new() { Latitude = CentreLat, Longitude = CentreLon },
                    new() { Latitude = FarLat, Longitude = FarLon },
                ],
                [
                    new() { TimePoint = "20240101T000000Z", Values = [new() { SurfaceCurrentSpeed = 0.5f, SurfaceCurrentDirection = 45f }, new() { SurfaceCurrentSpeed = 1.5f, SurfaceCurrentDirection = 90f }] },
                    new() { TimePoint = "20240101T010000Z", Values = [new() { SurfaceCurrentSpeed = 0.7f, SurfaceCurrentDirection = 50f }, new() { SurfaceCurrentSpeed = 1.2f, SurfaceCurrentDirection = 95f }] },
                ],
                lastDateTime: "20240101T010000Z");
            using var catalogues = new PortrayalCatalogueManager();
            var processor = new S111DatasetProcessor(path, catalogues, IdentityFactory.Instance);

            using var bitmap = await processor.RenderHeadlessAsync(256, 256);

            Assert.True(CountNonWhite(bitmap) > 0, "expected node glyphs to be painted");
        }
        finally
        {
            File.Delete(path);
        }
    }

    public static TheoryData<string> Products => new() { "S-104 dcf8", "S-111 dcf8", "S-111 dcf3" };

    [Theory]
    [MemberData(nameof(Products))]
    public async Task Composite_PaintsTheStationInsideTheViewport_AndOnlyThere(string product)
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var (processor, context) = WriteAndOpen(product, path);
            var viewport = new Viewport
            {
                MinLatitude = CentreLat - 0.05,
                MaxLatitude = CentreLat + 0.05,
                MinLongitude = CentreLon - 0.08,
                MaxLongitude = CentreLon + 0.08,
                WidthPixels = 256,
                HeightPixels = 256,
                ScaleDenominator = 100_000,
            };

            var portrayal = await ((ICoveragePortrayalSource)processor)
                .BuildCoveragePortrayalAsync(context with { Viewport = viewport });
            using var bitmap = new HeadlessCompositor(new ProjNetCrsTransformFactory()).Render(
                [HeadlessCompositeInput.ForCoverage(portrayal)],
                new HeadlessCompositeOptions { Viewport = viewport });

            // The centre station's glyph covers the centre pixel; the corners,
            // well away from it, stay background.
            Assert.NotEqual(SKColors.White, bitmap.GetPixel(128, 128));
            Assert.Equal(SKColors.White, bitmap.GetPixel(5, 5));
            Assert.Equal(SKColors.White, bitmap.GetPixel(250, 250));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static (IDatasetProcessor Processor, RenderContext Context) WriteAndOpen(string product, string path)
    {
        switch (product)
        {
            case "S-104 dcf8":
                S104Dcf8FixtureBuilder.WriteFile(path, new[]
                {
                    S104Station("WL1", CentreLat, CentreLon),
                    S104Station("WL2", FarLat, FarLon),
                });
                return (new S104DatasetProcessor(path, new ProjNetCrsTransformFactory()), new S104RenderContext());

            case "S-111 dcf8":
                S111Dcf8FixtureBuilder.WriteFile(path, new[]
                {
                    S111Station("SC1", CentreLat, CentreLon),
                    S111Station("SC2", FarLat, FarLon),
                });
                return (new S111DatasetProcessor(path, new PortrayalCatalogueManager(), new ProjNetCrsTransformFactory()), new S111RenderContext());

            default:
                S111Dcf3FixtureBuilder.WriteFile(
                    path,
                    [
                        new() { Latitude = CentreLat, Longitude = CentreLon },
                        new() { Latitude = FarLat, Longitude = FarLon },
                    ],
                    [
                        new() { TimePoint = "20240101T000000Z", Values = [new() { SurfaceCurrentSpeed = 0.5f, SurfaceCurrentDirection = 45f }, new() { SurfaceCurrentSpeed = 1.5f, SurfaceCurrentDirection = 90f }] },
                    ],
                    lastDateTime: "20240101T000000Z");
                return (new S111DatasetProcessor(path, new PortrayalCatalogueManager(), new ProjNetCrsTransformFactory()), new S111RenderContext());
        }
    }

    private static S104Dcf8FixtureBuilder.Station<S104Dcf8FixtureBuilder.SpecValueRow> S104Station(string id, float lat, float lon) => new()
    {
        Identifier = id,
        Latitude = lat,
        Longitude = lon,
        StartDateTime = "20240101T000000Z",
        EndDateTime = "20240101T010000Z",
        TimeRecordInterval = 3600,
        Values = [new() { WaterLevelHeight = 1.0f, WaterLevelTrend = 2 }, new() { WaterLevelHeight = 1.2f, WaterLevelTrend = 2 }],
    };

    private static S111Dcf8FixtureBuilder.Station<S111Dcf8FixtureBuilder.SpecValueRow> S111Station(string id, float lat, float lon) => new()
    {
        Identifier = id,
        Latitude = lat,
        Longitude = lon,
        StartDateTime = "20240101T000000Z",
        EndDateTime = "20240101T010000Z",
        TimeRecordInterval = 3600,
        Values = [new() { SurfaceCurrentSpeed = 0.5f, SurfaceCurrentDirection = 45f }, new() { SurfaceCurrentSpeed = 0.7f, SurfaceCurrentDirection = 50f }],
    };

    private static int CountNonWhite(SKBitmap bitmap)
    {
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y) != SKColors.White) count++;
        return count;
    }
}
