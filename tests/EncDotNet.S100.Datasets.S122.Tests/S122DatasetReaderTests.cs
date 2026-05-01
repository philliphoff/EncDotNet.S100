using EncDotNet.S100.Datasets.S122;

namespace EncDotNet.S100.Datasets.S122.Tests;

/// <summary>
/// Tests for <see cref="S122DatasetReader"/> using the official S-122 2.0.0
/// sample dataset (<c>122TESTDATASET.gml</c>).
/// </summary>
public class S122DatasetReaderTests
{
    private const string TestDataDir = "TestData";
    private const string SampleFile = "122TESTDATASET.gml";

    private static S122Dataset LoadSample()
    {
        var path = Path.Combine(TestDataDir, SampleFile);
        Assert.True(File.Exists(path), $"Test data file not found: {path}");
        return S122Dataset.Open(path);
    }

    [Fact]
    public void Sample_ParsesProductIdentifier()
    {
        // The sample's <S100:productIdentifier> is "INT.IHO.S-122.1.0.0".
        var ds = LoadSample();
        Assert.NotNull(ds.ProductIdentifier);
        Assert.Contains("S-122", ds.ProductIdentifier!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sample_ParsesDatasetId()
    {
        var ds = LoadSample();
        Assert.Equal("KRNPI123_TEST002_001", ds.DatasetIdentifier);
    }

    [Fact]
    public void Sample_HasFourFeatures()
    {
        var ds = LoadSample();
        Assert.Equal(4, ds.Features.Length);
    }

    [Fact]
    public void Sample_ContainsExpectedFeatureTypes()
    {
        var ds = LoadSample();
        var types = ds.Features.Select(f => f.FeatureType).Distinct().ToHashSet();

        Assert.Contains("MarineProtectedArea", types);
        Assert.Contains("RestrictedArea", types);
        Assert.Contains("VesselTrafficServiceArea", types);
    }

    [Fact]
    public void Sample_MarineProtectedArea_Surface_HasExteriorRing()
    {
        var ds = LoadSample();
        var f = ds.Features.First(x => x.Id == "FEATURE_ID_0003");

        Assert.Equal("MarineProtectedArea", f.FeatureType);
        Assert.Equal(S122GeometryType.Surface, f.GeometryType);
        Assert.Equal(5, f.ExteriorRing.Length);
        // S-100 Part 10b convention: lat lon for EPSG:4326.
        Assert.Equal(-32.5215215, f.ExteriorRing[0].Latitude, 5);
        Assert.Equal(60.9745257, f.ExteriorRing[0].Longitude, 5);
    }

    [Fact]
    public void Sample_RestrictedArea_HasSurfaceGeometry()
    {
        var ds = LoadSample();
        var f = ds.Features.First(x => x.Id == "FEATURE_ID_0001");

        Assert.Equal("RestrictedArea", f.FeatureType);
        Assert.Equal(S122GeometryType.Surface, f.GeometryType);
        Assert.Equal(5, f.ExteriorRing.Length);
    }

    [Fact]
    public void Sample_MarineProtectedArea_Curve_HasCurveGeometry()
    {
        var ds = LoadSample();
        var f = ds.Features.First(x => x.Id == "FEATURE_ID_0004");

        Assert.Equal("MarineProtectedArea", f.FeatureType);
        Assert.Equal(S122GeometryType.Curve, f.GeometryType);
        Assert.Single(f.Curves);
        Assert.Equal(4, f.Curves[0].Length);
    }

    [Fact]
    public void Sample_VesselTrafficServiceArea_HasSurfaceGeometry()
    {
        var ds = LoadSample();
        var f = ds.Features.First(x => x.Id == "FEATURE_ID_0002");

        Assert.Equal("VesselTrafficServiceArea", f.FeatureType);
        Assert.Equal(S122GeometryType.Surface, f.GeometryType);
        Assert.Equal(5, f.ExteriorRing.Length);
    }

    [Fact]
    public void Sample_HasNoInformationTypes()
    {
        var ds = LoadSample();
        Assert.Empty(ds.InformationTypes);
    }

    /// <summary>
    /// Verifies the producer-bug detection: when a dataset's posList is
    /// emitted in lon-lat order (as in the UK trial dataset GBNPI12200002045)
    /// while the bounding envelope is correctly lat-lon, the reader detects
    /// the mismatch and swaps axes so coords end up in the spec-mandated
    /// lat-lon orientation.
    /// </summary>
    [Fact]
    public void Reader_SwapsAxes_WhenPosListIsLonLatButEnvelopeIsLatLon()
    {
        const string gml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <S122:Dataset xmlns:S122="http://www.iho.int/S122/gml/1.0"
                          xmlns:gml="http://www.opengis.net/gml/3.2"
                          xmlns:S100="http://www.iho.int/s100gml/1.0"
                          gml:id="TEST">
              <gml:boundedBy>
                <gml:Envelope srsName="EPSG:4326">
                  <gml:lowerCorner>50.20 -3.00</gml:lowerCorner>
                  <gml:upperCorner>51.00  0.00</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <S100:DatasetIdentificationInformation>
                <S100:productIdentifier>S-122</S100:productIdentifier>
              </S100:DatasetIdentificationInformation>
              <member>
                <S122:MarineProtectedArea gml:id="F1">
                  <geometry><S100:surfaceProperty><gml:Surface gml:id="s1">
                    <gml:patches><gml:PolygonPatch>
                      <gml:exterior><gml:LinearRing>
                        <gml:posList>-2.0 50.4 -2.0 50.8 -1.0 50.8 -1.0 50.4 -2.0 50.4</gml:posList>
                      </gml:LinearRing></gml:exterior>
                    </gml:PolygonPatch></gml:patches>
                  </gml:Surface></S100:surfaceProperty></geometry>
                </S122:MarineProtectedArea>
              </member>
            </S122:Dataset>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gml));
        var ds = S122Dataset.Open(stream);

        Assert.Single(ds.Features);
        var ring = ds.Features[0].ExteriorRing;
        // After swap: lat in [50.4, 50.8], lon in [-2.0, -1.0] (UK / Solent).
        Assert.All(ring, p =>
        {
            Assert.InRange(p.Latitude, 50.0, 51.0);
            Assert.InRange(p.Longitude, -3.0, 0.0);
        });
    }

