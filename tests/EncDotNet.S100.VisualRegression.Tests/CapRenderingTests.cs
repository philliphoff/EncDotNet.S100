using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Issue #1069: CAP marine weather warnings through <see cref="CapDatasetProcessor"/>,
/// one golden per way an area gets its outline and between them every
/// severity: the NWS Special Marine Warning polygon over Delaware Bay (Severe)
/// with the hand-written circle (Extreme) and its zone-coded area beside it,
/// composited at one view time; the NWS Marine Weather Statement's eleven
/// Chesapeake Bay zones (Minor); and the NWS Gale Warning's Monterey Bay zone
/// (Moderate) in the Night palette.
/// </summary>
public sealed class CapRenderingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_polygon_a_circle_and_a_zone_off_Delaware_Bay()
    {
        var context = new RenderContext { TimeStep = new DateTime(2026, 8, 28, 15, 30, 0, DateTimeKind.Utc) };
        var polygon = new CapDatasetProcessor(CapFixtures.Fixture(CapFixtures.GeoJson));
        var circle = new CapDatasetProcessor(CapFixtures.Fixture(CapFixtures.Circle));

        HeadlessCompositeInput[] inputs =
        [
            HeadlessCompositeInput.ForVector(await circle.BuildVectorPortrayalAsync(context, Ct)),
            HeadlessCompositeInput.ForVector(await polygon.BuildVectorPortrayalAsync(context, Ct)),
        ];
        var bitmap = new HeadlessCompositor(new ProjNetCrsTransformFactory())
            .Render(inputs, new HeadlessCompositeOptions { Viewport = CentredViewport(-74.55, 39.4, 3.4, 640, 1_500_000) });

        Assert.True(TestHelpers.CountNonBackgroundPixels(bitmap) > 20_000);
        await TestHelpers.VerifyBitmap(bitmap);
    }

    [Fact]
    public async Task Zone_codes_resolved_to_the_Chesapeake_Bay_marine_zones()
    {
        var processor = new CapDatasetProcessor(CapFixtures.Fixture(CapFixtures.MarineWeatherStatement));

        var bitmap = await processor.RenderHeadlessAsync(
            480, 480, new RenderContext { TimeStep = processor.AvailableTimes[0] }, cancellationToken: Ct);

        Assert.True(TestHelpers.CountNonBackgroundPixels(bitmap) > 10_000);
        await TestHelpers.VerifyBitmap(bitmap);
    }

    [Fact]
    public async Task A_gale_warning_zone_in_the_night_palette()
    {
        var processor = new CapDatasetProcessor(CapFixtures.Fixture(CapFixtures.GaleWarning));

        var bitmap = await processor.RenderHeadlessAsync(
            480,
            360,
            new RenderContext { Palette = PaletteType.Night, TimeStep = CapFixtures.GaleOnset.UtcDateTime },
            background: new RgbaColor(0x10, 0x14, 0x1C),
            cancellationToken: Ct);

        await TestHelpers.VerifyBitmap(bitmap);
    }

    // A square viewport centred on (longitude, latitude), spanning the same
    // Web Mercator distance each way.
    private static Viewport CentredViewport(double longitude, double latitude, double longitudeSpan, int size, double scale)
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
            ScaleDenominator = scale,
        };
    }
}
