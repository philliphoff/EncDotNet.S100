using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Spec;
using EncDotNet.S100.Datasets.TropicalCyclones;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1296 (vector weather S2): an NHC advisory drawn in storm ink on two
/// S-98 planes, its parts as features that each report the storm, one hit
/// per storm, the V23 pick attributes, and labels in the host's time zone.
/// The fixture is Hurricane Ian's advisory 20, issued 03:00 UTC 28 Sep 2022.
/// </summary>
public sealed class TropicalCyclonePortrayalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TropicalCycloneDatasetProcessor Ian() => new(TropicalCycloneFixtures.IanForecast);

    [Fact]
    public async Task A_storm_draws_fills_on_the_on_demand_surface_and_lines_on_other_chart_overlays()
    {
        var result = await Ian().BuildVectorPortrayalAsync(new RenderContext { Palette = PaletteType.Dusk }, Ct);

        Assert.Equal(2, result.SubLayers.Count);
        var areas = result.SubLayers.Single(l => l.LayerKey == TropicalCycloneStyle.AreasLayerKey);
        var track = result.SubLayers.Single(l => l.LayerKey == TropicalCycloneStyle.TrackLayerKey);
        Assert.Equal(S98DisplayPlane.OnDemandSurface, areas.Plane);
        Assert.Equal(10, areas.WithinPlanePriority);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, track.Plane);
        Assert.Equal(95, track.WithinPlanePriority);
        Assert.All(areas.Instructions, i => Assert.IsType<AreaInstruction>(i));
        Assert.DoesNotContain(track.Instructions, i => i is AreaInstruction);
        Assert.Equal(5, areas.Legend!.Count);
        Assert.Equal(8, track.Legend!.Count);
        Assert.Equal(WeatherPalette.Palette.GetColorPalette(PaletteType.Dusk).Name, result.Palette.Name);
        Assert.Contains("not for navigation", result.Info, StringComparison.Ordinal);

        // Every instruction draws a part the geometry provider knows.
        Assert.All(result.SubLayers.SelectMany(l => l.Instructions), i => Assert.NotNull(result.GeometryProvider.GetGeometry(i.FeatureReference)));
        // The cone in both parts, the three radii, both tracks and the storm.
        var references = result.SubLayers.SelectMany(l => l.Instructions).Select(i => i.FeatureReference).ToHashSet();
        Assert.Contains("AL092022", references);
        Assert.Contains("AL092022 cone days 1-3", references);
        Assert.Contains("AL092022 cone days 4-5", references);
        Assert.Contains("AL092022 64 kt", references);
        Assert.Contains("AL092022 34 kt", references);
        Assert.Contains("AL092022 track", references);
        Assert.Contains("AL092022 past track", references);
        Assert.Contains(areas.Instructions, i => i is AreaInstruction { AreaFillReference: TropicalCycloneStyle.StippleFill });
    }

    [Fact]
    public async Task The_storm_is_hurricane_Ian_at_the_advisory_with_its_two_line_label()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var result = await Ian().BuildVectorPortrayalAsync(new RenderContext(), Ct);
            var storm = result.SubLayers.SelectMany(l => l.Instructions).Where(i => i.FeatureReference == "AL092022").ToArray();

            var texts = storm.OfType<TextInstruction>().Select(t => t.Text).ToArray();
            Assert.Equal("HURRICANE IAN · 105 kt · 952 hPa", texts[0]);
            Assert.StartsWith("Adv 20 · issued Wed 03:00 · ", texts[1], StringComparison.Ordinal);
            Assert.Contains(storm.OfType<PointInstruction>(), p => p.SymbolReference == TropicalCycloneStyle.RingSymbol);
            Assert.Contains(storm.OfType<PointInstruction>(), p => p.SymbolReference!.StartsWith("WXTC", StringComparison.Ordinal) && p.SymbolReference.EndsWith('N'));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public async Task Labels_are_written_in_the_hosts_time_zone()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var eastern = TimeZoneInfo.CreateCustomTimeZone("Test-4", TimeSpan.FromHours(-4), "Test-4", "Test-4");
            var utc = await Ian().BuildVectorPortrayalAsync(new RenderContext(), Ct);
            var local = await Ian().BuildVectorPortrayalAsync(new RenderContext { DisplayTimeZone = eastern }, Ct);

            static string[] Labels(VectorPortrayalResult result) =>
                [.. result.SubLayers.SelectMany(l => l.Instructions).OfType<TextInstruction>().Select(t => t.Text)];

            // Issued 03:00 UTC Wednesday is 23:00 Tuesday four hours west.
            Assert.Contains(Labels(utc), t => t.Contains("issued Wed 03:00", StringComparison.Ordinal));
            Assert.Contains(Labels(local), t => t.Contains("issued Tue 23:00", StringComparison.Ordinal));
            // Forecast points at 12 hours and every 24 hours, and the last.
            Assert.Contains(Labels(utc), t => t.StartsWith("Wed 12:00 · ", StringComparison.Ordinal));
            Assert.Contains(Labels(utc), t => t.StartsWith("Sun 00:00 · ", StringComparison.Ordinal));
            Assert.DoesNotContain(Labels(utc), t => t.StartsWith("Thu 12:00", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Every_part_reports_the_storm_with_the_part_that_was_picked()
    {
        var processor = Ian();
        var parts = processor.EnumerateFeatures().ToArray();

        Assert.Equal("AL092022", parts[0].FeatureRef);
        Assert.All(parts, p => Assert.Equal(TropicalCycloneDatasetProcessor.FeatureType, p.FeatureType));
        var infos = parts.Select(p => processor.GetFeatureInfoAt(p.Ordinal)!).ToArray();
        Assert.All(infos, info => Assert.Equal("AL092022", info.FeatureRef));
        Assert.All(infos, info => Assert.Equal("Category 3", info.FeatureTypeName));
        Assert.Equal(parts.Length, infos.Select(i => i.Attributes.Single(a => a.Code == "pickedPart").RawValue).Distinct().Count());

        var byRef = processor.GetFeatureInfo("al092022 +24h")!;
        Assert.Equal("AL092022", byRef.FeatureRef);
        Assert.StartsWith("Forecast point +24 h", byRef.Attributes.Single(a => a.Code == "pickedPart").RawValue, StringComparison.Ordinal);
        Assert.Null(processor.GetFeatureInfo("AL092022 nothing"));
    }

    [Fact]
    public void The_pick_card_has_the_notice_name_advisory_intensity_radii_and_cone_rows()
    {
        var info = Ian().GetFeatureInfo("AL092022")!;
        var rows = info.Attributes.ToDictionary(a => a.Code);

        Assert.True(info.Attributes[0].IsNavigationNotice);
        Assert.Equal(TropicalCycloneDatasetProcessor.NotForNavigationNotice, info.Attributes[0].RawValue);
        Assert.Equal("HURRICANE IAN", rows["name"].RawValue);
        Assert.Equal("20", rows["advisory"].RawValue);
        Assert.Equal(new DateTime(2022, 9, 28, 3, 0, 0, DateTimeKind.Utc), rows["issued"].DateTimeValue);
        Assert.Equal(new DateTime(2022, 9, 28, 9, 0, 0, DateTimeKind.Utc), rows["nextAdvisory"].DateTimeValue);
        Assert.Equal("105 kn, gusts 130 kn", rows["maxWind"].RawValue);
        Assert.Equal("952 hPa", rows["pressure"].RawValue);
        Assert.EndsWith(" kn", rows["movement"].RawValue, StringComparison.Ordinal);
        Assert.Equal("Storm, at the advisory", rows["pickedPart"].RawValue);
        Assert.EndsWith("nm (NE/SE/SW/NW)", rows["radius64"].RawValue, StringComparison.Ordinal);
        Assert.Contains("radius50", rows.Keys);
        Assert.Contains("radius34", rows.Keys);
        Assert.Equal(TropicalCycloneDatasetProcessor.ConeCaution, rows["cone"].RawValue);
        Assert.Equal(
            ["notice", "name", "advisory", "issued", "nextAdvisory", "position", "maxWind", "pressure", "movement", "pickedPart", "radius64", "radius50", "radius34", "cone"],
            info.Attributes.Select(a => a.Code));
    }

    [Fact]
    public void A_pick_gives_one_hit_per_storm_on_the_nearest_part()
    {
        var processor = Ian();
        var storm = processor.Advisory.Current!.Position;

        // At the storm's centre the storm, the track, the radii and the cone
        // all lie under the pick: one hit, on the storm.
        var atStorm = Assert.Single(processor.HitTestFeatures(storm.Latitude, storm.Longitude, 20_000));
        Assert.Equal("AL092022", atStorm.FeatureRef);

        // Inside the cone near its far end, away from the track's points: the cone part.
        var last = processor.Advisory.Forecast[^1].Position;
        var inCone = Assert.Single(processor.HitTestFeatures(last.Latitude - 0.3, last.Longitude + 1.2, 1_000));
        Assert.StartsWith("AL092022 cone", inCone.FeatureRef, StringComparison.Ordinal);

        Assert.Empty(processor.HitTestFeatures(0, 0, 1_000));
    }

    [Theory]
    [InlineData("2022-09-28T03:00:00Z", "2022-09-28T09:00:00Z")]
    [InlineData("2022-09-28T02:59:00Z", "2022-09-28T03:00:00Z")]
    [InlineData("2022-09-28T21:00:00Z", "2022-09-29T03:00:00Z")]
    [InlineData("2022-09-27T23:00:00-04:00", "2022-09-28T09:00:00Z")]
    public void The_next_advisory_is_due_at_the_next_of_03_09_15_and_21_UTC(string issued, string due) =>
        Assert.Equal(
            DateTimeOffset.Parse(due, CultureInfo.InvariantCulture),
            TropicalCycloneDatasetProcessor.NextAdvisoryDue(DateTimeOffset.Parse(issued, CultureInfo.InvariantCulture)));

    [Fact]
    public void The_catalog_lists_the_parts_for_identify_and_pick()
    {
        var processor = Ian();
        var dataset = LoadedDatasetProjector.Project(new DatasetId("ian"), processor)!;

        var features = FeatureAccessor.GetFeatures(dataset, extent: null)!.ToArray();

        Assert.Equal(processor.EnumerateFeatures().Select(f => f.FeatureRef), features.Select(f => f.Id));
        Assert.Equal("HURRICANE IAN", features[0].Attributes["name"]);
        Assert.Empty(FeatureAccessor.GetFeatures(dataset, new BoundingBox(-10, -10, 10, 10))!);
    }

    [Fact]
    public void A_radius_without_NHCs_polygon_is_built_from_its_quadrants()
    {
        var radius = new TropicalCycloneWindRadii(34, 120, 60, 0, 90, []);
        var surface = Assert.Single(radius.QuadrantSurface(new GeoPosition(25, -80), 8));

        Assert.Equal(4 * 8 + 1, surface.ExteriorRing.Count);
        Assert.Equal(surface.ExteriorRing[0], surface.ExteriorRing[^1]);
        // 120 nm north-east is 2° of latitude due north at the quadrant's start.
        Assert.Equal(27.0, surface.ExteriorRing[0].Latitude, 1);
        Assert.Empty(new TropicalCycloneWindRadii(34, 0, 0, 0, 0, []).QuadrantSurface(new GeoPosition(25, -80), 8));
    }

    [Fact]
    public async Task It_renders_headless()
    {
        using var bitmap = await Ian().RenderHeadlessAsync(400, 300, new RenderContext(), cancellationToken: Ct);

        int ink = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Red < 200)
                    ink++;
        Assert.True(ink > 1_000, $"{ink} ink pixels.");
    }
}
