using System.Text;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.TestSupport;
using static EncDotNet.S100.Datasets.Cap.Tests.CapReaderTests;

namespace EncDotNet.S100.Datasets.Cap.Tests;

/// <summary>
/// Issue #1069: resolving CAP areas to outlines (polygons, circles, marine
/// zones, unmatched codes) and judging which are in force, by the S-124 rule.
/// </summary>
public sealed class CapWarningSetTests
{
    private static CapWarningSet Build(string name) => CapWarningSet.Build(CapReader.Read(CapFixtures.Fixture(name)));

    private static CapWarningSet BuildXml(string xml, MarineZoneLayer? zones = null) =>
        CapWarningSet.Build(CapReader.Read(Encoding.UTF8.GetBytes(xml)), zones);

    // -----------------------------------------------------------------
    // Zone resolution
    // -----------------------------------------------------------------

    [Fact]
    public void A_zone_code_area_is_drawn_as_the_zone_from_the_marine_zone_layer()
    {
        var area = Assert.Single(Build(CapFixtures.GaleWarning).Areas);

        Assert.Equal(CapShapeSource.Zones, area.ShapeSource);
        var zone = Assert.Single(area.Zones);
        Assert.Equal("PZZ560", zone.Code);
        Assert.Equal("MTR", zone.Office);
        Assert.Equal(zone.Surfaces, area.Surfaces);
        Assert.Empty(area.UnmatchedZoneCodes);
        // Monterey Bay.
        Assert.All(area.Surfaces[0].ExteriorRing, p => Assert.InRange(p.Latitude, 36.5, 37.2));
    }

    [Fact]
    public void Every_zone_of_an_area_is_resolved()
    {
        var area = Assert.Single(Build(CapFixtures.MarineWeatherStatement).Areas);

        Assert.Equal(11, area.Zones.Count);
        Assert.Contains(area.Zones, z => z.Code == "ANZ535" && z.Name == "Tidal Potomac from Key Bridge to Indian Head MD");
        Assert.True(area.Surfaces.Count >= 11);
        Assert.Empty(area.UnmatchedZoneCodes);
    }

    [Fact]
    public void A_code_the_layer_does_not_have_is_listed_and_the_rest_drawn()
    {
        var set = Build(CapFixtures.Circle);
        var zoned = set.Areas[1];

        Assert.Equal(CapShapeSource.Zones, zoned.ShapeSource);
        Assert.Equal("ANZ450", Assert.Single(zoned.Zones).Code);
        Assert.Equal(["ANZ999"], zoned.UnmatchedZoneCodes);
        Assert.Equal(["ANZ999"], set.UnmatchedZoneCodes);
        Assert.True(zoned.IsDrawnAt(null));
    }

    [Fact]
    public void An_area_whose_codes_all_fail_to_resolve_is_kept_but_not_drawn()
    {
        var set = BuildXml(Alert("a", "Alert", areas: Zones("ANZ998", "ANZ999")));

        var area = Assert.Single(set.Areas);
        Assert.Equal(CapShapeSource.None, area.ShapeSource);
        Assert.Empty(area.Surfaces);
        Assert.Equal(["ANZ998", "ANZ999"], area.UnmatchedZoneCodes);
        Assert.False(area.IsDrawnAt(null));
        Assert.Empty(set.DrawnAt(null));
    }

    [Fact]
    public void An_area_with_its_own_polygon_does_not_use_its_zone_codes()
    {
        var area = Build(CapFixtures.GeoJson).Areas[0];

        Assert.Equal(CapShapeSource.Polygon, area.ShapeSource);
        Assert.Single(area.Surfaces);
        Assert.Empty(area.Zones);
        Assert.Empty(area.UnmatchedZoneCodes);
    }

    [Fact]
    public void Codes_resolve_against_a_layer_the_host_gives()
    {
        var box = new SurfacePart([new(1, 1), new(2, 1), new(2, 2), new(1, 2), new(1, 1)], []);
        var layer = MarineZoneLayer.Create([new MarineZone("ANZ999", "Test", "TST", [box])]);

        var area = Assert.Single(BuildXml(Alert("a", "Alert", areas: Zones("anz999", "ANZ535")), layer).Areas);

        Assert.Equal("ANZ999", Assert.Single(area.Zones).Code);
        Assert.Equal(["ANZ535"], area.UnmatchedZoneCodes);
    }

    // -----------------------------------------------------------------
    // Circles
    // -----------------------------------------------------------------