    /// <summary>
    /// Confirms the spec-conformant sample is *not* swapped — its coords
    /// already fall inside the (synthesised) envelope and the heuristic must
    /// be conservative enough to leave them alone.
    /// </summary>
    [Fact]
    public void Reader_DoesNotSwap_WhenPosListIsAlreadyLatLon()
    {
        var ds = LoadSample();
        // The sample's MPA at (lat≈-32.52, lon≈60.97) — the heuristic must
        // not corrupt this even though the values look unusual, because
        // there is no envelope to compare against in the sample file.
        var ring = ds.Features.First(f => f.GeometryType == S122GeometryType.Surface).ExteriorRing;
        Assert.NotEmpty(ring);
        Assert.InRange(ring[0].Latitude, -33.0, -32.0);
        Assert.InRange(ring[0].Longitude, 60.0, 61.0);
    }

    /// <summary>
    /// Verifies the comma-tuple posList variant: UKHO's S-122 trial data
    /// emits <c>lon,lat lon,lat</c> tokens (the gml:coordinates convention)
    /// inside <c>&lt;gml:posList&gt;</c>. The reader must accept both
    /// commas and whitespace as coordinate separators.
    /// </summary>
    [Fact]
    public void Reader_ParsesPosList_WithCommaSeparatedTuples()
    {
        const string gml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <S122:Dataset xmlns:S122="http://www.iho.int/S122/gml/1.0"
                          xmlns:gml="http://www.opengis.net/gml/3.2"
                          xmlns:S100="http://www.iho.int/s100gml/1.0"
                          gml:id="TEST">
              <gml:boundedBy>
                <gml:Envelope srsName="EPSG:4326">
                  <gml:lowerCorner>50.20 -3.00</gml:lowerCorner>
                  <gml:upperCorner>51.00  0.00</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <S100:DatasetIdentificationInformation>
                <S100:productIdentifier>S-122</S100:productIdentifier>
              </S100:DatasetIdentificationInformation>
              <member>
                <S122:MarineProtectedArea gml:id="F1">
                  <geometry><S100:surfaceProperty><gml:Surface gml:id="s1">
                    <gml:patches><gml:PolygonPatch>
                      <gml:exterior><gml:LinearRing>
                        <gml:posList>-2.0,50.4 -2.0,50.8 -1.0,50.8 -1.0,50.4 -2.0,50.4</gml:posList>
                      </gml:LinearRing></gml:exterior>
                    </gml:PolygonPatch></gml:patches>
                  </gml:Surface></S100:surfaceProperty></geometry>
                </S122:MarineProtectedArea>
              </member>
            </S122:Dataset>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gml));
        var ds = S122Dataset.Open(stream);

        var ring = ds.Features.Single().ExteriorRing;
        Assert.Equal(5, ring.Length);
        // After axis-swap (lon,lat token order is detected as wrong vs the
        // lat-lon envelope), each point ends up in (lat, lon) form.
        Assert.All(ring, p =>
        {
            Assert.InRange(p.Latitude, 50.0, 51.0);
            Assert.InRange(p.Longitude, -3.0, 0.0);
        });
    }
}
