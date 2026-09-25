using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Collections.Tests;

public class GmlCoverageParserTests
{
    private const string Ns =
        "xmlns:S100XC=\"http://www.iho.int/s100/xc/5.2\" xmlns:gex=\"http://standards.iso.org/iso/19115/-3/gex/1.0\" xmlns:gml=\"http://www.opengis.net/gml/3.2\"";

    [Fact]
    public void Parses_epsg4326_poslist_as_latitude_longitude()
    {
        var xml = $"""
            <S100XC:boundingPolygon {Ns}>
              <gex:polygon>
                <gml:Polygon gml:id="p1" srsName="EPSG:4326">
                  <gml:exterior><gml:LinearRing>
                    <gml:posList> 32.4 -80.1 32.4 -79.8 32.7 -79.8 32.7 -80.1 32.4 -80.1 </gml:posList>
                  </gml:LinearRing></gml:exterior>
                </gml:Polygon>
              </gex:polygon>
            </S100XC:boundingPolygon>
            """;

        var polygon = Assert.Single(GmlCoverageParser.Parse(xml));

        Assert.Equal(5, polygon.Exterior.Count);
        Assert.Equal(32.4, polygon.Exterior[0].Latitude);
        Assert.Equal(-80.1, polygon.Exterior[0].Longitude);
        Assert.Empty(polygon.Holes);
    }

    [Fact]
    public void Parses_crs84_as_longitude_latitude_with_holes_and_pos_elements()
    {
        var xml = $"""
            <S100XC:boundingPolygon {Ns}>
              <gml:Polygon srsName="urn:ogc:def:crs:OGC:1.3:CRS84">
                <gml:exterior><gml:LinearRing>
                  <gml:pos>-80 30</gml:pos><gml:pos>-70 30</gml:pos><gml:pos>-70 40</gml:pos><gml:pos>-80 30</gml:pos>
                </gml:LinearRing></gml:exterior>
                <gml:interior><gml:LinearRing>
                  <gml:posList>-75 32 -74 32 -74 33 -75 32</gml:posList>
                </gml:LinearRing></gml:interior>
              </gml:Polygon>
            </S100XC:boundingPolygon>
            """;

        var polygon = Assert.Single(GmlCoverageParser.Parse(xml));

        Assert.Equal(30, polygon.Exterior[0].Latitude);
        Assert.Equal(-80, polygon.Exterior[0].Longitude);
        var hole = Assert.Single(polygon.Holes);
        Assert.Equal(32, hole[0].Latitude);
        Assert.Equal(-75, hole[0].Longitude);
    }

    [Fact]
    public void Honours_srsDimension_three()
    {
        var xml = $"""
            <S100XC:boundingPolygon {Ns}>
              <gml:Polygon srsName="EPSG:4326"><gml:exterior><gml:LinearRing>
                <gml:posList srsDimension="3">10 20 0 10 21 0 11 21 0 10 20 0</gml:posList>
              </gml:LinearRing></gml:exterior></gml:Polygon>
            </S100XC:boundingPolygon>
            """;

        var polygon = Assert.Single(GmlCoverageParser.Parse(xml));

        Assert.Equal(4, polygon.Exterior.Count);
        Assert.Equal(11, polygon.Exterior[2].Latitude);
        Assert.Equal(21, polygon.Exterior[2].Longitude);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<not-closed")]
    [InlineData("<a xmlns:gml=\"http://www.opengis.net/gml/3.2\"><gml:Polygon><gml:exterior><gml:LinearRing><gml:posList>1 2 x 4</gml:posList></gml:LinearRing></gml:exterior></gml:Polygon></a>")]
    [InlineData("<a xmlns:gml=\"http://www.opengis.net/gml/3.2\"><gml:Polygon><gml:exterior><gml:LinearRing><gml:posList>1 2 3 4</gml:posList></gml:LinearRing></gml:exterior></gml:Polygon></a>")]
    public void Malformed_input_yields_no_polygons(string? xml)
    {
        Assert.Empty(GmlCoverageParser.Parse(xml));
    }

    [Fact]
    public void Parses_polygons_preserved_by_the_exchange_catalogue_reader()
    {
        // The reader keeps boundingPolygon as an XML string; the prefixes it
        // uses must survive that round trip (NOAA S-104, S-100 5.2).
        var catalogue = ExchangeCatalogueReader.Read(TestPaths.Fixture("noaa-s104-catalog.xml"));

        var coverage = catalogue.DatasetDiscoveryMetadata[0].DataCoverages.Single();
        var polygon = Assert.Single(GmlCoverageParser.Parse(coverage.BoundingPolygon));

        Assert.Equal(5, polygon.Exterior.Count);
        Assert.Equal(32.4, polygon.Exterior[0].Latitude, 3);
        Assert.Equal(-80.1, polygon.Exterior[0].Longitude, 3);
    }
}
