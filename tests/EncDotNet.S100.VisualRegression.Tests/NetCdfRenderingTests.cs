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
/// Issue #1071, the first end-to-end weather result: generated NetCDF-CF
/// files (a Copernicus-style NetCDF-4 wave file and a 10 m wind file) opened
/// by <see cref="NetCdfDatasetProcessor"/> and composited over an S-101 cell.
/// </summary>
public sealed class NetCdfRenderingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("netcdf-golden-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task NetCDF_waves_and_wind_over_an_S101_cell()
    {
        var bitmap = await RenderCompositeAsync(NetCdfFixtures.Run.AddHours(3));

        await TestHelpers.VerifyBitmap(bitmap);
    }

    [Fact]
    public async Task Stepping_the_time_changes_the_waves_and_the_wind()
    {
        using var first = await RenderCompositeAsync(NetCdfFixtures.Run);
        using var last = await RenderCompositeAsync(NetCdfFixtures.Run.AddHours(6));

        int changed = 0;
        for (int y = 0; y < first.Height; y += 2)
        {
            for (int x = 0; x < first.Width; x += 2)
            {
                if (first.GetPixel(x, y) != last.GetPixel(x, y))
                    changed++;
            }
        }

        // The wave height rises 0.5 m a step (crossing band boundaries) and the
        // wind 5 kn (another barb feather).
        Assert.True(changed > 5000, $"Only {changed} sampled pixels differ between 00Z and 06Z.");
    }

    // Issue #1097: with no viewport the compositor frames the union of the
    // cell's extent and the 0.2° wave grid's true extent, not the grid
    // widened to the 0.5° bins of the seam-aware accumulator.
    [Fact]
    public async Task Auto_fit_frames_the_cell_and_the_wave_grid_on_their_true_extents()
    {
        var cellPath = Path.Combine(TestHelpers.DatasetsRoot, "S101", "S-101", "DATASET_FILES", "101AA00DS0020.000");
        Assert.SkipUnless(File.Exists(cellPath), $"S-101 test dataset not present: {cellPath}");

        using var manager = CreateCatalogueManager();
        var s101 = await new S101DatasetProcessor(
                cellPath, manager, new MoonSharpLuaEngine(), new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue))
            .BuildVectorPortrayalAsync(new RenderContext(), Ct);
        var factory = new ProjNetCrsTransformFactory();
        using var waves = new NetCdfDatasetProcessor(
            NetCdfFixtures.WriteWaveNetCdf4(Path.Combine(_dir, "waves.nc")), factory);
        var wavesInput = HeadlessCompositeInput.ForCoverage(await waves.BuildCoveragePortrayalAsync(null, Ct));
        var compositor = new HeadlessCompositor(factory);

        Assert.True(compositor.Prepare([HeadlessCompositeInput.ForVector(s101)])
            .TryGetWorldBounds(out double cellMinX, out double cellMinY, out double cellMaxX, out double cellMaxY));
        Assert.True(compositor.Prepare([wavesInput])
            .TryGetWorldBounds(out double gridMinX, out double gridMinY, out double gridMaxX, out double gridMaxY));
        Assert.True(compositor.Prepare([HeadlessCompositeInput.ForVector(s101), wavesInput])
            .TryGetWorldBounds(out double minX, out double minY, out double maxX, out double maxY));

        var extent = waves.Metadata.Extent!;
        var (west, south) = WebMercator.FromLonLat(extent.WestLongitude, extent.SouthLatitude);
        var (east, north) = WebMercator.FromLonLat(extent.EastLongitude, extent.NorthLatitude);
        Assert.Equal(west, gridMinX, 1.0);
        Assert.Equal(east, gridMaxX, 1.0);
        Assert.Equal(south, gridMinY, 1.0);
        Assert.Equal(north, gridMaxY, 1.0);

        Assert.Equal(Math.Min(cellMinX, gridMinX), minX, 1.0);
        Assert.Equal(Math.Max(cellMaxX, gridMaxX), maxX, 1.0);
        Assert.Equal(Math.Min(cellMinY, gridMinY), minY, 1.0);
        Assert.Equal(Math.Max(cellMaxY, gridMaxY), maxY, 1.0);
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
        using var waves = new NetCdfDatasetProcessor(
            NetCdfFixtures.WriteWaveNetCdf4(Path.Combine(_dir, $"waves-{time:HH}.nc")), factory);
        using var wind = new NetCdfDatasetProcessor(
            NetCdfFixtures.WriteWindNetCdf4(Path.Combine(_dir, $"wind-{time:HH}.nc")), factory);
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
