using System.Text;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Datasets.Cap.Tests;

/// <summary>Issue #1069: reading CAP XML, Atom and NWS GeoJSON, and recognising CAP from a file's first bytes.</summary>
public sealed class CapReaderTests
{
    [Fact]
    public void A_CAP_XML_alert_with_zone_codes_only_reads_its_fields()
    {
        var document = CapReader.Read(CapFixtures.Fixture(CapFixtures.GaleWarning));

        Assert.Equal(CapFormat.CapXml, document.Format);
        var alert = Assert.Single(document.Alerts);
        Assert.Equal("urn:oid:2.49.0.1.840.0.1b8c16aeb259dc142a72b0ec82d16657b71df1b1.002.2", alert.Identifier);
        Assert.Equal("w-nws.webmaster@noaa.gov", alert.Sender);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 8, 47, 0, TimeSpan.FromHours(-7)), alert.Sent);
        Assert.Equal(CapMessageType.Alert, alert.MessageType);
        Assert.Equal("Actual", alert.Status);

        var info = Assert.Single(alert.Infos);
        Assert.Equal("Gale Warning", info.Event);
        Assert.Equal(CapSeverity.Moderate, info.Severity);
        Assert.Equal("Expected", info.Urgency);
        Assert.Equal("Likely", info.Certainty);
        Assert.Equal("NWS San Francisco CA", info.SenderName);
        Assert.Equal(CapFixtures.GaleOnset, info.Onset);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero), info.Expires);
        Assert.Equal(CapFixtures.GaleEnds, info.Ends);
        Assert.StartsWith("Gale Warning issued September 12", info.Headline);
        Assert.StartsWith("* WHAT...For the Small Craft Advisory", info.Description);
        Assert.StartsWith("Mariners should alter plans", info.Instruction);
        Assert.Contains(info.Parameters, p => p.Name == "VTEC" && p.Value.Contains("KMTR.GL.W.0029", StringComparison.Ordinal));

        var area = Assert.Single(info.Areas);
        Assert.False(area.HasShape);
        Assert.Equal(["PZZ560"], area.ZoneCodes);
        Assert.Equal("Coastal Waters from Pigeon Point to Point Pinos California out to 10 nm", area.Description);
    }

    [Fact]
    public void A_CAP_XML_alert_lists_every_zone_code_once()
    {
        var area = Assert.Single(Assert.Single(CapReader.Read(CapFixtures.Fixture(CapFixtures.MarineWeatherStatement)).Alerts).Infos[0].Areas);

        Assert.Equal(
            ["ANZ530", "ANZ531", "ANZ532", "ANZ533", "ANZ535", "ANZ536", "ANZ538", "ANZ539", "ANZ540", "ANZ541", "ANZ542"],
            area.ZoneCodes);
        Assert.Contains(area.Geocodes, g => g.Name == "SAME" && g.Value == "073535");
    }

    [Fact]
    public void NWS_GeoJSON_reads_each_feature_as_an_alert_with_its_polygon()
    {
        var document = CapReader.Read(CapFixtures.Fixture(CapFixtures.GeoJson));

        Assert.Equal(CapFormat.NwsGeoJson, document.Format);
        Assert.Equal(2, document.Alerts.Count);

        var warning = document.Alerts[0];
        Assert.Equal("urn:oid:2.49.0.1.840.0.a8794110aadd6082e6ee53e622a4841194229935.001.1", warning.Identifier);
        var info = Assert.Single(warning.Infos);
        Assert.Equal("Special Marine Warning", info.Event);
        Assert.Equal(CapSeverity.Severe, info.Severity);
        Assert.Equal("Immediate", info.Urgency);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 15, 12, 0, TimeSpan.Zero), info.Onset);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 16, 15, 0, TimeSpan.Zero), info.Ends);
        Assert.Equal("Move to safe harbor until hazardous weather passes.", info.Instruction);

        var area = Assert.Single(info.Areas);
        var ring = Assert.Single(area.Polygons);
        // GeoJSON is longitude first; positions are latitude first.
        Assert.Equal(new GeoPosition(39.22, -75), ring[0]);
        Assert.Equal(ring[0], ring[^1]);
        Assert.Equal(11, ring.Count);
        Assert.Equal(["ANZ430", "ANZ431"], area.ZoneCodes);

        var advisory = document.Alerts[1];
        Assert.Equal("Small Craft Advisory", advisory.Infos[0].Event);
        Assert.False(advisory.Infos[0].Areas[0].HasShape);
        Assert.Equal(["PZZ650"], advisory.Infos[0].Areas[0].ZoneCodes);
        Assert.Equal(new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero), advisory.Infos[0].Ends);
    }

    [Fact]
    public void The_NWS_Atom_feed_reads_each_entry_as_an_alert()
    {
        var document = CapReader.Read(CapFixtures.Fixture(CapFixtures.Atom));

        Assert.Equal(CapFormat.Atom, document.Format);
        Assert.Equal(3, document.Alerts.Count);

        var warning = document.Alerts[0];
        Assert.Equal("urn:oid:2.49.0.1.840.0.304f0e68151e6e5e27d933effd28fd9175045bdd.001.1", warning.Identifier);
        var info = Assert.Single(warning.Infos);
        Assert.Equal("Special Marine Warning", info.Event);
        Assert.Equal(CapSeverity.Severe, info.Severity);
        Assert.StartsWith("Special Marine Warning issued June 3 at 12:34PM EDT", info.Headline);
        Assert.StartsWith("SMWKEY", info.Description);
        var area = Assert.Single(info.Areas);
        Assert.Equal(new GeoPosition(24.57, -80.89), Assert.Single(area.Polygons)[0]);
        Assert.Equal(["GMZ032", "GMZ043", "GMZ053"], area.ZoneCodes);

        Assert.All(document.Alerts.Skip(1), a => Assert.Equal("Small Craft Advisory", a.Infos[0].Event));
        Assert.Equal(["GMZ475"], document.Alerts[1].Infos[0].Areas[0].ZoneCodes);
        Assert.Equal(new DateTimeOffset(2026, 6, 5, 12, 0, 0, TimeSpan.Zero), document.Alerts[1].Infos[0].Ends);
    }

    [Fact]
    public void An_Atom_entry_holding_a_whole_alert_reads_that_alert()
    {
        var alert = File.ReadAllText(CapFixtures.Fixture(CapFixtures.GaleWarning));
        alert = alert[alert.IndexOf("<alert", StringComparison.Ordinal)..];
        var feed = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Alerts</title>
              <entry><id>one</id><content type="text/xml">{alert}</content></entry>
            </feed>
            """;

        var document = CapReader.Read(Encoding.UTF8.GetBytes(feed));

        Assert.Equal(CapFormat.Atom, document.Format);
        Assert.Equal("Gale Warning", Assert.Single(document.Alerts).Infos[0].Event);
    }

    [Fact]
    public void A_circle_is_read_with_its_radius_in_kilometres()
    {
        var info = Assert.Single(CapReader.Read(CapFixtures.Fixture(CapFixtures.Circle)).Alerts).Infos[0];

        Assert.Equal(CapSeverity.Extreme, info.Severity);
        Assert.Equal(new CapCircle(new GeoPosition(38.5, -74.5), 30), Assert.Single(info.Areas[0].Circles));
        Assert.Equal(["ANZ450", "ANZ999"], info.Areas[1].ZoneCodes);
    }

    [Theory]
    [InlineData("38.5,-74.5 38.6,-74.5 38.6,-74.4 38.5,-74.5", 4)]
    [InlineData("38.5,-74.5 38.6,-74.5 38.6,-74.4", 4)]
    [InlineData("  38.5,-74.5\n38.6,-74.5\t38.6,-74.4 38.5,-74.5 ", 4)]
    public void A_polygon_is_closed_latitude_first(string text, int count)
    {
        var ring = CapReader.ParsePolygon(text);

        Assert.NotNull(ring);
        Assert.Equal(count, ring.Count);
        Assert.Equal(new GeoPosition(38.5, -74.5), ring[0]);
        Assert.Equal(ring[0], ring[^1]);
    }

    [Theory]
    [InlineData("38.5,-74.5 38.6,-74.5")]
    [InlineData("38.5;-74.5 38.6,-74.5 38.6,-74.4")]
    [InlineData("98.5,-74.5 38.6,-74.5 38.6,-74.4")]
    [InlineData("")]
    public void A_polygon_that_cannot_be_read_is_skipped(string text) =>
        Assert.Null(CapReader.ParsePolygon(text));

    [Theory]
    [InlineData("38.5,-74.5")]
    [InlineData("38.5,-74.5 -1")]
    [InlineData("38.5,-74.5 x")]
    public void A_circle_that_cannot_be_read_is_skipped(string text) =>
        Assert.Null(CapReader.ParseCircle(text));

    [Fact]
    public void Updates_and_cancellations_list_the_identifiers_they_reference()
    {
        var xml = Alert("b", "Cancel", references: "w-nws.webmaster@noaa.gov,a,2026-08-28T15:00:00-00:00 other@x,c,2026-08-28T15:10:00-00:00");

        var alert = Assert.Single(CapReader.Read(Encoding.UTF8.GetBytes(xml)).Alerts);

        Assert.Equal(CapMessageType.Cancel, alert.MessageType);
        Assert.Equal(["a", "c"], alert.References);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    [InlineData("<root/>")]
    [InlineData("<alert>")]
    [InlineData("{\"type\":\"Point\",\"coordinates\":[0,0]}")]
    [InlineData("{not json")]
    public void Something_that_is_not_CAP_fails_with_a_format_error(string text) =>
        Assert.Throws<CapFormatException>(() => CapReader.Read(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void A_DTD_is_refused()
    {
        var xml = """<?xml version="1.0"?><!DOCTYPE alert [<!ENTITY x "y">]><alert xmlns="urn:oasis:names:tc:emergency:cap:1.2"><identifier>&x;</identifier></alert>""";

        Assert.Throws<CapFormatException>(() => CapReader.Read(Encoding.UTF8.GetBytes(xml)));
    }

    // -----------------------------------------------------------------
    // Recognising CAP from the leading bytes
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(CapFixtures.GaleWarning)]
    [InlineData(CapFixtures.MarineWeatherStatement)]
    [InlineData(CapFixtures.GeoJson)]
    [InlineData(CapFixtures.Atom)]
    [InlineData(CapFixtures.Circle)]
    public void Every_fixture_looks_like_CAP_from_its_first_kilobyte(string name)
    {
        var bytes = File.ReadAllBytes(CapFixtures.Fixture(name));

        Assert.True(CapReader.LooksLikeCap(bytes.AsSpan(0, Math.Min(1024, bytes.Length))));
    }

    [Fact]
    public void A_byte_order_mark_and_leading_whitespace_are_skipped()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("\r\n  " + Alert("a", "Alert"))).ToArray();

        Assert.True(CapReader.LooksLikeCap(bytes));
        Assert.Equal("a", Assert.Single(CapReader.Read(bytes).Alerts).Identifier);
    }

    [Theory]
    // S-124 GML: a DataSet root in an S-100 namespace.
    [InlineData("""<?xml version="1.0" encoding="UTF-8"?><S124:DataSet xmlns:S124="http://www.iho.int/S124/1.0" xmlns:gml="http://www.opengis.net/gml/3.2" gml:id="DS1"><S124:NavwarnPart/></S124:DataSet>""")]
    // An exchange set catalogue.
    [InlineData("""<?xml version="1.0"?><S100XC:S100_ExchangeCatalogue xmlns:S100XC="http://www.iho.int/s100/xc/5.0"/>""")]
    // A plain Atom feed.
    [InlineData("""<?xml version="1.0"?><feed xmlns="http://www.w3.org/2005/Atom"><title>News</title></feed>""")]
    // An alert root in no CAP namespace.
    [InlineData("""<alert><identifier>x</identifier></alert>""")]
    // A CAP namespace on a root that isn't an alert or a feed.
    [InlineData("""<doc xmlns:cap="urn:oasis:names:tc:emergency:cap:1.2"/>""")]
    // GeoJSON from elsewhere, and NWS JSON that isn't an alert.
    [InlineData("""{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"name":"Alert"},"geometry":null}]}""")]
    [InlineData("""{"@context":["https://geojson.org/geojson-ld/geojson-context.jsonld",{"@vocab":"https://api.weather.gov/ontology#"}],"type":"Feature","id":"https://api.weather.gov/zones/forecast/ANZ535"}""")]
    // Binary and empty.
    [InlineData("GRIB\0\0\0\u0002")]
    [InlineData("")]
    [InlineData("<!DOCTYPE alert><alert xmlns=\"urn:oasis:names:tc:emergency:cap:1.2\"/>")]
    public void Other_files_do_not_look_like_CAP(string text) =>
        Assert.False(CapReader.LooksLikeCap(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void A_CAP_root_is_recognised_from_its_namespace_and_name()
    {
        Assert.True(CapReader.IsCapRoot(CapReader.Cap12Namespace, "alert", string.Empty));
        Assert.True(CapReader.IsCapRoot("urn:oasis:names:tc:emergency:cap:1.1", "alert", string.Empty));
        Assert.True(CapReader.IsCapRoot(CapReader.AtomNamespace, "feed", "<feed xmlns:cap=\"urn:oasis:names:tc:emergency:cap:1.2\">"));
        Assert.False(CapReader.IsCapRoot(CapReader.AtomNamespace, "feed", "<feed>"));
        Assert.False(CapReader.IsCapRoot("http://www.iho.int/S124/1.0", "DataSet", CapReader.Cap12Namespace));
        Assert.False(CapReader.IsCapRoot(string.Empty, "alert", string.Empty));
    }

    internal static string Alert(
        string identifier,
        string messageType,
        string? references = null,
        string sent = "2026-08-28T15:00:00-00:00",
        string onset = "2026-08-28T15:00:00-00:00",
        string expires = "2026-08-28T18:00:00-00:00",
        string severity = "Moderate",
        string areas = "<area><areaDesc>Box</areaDesc><polygon>38,-75 39,-75 39,-74 38,-74 38,-75</polygon></area>",
        string extraInfo = "") => $"""
        <alert xmlns="urn:oasis:names:tc:emergency:cap:1.2">
          <identifier>{identifier}</identifier>
          <sender>test@example.invalid</sender>
          <sent>{sent}</sent>
          <status>Actual</status>
          <msgType>{messageType}</msgType>
          <scope>Public</scope>
          {(references is null ? "" : $"<references>{references}</references>")}
          <info>
            <language>en-US</language>
            <category>Met</category>
            <event>Test Warning</event>
            <urgency>Expected</urgency>
            <severity>{severity}</severity>
            <certainty>Likely</certainty>
            <onset>{onset}</onset>
            <expires>{expires}</expires>
            {areas}
          </info>
          {extraInfo}
        </alert>
        """;
}