    [Fact]
    public void A_circle_is_drawn_as_a_64_vertex_polygon_at_its_radius()
    {
        var area = Build(CapFixtures.Circle).Areas[0];

        Assert.Equal(CapShapeSource.Circle, area.ShapeSource);
        var ring = Assert.Single(area.Surfaces).ExteriorRing;
        Assert.Equal(65, ring.Count);
        Assert.Equal(ring[0], ring[^1]);
        Assert.Equal(64, ring.Distinct().Count());
        var centre = new GeoPosition(38.5, -74.5);
        Assert.All(ring, p => Assert.Equal(30, CapGeometry.DistanceKilometres(centre, p), 3));
        // Clockwise from north.
        Assert.True(ring[0].Latitude > 38.5);
        Assert.Equal(-74.5, ring[0].Longitude, 9);
        Assert.True(ring[16].Longitude > -74.5);
    }

    [Fact]
    public void A_circle_across_the_antimeridian_keeps_longitudes_in_range()
    {
        var ring = CapGeometry.CircleToPolygon(new CapCircle(new GeoPosition(0, 179.9), 50));

        Assert.All(ring, p => Assert.InRange(p.Longitude, -180, 180));
        Assert.Contains(ring, p => p.Longitude < 0);
    }

    // -----------------------------------------------------------------
    // In force
    // -----------------------------------------------------------------

    [Fact]
    public void A_warning_is_in_force_from_onset_to_the_later_of_expires_and_the_hazard_end()
    {
        var area = Assert.Single(Build(CapFixtures.GaleWarning).Areas);

        // expires (00:00Z on the 13th) is before onset; the hazard ends 04:00Z on the 14th.
        Assert.Equal(new NavigationalWarningValidity(CapFixtures.GaleOnset, CapFixtures.GaleEnds), area.Validity);
        Assert.Equal(NavigationalWarningStatus.NotYetInForce, area.StatusAt(CapFixtures.GaleOnset.AddTicks(-1)));
        Assert.Equal(NavigationalWarningStatus.InForce, area.StatusAt(CapFixtures.GaleOnset));
        Assert.Equal(NavigationalWarningStatus.InForce, area.StatusAt(CapFixtures.GaleEnds));
        Assert.Equal(NavigationalWarningStatus.Ended, area.StatusAt(CapFixtures.GaleEnds.AddTicks(1)));

        Assert.False(area.IsDrawnAt(CapFixtures.GaleOnset.AddMinutes(-1)));
        Assert.True(area.IsDrawnAt(CapFixtures.GaleOnset.AddHours(1)));
        Assert.False(area.IsDrawnAt(CapFixtures.GaleEnds.AddMinutes(1)));
        Assert.True(area.IsDrawnAt(null));
    }

    [Fact]
    public void The_in_force_rule_is_the_one_S124_warnings_use()
    {
        var area = Assert.Single(Build(CapFixtures.GaleWarning).Areas);
        var s124 = new NavigationalWarningInfo { Validity = [area.Validity] };

        foreach (var time in new[] { CapFixtures.GaleOnset.AddHours(-1), CapFixtures.GaleOnset, CapFixtures.GaleEnds, CapFixtures.GaleEnds.AddSeconds(1) })
            Assert.Equal(s124.StatusAt(time), area.StatusAt(time));
    }

