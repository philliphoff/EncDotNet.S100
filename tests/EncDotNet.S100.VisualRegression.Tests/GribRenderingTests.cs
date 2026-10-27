using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Issue #1074: GRIB2 wind and waves through <see cref="GribDatasetProcessor"/>.
/// The S-101 test cells are synthetic (62°E, 32°S), where no NOAA grid
/// reaches, so the composited golden draws generated GRIB2 messages over the
/// cell: GFS-Wave style waves (rows north to south, a bitmapped node) and GFS
/// style 10 m wind, each file three forecast hours appended. A second golden
/// draws the real GFS-Wave GRIB filter fixture off the US East Coast on its own.
/// </summary>
public sealed class GribRenderingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grib-golden-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task GRIB2_waves_and_wind_over_an_S101_cell()
    {
        var bitmap = await RenderCompositeAsync(GribFixtures.Run.AddHours(3));

        await TestHelpers.VerifyBitmap(bitmap);
    }

    [Fact]
    public async Task Stepping_the_time_changes_the_waves_and_the_wind()
    {
        using var first = await RenderCompositeAsync(GribFixtures.Run);
        using var last = await RenderCompositeAsync(GribFixtures.Run.AddHours(6));

        int changed = 0;
        for (int y = 0; y < first.Height; y += 2)
        {
            for (int x = 0; x < first.Width; x += 2)
            {
                if (first.GetPixel(x, y) != last.GetPixel(x, y))
                    changed++;
            }
        }

        // The wave height rises 0.6 m a step (crossing band boundaries) and the
        // wind 6 kn (another barb feather).
        Assert.True(changed > 5000, $"Only {changed} sampled pixels differ between 00Z and 06Z.");
    }

    [Fact]
    public async Task GFS_Wave_filter_download_off_the_US_East_Coast()
    {
        using var processor = new GribDatasetProcessor(
            GribFixtures.Fixture(GribFixtures.WaveFilter), new ProjNetCrsTransformFactory());

        var bitmap = await processor.RenderHeadlessAsync(
            480, 480, new RenderContext { TimeStep = GribFixtures.Run.AddHours(3) }, cancellationToken: Ct);

        Assert.True(TestHelpers.CountNonBackgroundPixels(bitmap) > 100_000);
        await TestHelpers.VerifyBitmap(bitmap);
    }

    // The S-101 test cell, the wave fill and the wind barbs at one time step,
    // in a 640 × 640 viewport on the cell.
    private async Task<SKBitmap> RenderCompositeAsync(DateTime time)
    {
        var cellPath = Path.Combine(TestHelpers.DatasetsRoot, "S101", "S-101", "DATASET_FILES", "101AA00DS0020.000");
        Assert.SkipUnless(File.Exists(cellPath), $"S-101 test dataset not present: {cellPath}");

        using var manager = CreateCatalogueManager();
        var s101 = await new S101DatasetProcessor(
                cellPath, manager, new MoonSharpLuaEngine(), new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue))
            .BuildVectorPortrayalAsync(new RenderContext(), Ct);

        var factory = new ProjNetCrsTransformFactory();
        using var waves = new GribDatasetProcessor(
            GribFixtures.Write(Path.Combine(_dir, $"gfswave-{time:HH}.grib2"), GribFixtures.ChartWaves()), factory);
        using var wind = new GribDatasetProcessor(
            GribFixtures.Write(Path.Combine(_dir, $"gfs-{time:HH}.grib2"), GribFixtures.ChartWind()), factory);
        var context = new RenderContext { TimeStep = time };

        HeadlessCompositeInput[] inputs =
        [
            HeadlessCompositeInput.ForVector(s101),
            HeadlessCompositeInput.ForCoverage(await wind.BuildCoveragePortrayalAsync(context, Ct)),
            HeadlessCompositeInput.ForCoverage(await waves.BuildCoveragePortrayalAsync(context, Ct)),
        ];

        return new HeadlessCompositor(factory)
            .Render(inputs, new HeadlessCompositeOptions { Viewport = CentredViewport(62.0833, -32.2167, 0.2, 640) });
    }

    // A square viewport centred on (longitude, latitude), spanning the same
    // Web Mercator distance each way.
    private static Viewport CentredViewport(double longitude, double latitude, double longitudeSpan, int size)
    {
        var (x, y) = WebMercator.FromLonLat(longitude, latitude);
        var (west, _) = WebMercator.FromLonLat(longitude - longitudeSpan / 2, latitude);
        double half = x - west;
        var (_, south) = WebMercator.ToLonLat(x, y - half);
        var (_, north) = WebMercator.ToLonLat(x, y + half);
        return new Viewport
        {
            MinLongitude = longitude - longitudeSpan / 2,
            MaxLongitude = longitude + longitudeSpan / 2,
            MinLatitude = south,
            MaxLatitude = north,
            WidthPixels = size,
            HeightPixels = size,
            ScaleDenominator = 1_000_000,
        };
    }

    private static PortrayalCatalogueManager CreateCatalogueManager()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        return manager;
    }
}
