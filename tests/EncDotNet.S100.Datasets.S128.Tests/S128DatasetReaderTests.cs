using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S128.Tests;

/// <summary>
/// Tests for <see cref="S128DatasetReader"/> using the official S-128 2.0.0
/// sample dataset (<c>S128_TDS_sample.gml</c>) and synthetic fixtures.
/// </summary>
public class S128DatasetReaderTests
{
    private const string TestDataDir = "TestData";
    private const string SampleFile = "S128_TDS_sample.gml";

    private static S128Dataset LoadSample()
    {
        var path = Path.Combine(TestDataDir, SampleFile);
        Assert.True(File.Exists(path), $"Test data file not found: {path}");
        return S128Dataset.Open(path);
    }

    [Fact]
    public void Sample_ParsesProductIdentifier()
    {
        var ds = LoadSample();
        Assert.NotNull(ds.ProductIdentifier);
        Assert.Contains("S-128", ds.ProductIdentifier!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sample_HasExpectedFeatureCount()
    {
        // Sample contains 9 feature instances inside the inline <S128:members>
        // container: 2× ElectronicProduct, 2× PhysicalProduct, 1× S100Service,
        // 1× DistributorInformation, 1× CatalogueSectionHeader,
        // 1× ContactDetails, 1× ProducerInformation.
        var ds = LoadSample();
        Assert.Equal(9, ds.Features.Length);
    }

    [Fact]
    public void Sample_ContainsExpectedFeatureTypes()
    {
        var ds = LoadSample();
        var types = ds.Features.Select(f => f.FeatureType).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("ElectronicProduct", types);
        Assert.Contains("PhysicalProduct", types);
        Assert.Contains("S100Service", types);
        Assert.Contains("DistributorInformation", types);
        Assert.Contains("CatalogueSectionHeader", types);
    }

    [Fact]
    public void Sample_ElectronicProduct_HasSurfaceGeometry()
    {
        var ds = LoadSample();
        var f = ds.Features.First(x => x.Id == "ID0002");

        Assert.Equal("ElectronicProduct", f.FeatureType);
        Assert.Equal(GmlGeometryType.Surface, f.GeometryType);
        Assert.True(f.ExteriorRing.Length > 2);
        // S-100 Part 10b convention: lat lon for EPSG:4326. Sample is Korean
        // waters, so latitude ≈ 32–40°, longitude ≈ 122–135°.
        Assert.All(f.ExteriorRing, p =>
        {
            Assert.InRange(p.Latitude, 30.0, 45.0);
            Assert.InRange(p.Longitude, 120.0, 140.0);
        });
    }

    [Fact]
    public void Sample_DistributorInformation_HasNoGeometry()
    {
        var ds = LoadSample();
        var f = ds.Features.First(x => x.Id == "CNP00007");

        Assert.Equal("DistributorInformation", f.FeatureType);
        Assert.Equal(GmlGeometryType.None, f.GeometryType);
        Assert.True(f.ExteriorRing.IsDefaultOrEmpty);
    }

    [Fact]
    public void Sample_PreservesXlinkReferences()
    {
        // ElectronicProduct ID0002 should reference its catalogueHeader /
        // elementContainer via xlink:href.
        var ds = LoadSample();
        var f = ds.Features.First(x => x.Id == "ID0002");
        Assert.True(f.References.Length >= 1);
        Assert.All(f.References, r => Assert.False(string.IsNullOrEmpty(r.Href)));
    }

    [Fact]
    public void Sample_Entries_ProjectsOnlyProductFeatures()
    {
        var ds = LoadSample();
        // Five product features (2 ElectronicProduct + 2 PhysicalProduct +
        // 1 S100Service); the remaining four are metadata records.
        Assert.Equal(5, ds.Entries.Count);
        Assert.All(ds.Entries, e =>
            Assert.Contains(e.FeatureType,
                new[] { "ElectronicProduct", "PhysicalProduct", "S100Service" }));
    }

    [Fact]
    public void Sample_Entry_ExposesProductSpecificationName()
    {
        var ds = LoadSample();
        var specs = ds.Entries
            .Select(e => e.ProductSpecificationName)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        // The sample binds at least one entry to a recognised IHO spec.
        Assert.NotEmpty(specs);
    }

    /// <summary>
    /// Verifies the producer-bug detection: when a posList is emitted in
    /// lon-lat order while the bounding envelope is correctly lat-lon, the
    /// reader detects the mismatch and swaps axes.
    /// </summary>
    [Fact]
    public void Reader_SwapsAxes_WhenPosListIsLonLatButEnvelopeIsLatLon()
    {
        const string gml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <S128:Dataset xmlns:S128="http://www.iho.int/S128/2.0"
                          xmlns:gml="http://www.opengis.net/gml/3.2"
                          xmlns:S100="http://www.iho.int/s100gml/5.0"
                          xmlns:xlink="http://www.w3.org/1999/xlink"
                          gml:id="TEST">
              <gml:boundedBy>
                <gml:Envelope srsName="EPSG:4326">
                  <gml:lowerCorner>50.20 -3.00</gml:lowerCorner>
                  <gml:upperCorner>51.00  0.00</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <S100:DatasetIdentificationInformation>
                <S100:productIdentifier>S-128</S100:productIdentifier>
              </S100:DatasetIdentificationInformation>
              <S128:members>
                <S128:ElectronicProduct gml:id="F1">
                  <S128:geometry>
                  <S100:surfaceProperty><S100:Surface gml:id="s1">
                    <gml:patches><gml:PolygonPatch>
                      <gml:exterior><gml:LinearRing>
                        <gml:posList>-2.0 50.4 -2.0 50.8 -1.0 50.8 -1.0 50.4 -2.0 50.4</gml:posList>
                      </gml:LinearRing></gml:exterior>
                    </gml:PolygonPatch></gml:patches>
                  </S100:Surface></S100:surfaceProperty>
                  </S128:geometry>
                </S128:ElectronicProduct>
              </S128:members>
            </S128:Dataset>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gml));
        var ds = S128Dataset.Open(stream);

        Assert.Single(ds.Features);
        var ring = ds.Features[0].ExteriorRing;
        Assert.All(ring, p =>
        {
            Assert.InRange(p.Latitude, 50.0, 51.0);
            Assert.InRange(p.Longitude, -3.0, 0.0);
        });
    }

