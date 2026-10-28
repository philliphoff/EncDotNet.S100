using System.Text;

namespace EncDotNet.S100.Datasets.Cap.Tests;

/// <summary>Issue #1069: the bundled NWS marine zone layer, and reading another.</summary>
public sealed class MarineZoneLayerTests
{
    [Fact]
    public void The_bundled_layer_holds_the_NWS_coastal_and_offshore_zones()
    {
        var layer = MarineZoneLayer.Default;

        Assert.Equal(696, layer.Count);
        Assert.Contains("mz18mr25", layer.Source);
        Assert.Contains("oz18mr25", layer.Source);
        // Every marine UGC area prefix, coastal (ANZ5xx) and offshore (ANZ8xx).
        foreach (var code in new[] { "ANZ535", "ANZ800", "AMZ040", "GMZ475", "PZZ560", "PZZ650", "PKZ731", "PHZ110", "LMZ043", "LEZ148", "SLZ022" })
            Assert.NotNull(layer.Find(code));
    }

    [Fact]
    public void A_zone_has_its_name_office_and_outline()
    {
        var zone = MarineZoneLayer.Default.Find("anz535");

        Assert.NotNull(zone);
        Assert.Equal("ANZ535", zone.Code);
        Assert.Equal("Tidal Potomac from Key Bridge to Indian Head MD", zone.Name);
        Assert.Equal("LWX", zone.Office);
        var ring = zone.Surfaces[0].ExteriorRing;
        Assert.Equal(ring[0], ring[^1]);
        // Along the Potomac between Washington and Indian Head.
        Assert.All(ring, p =>
        {
            Assert.InRange(p.Latitude, 38.5, 38.95);
            Assert.InRange(p.Longitude, -77.3, -76.9);
        });
    }

    [Fact]
    public void Every_zone_has_a_closed_outline_in_range()
    {
        foreach (var code in MarineZoneLayer.Default.Codes)
        {
            var zone = MarineZoneLayer.Default.Find(code)!;
            Assert.NotEmpty(zone.Surfaces);
            foreach (var surface in zone.Surfaces)
            {
                Assert.True(surface.ExteriorRing.Count >= 4, code);
                Assert.Equal(surface.ExteriorRing[0], surface.ExteriorRing[^1]);
                Assert.All(surface.ExteriorRing, p =>
                {
                    Assert.InRange(p.Latitude, -20, 75);
                    // The NWS Bering Sea zones run a hundredth of a degree past 180°.
                    Assert.InRange(p.Longitude, -180.1, 180.1);
                });
            }
        }
    }

    [Fact]
    public void An_unknown_code_finds_nothing() =>
        Assert.Null(MarineZoneLayer.Default.Find("ANZ999"));

    [Fact]
    public void A_layer_in_the_NWS_shapefile_attribute_form_reads_too()
    {
        const string json = """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{"ID":"XXZ001","WFO":"TST","NAME":"Test zone"},
               "geometry":{"type":"MultiPolygon","coordinates":[
                 [[[0,0],[1,0],[1,1],[0,1],[0,0]],[[0.2,0.2],[0.4,0.2],[0.4,0.4],[0.2,0.2]]],
                 [[[2,2],[3,2],[3,3],[2,2]]]]}},
              {"type":"Feature","properties":{"ID":"XXZ002"},"geometry":null}
            ]}
            """;

        var layer = MarineZoneLayer.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        var zone = Assert.Single(layer.Codes.Select(layer.Find));
        Assert.Equal("XXZ001", zone!.Code);
        Assert.Equal("Test zone", zone.Name);
        Assert.Equal("TST", zone.Office);
        Assert.Equal(2, zone.Surfaces.Count);
        Assert.Single(zone.Surfaces[0].InteriorRings);
        Assert.Equal(new DataModel.GeoPosition(0, 1), zone.Surfaces[0].ExteriorRing[1]);
    }

    [Fact]
    public void The_zones_in_an_area_are_those_whose_outline_reaches_into_it()
    {
        // Around the Potomac between Washington and Indian Head (issue #1075: alerts for the map view).
        var codes = MarineZoneLayer.Default.CodesIn(38.6, -77.2, 38.8, -77.0);

        Assert.Contains("ANZ535", codes);
        Assert.DoesNotContain("PZZ560", codes);
        Assert.Equal(codes.Order(StringComparer.Ordinal), codes);
        Assert.Empty(MarineZoneLayer.Default.CodesIn(-10, 60, -5, 65));
        // Across the antimeridian, the Aleutians' zones.
        Assert.Contains(MarineZoneLayer.Default.CodesIn(50, 170, 56, -170), c => c.StartsWith("PKZ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_layer_that_is_not_a_feature_collection_fails() =>
        Assert.Throws<CapFormatException>(() => MarineZoneLayer.Load(new MemoryStream("[1,2]"u8.ToArray())));
}