    [Fact]
    public void Onset_falls_back_to_effective_then_sent_and_an_open_end_never_ends()
    {
        var xml = Alert("a", "Alert").Replace("<onset>2026-08-28T15:00:00-00:00</onset>", "").Replace("<expires>2026-08-28T18:00:00-00:00</expires>", "");

        var area = Assert.Single(BuildXml(xml).Areas);

        Assert.Equal(new NavigationalWarningValidity(new DateTimeOffset(2026, 8, 28, 15, 0, 0, TimeSpan.Zero), null), area.Validity);
        Assert.True(area.IsDrawnAt(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void The_changes_are_every_start_and_the_tick_after_every_end()
    {
        var set = Build(CapFixtures.GeoJson);

        Assert.Equal(
            [
                new DateTime(2026, 8, 28, 15, 12, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 28, 16, 15, 0, DateTimeKind.Utc).AddTicks(1),
                new DateTime(2026, 8, 28, 22, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc).AddTicks(1),
            ],
            set.Changes);

        // Between two changes the set in force doesn't change.
        DateTimeOffset At(DateTime t) => new(t);
        for (var i = 0; i + 1 < set.Changes.Count; i++)
        {
            var at = set.DrawnAt(At(set.Changes[i])).Select(a => a.Id).ToArray();
            var before = set.DrawnAt(At(set.Changes[i + 1].AddTicks(-1))).Select(a => a.Id).ToArray();
            Assert.Equal(at, before);
        }
    }

    [Fact]
    public void Only_the_warnings_in_force_are_drawn_at_a_time()
    {
        var set = Build(CapFixtures.GeoJson);
        string[] Drawn(int hour, int minute) =>
            set.DrawnAt(new DateTimeOffset(2026, 8, 28, hour, minute, 0, TimeSpan.Zero)).Select(a => a.Info.Event).ToArray();

        Assert.Empty(Drawn(15, 0));
        Assert.Equal(["Special Marine Warning"], Drawn(15, 15));
        Assert.Equal(["Special Marine Warning"], Drawn(16, 15));
        Assert.Empty(Drawn(17, 0));
        Assert.Equal(["Small Craft Advisory"], Drawn(22, 30));
        Assert.Equal(2, set.DrawnAt(null).Count());
    }

    [Fact]
    public void An_update_ends_the_alert_it_references_from_when_it_was_sent()
    {
        var updated = Alert("a", "Alert", sent: "2026-08-28T14:00:00-00:00");
        var update = Alert("b", "Update", references: "test@example.invalid,a,2026-08-28T14:00:00-00:00", sent: "2026-08-28T16:00:00-00:00",
            onset: "2026-08-28T16:00:00-00:00", expires: "2026-08-28T20:00:00-00:00", severity: "Severe");
        var set = CapWarningSet.Build(new CapDocument(CapFormat.Atom, [Read(updated), Read(update)]));

        var (first, second) = (set.Areas[0], set.Areas[1]);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 16, 0, 0, TimeSpan.Zero), first.SupersededAt);
        Assert.Equal("b", first.SupersededBy);
        Assert.True(first.IsDrawnAt(new DateTimeOffset(2026, 8, 28, 15, 59, 0, TimeSpan.Zero)));
        Assert.False(first.IsDrawnAt(new DateTimeOffset(2026, 8, 28, 16, 0, 0, TimeSpan.Zero)));
        Assert.True(second.IsDrawnAt(new DateTimeOffset(2026, 8, 28, 16, 0, 0, TimeSpan.Zero)));
        // With no time, only the latest version.
        Assert.Equal(["b"], set.DrawnAt(null).Select(a => a.Alert.Identifier));
        Assert.Contains(new DateTime(2026, 8, 28, 16, 0, 0, DateTimeKind.Utc), set.Changes);
    }

    [Fact]
    public void A_cancellation_ends_the_alert_and_is_not_drawn_itself()
    {
        var cancelled = Alert("a", "Alert");
        var cancel = Alert("c", "Cancel", references: "test@example.invalid,a,2026-08-28T15:00:00-00:00", sent: "2026-08-28T16:30:00-00:00");
        var set = CapWarningSet.Build(new CapDocument(CapFormat.Atom, [Read(cancelled), Read(cancel)]));

        Assert.False(set.Areas[1].IsDrawable);
        Assert.Empty(set.DrawnAt(null));
        Assert.Equal(["a"], set.DrawnAt(new DateTimeOffset(2026, 8, 28, 16, 0, 0, TimeSpan.Zero)).Select(a => a.Alert.Identifier));
        Assert.Empty(set.DrawnAt(new DateTimeOffset(2026, 8, 28, 16, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void An_info_repeating_another_in_a_second_language_is_drawn_once()
    {
        var spanish = """
            <info><language>es-US</language><category>Met</category><event>Aviso de prueba</event>
            <urgency>Expected</urgency><severity>Moderate</severity><certainty>Likely</certainty>
            <area><areaDesc>Caja</areaDesc><polygon>38,-75 39,-75 39,-74 38,-74 38,-75</polygon></area></info>
            """;

        var set = BuildXml(Alert("a", "Alert", extraInfo: spanish));

        Assert.Equal("Test Warning", Assert.Single(set.Areas).Info.Event);
    }

    [Fact]
    public void Area_ids_are_stable_and_unique()
    {
        var set = Build(CapFixtures.Atom);

        Assert.Equal(["alert1.info1.area1", "alert2.info1.area1", "alert3.info1.area1"], set.Areas.Select(a => a.Id));
    }

    private static CapAlert Read(string xml) => Assert.Single(CapReader.Read(Encoding.UTF8.GetBytes(xml)).Alerts);

    private static string Zones(params string[] codes) =>
        "<area><areaDesc>Zones</areaDesc>"
        + string.Concat(codes.Select(c => $"<geocode><valueName>UGC</valueName><value>{c}</value></geocode>"))
        + "</area>";
}