    /// <summary>
    /// Verifies the comma-tuple posList variant: the reader must accept
    /// both commas and whitespace as coordinate separators.
    /// </summary>
    [Fact]
    public void Reader_ParsesPosList_WithCommaSeparatedTuples()
    {
        const string gml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <S128:Dataset xmlns:S128="http://www.iho.int/S128/2.0"
                          xmlns:gml="http://www.opengis.net/gml/3.2"
                          xmlns:S100="http://www.iho.int/s100gml/5.0"
                          xmlns:xlink="http://www.w3.org/1999/xlink"
                          gml:id="TEST">
              <gml:boundedBy>
                <gml:Envelope srsName="EPSG:4326">
                  <gml:lowerCorner>50.20 -3.00</gml:lowerCorner>
                  <gml:upperCorner>51.00  0.00</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <S100:DatasetIdentificationInformation>
                <S100:productIdentifier>S-128</S100:productIdentifier>
              </S100:DatasetIdentificationInformation>
              <S128:members>
                <S128:ElectronicProduct gml:id="F1">
                  <S128:geometry>
                  <S100:surfaceProperty><S100:Surface gml:id="s1">
                    <gml:patches><gml:PolygonPatch>
                      <gml:exterior><gml:LinearRing>
                        <gml:posList>-2.0,50.4 -2.0,50.8 -1.0,50.8 -1.0,50.4 -2.0,50.4</gml:posList>
                      </gml:LinearRing></gml:exterior>
                    </gml:PolygonPatch></gml:patches>
                  </S100:Surface></S100:surfaceProperty>
                  </S128:geometry>
                </S128:ElectronicProduct>
              </S128:members>
            </S128:Dataset>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gml));
        var ds = S128Dataset.Open(stream);

        var ring = ds.Features.Single().ExteriorRing;
        Assert.Equal(5, ring.Length);
        Assert.All(ring, p =>
        {
            Assert.InRange(p.Latitude, 50.0, 51.0);
            Assert.InRange(p.Longitude, -3.0, 0.0);
        });
    }

    [Fact]
    public void Reader_AcceptsLegacyS100GmlNamespace()
    {
        const string gml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <S128:Dataset xmlns:S128="http://www.iho.int/S128/2.0"
                          xmlns:gml="http://www.opengis.net/gml/3.2"
                          xmlns:S100="http://www.iho.int/s100gml/1.0"
                          xmlns:xlink="http://www.w3.org/1999/xlink"
                          gml:id="TEST">
              <S100:DatasetIdentificationInformation>
                <S100:productIdentifier>S-128</S100:productIdentifier>
              </S100:DatasetIdentificationInformation>
              <S128:members>
                <S128:DistributorInformation gml:id="D1">
                  <S128:distributorName>Test</S128:distributorName>
                </S128:DistributorInformation>
              </S128:members>
            </S128:Dataset>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gml));
        var ds = S128Dataset.Open(stream);
        Assert.Single(ds.Features);
        Assert.Equal("DistributorInformation", ds.Features[0].FeatureType);
    }
}
