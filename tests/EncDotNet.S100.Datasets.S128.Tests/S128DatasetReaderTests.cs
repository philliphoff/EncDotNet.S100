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

    /// <summary>
    /// Variant A producer bug (S-128 GML 1.0 IC-ENC/DK): polygons encoded as
    /// <c>&lt;gml:Polygon&gt;&lt;gml:posList&gt;</c> with no
    /// <c>&lt;gml:exterior&gt;/&lt;gml:LinearRing&gt;</c> wrapper, and features
    /// keyed by the non-standard <c>gml:gmlId</c>. The reader must still parse
    /// the exterior ring and populate the feature identifier (otherwise the
    /// geometry provider drops the feature and the dataset renders blank — see
    /// issue #243).
    /// </summary>
    [Fact]
    public void Reader_ParsesExteriorlessPolygon_AndGmlIdFallback()
    {
        const string gml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <S128:Dataset xmlns:S128="http://www.iho.int/S128/gml/1.0"
                          xmlns:gml="http://www.opengis.net/gml/3.2"
                          xmlns:S100="http://www.iho.int/s100gml/1.0"
                          xmlns:xlink="http://www.w3.org/1999/xlink"
                          gml:id="TEST">
              <S100:DatasetIdentificationInformation>
                <S100:productIdentifier>S-128</S100:productIdentifier>
              </S100:DatasetIdentificationInformation>
              <member>
                <S128:ElectronicChart gml:gmlId="GST.ElectronicChart.DK1">
                  <geometry>
                    <S100:surfaceProperty>
                      <gml:Polygon>
                        <gml:posList>50.4 -2.0 50.8 -2.0 50.8 -1.0 50.4 -1.0 50.4 -2.0</gml:posList>
                      </gml:Polygon>
                    </S100:surfaceProperty>
                  </geometry>
                </S128:ElectronicChart>
              </member>
            </S128:Dataset>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gml));
        var ds = S128Dataset.Open(stream);

        var f = ds.Features.Single();
        Assert.Equal("ElectronicChart", f.FeatureType);
        Assert.Equal("GST.ElectronicChart.DK1", f.Id);
        Assert.Equal(GmlGeometryType.Surface, f.GeometryType);
        Assert.Equal(5, f.ExteriorRing.Length);
        Assert.All(f.ExteriorRing, p =>
        {
            Assert.InRange(p.Latitude, 50.0, 51.0);
            Assert.InRange(p.Longitude, -3.0, 0.0);
        });
    }

    /// <summary>
    /// Variant B producer bug (S-128 GML 1.0 IC-ENC): each ordinate is emitted
    /// in its own single-value <c>&lt;gml:pos&gt;</c> element rather than as
    /// full coordinate tuples. The reader must flatten and pair the ordinates
    /// into (lat, lon) coordinates (otherwise the ring is empty and the dataset
    /// renders blank — see issue #243).
    /// </summary>
    [Fact]
    public void Reader_ParsesSingleOrdinatePosElements()
    {
        const string gml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <S128:Dataset xmlns:S128="http://www.iho.int/S128/gml/1.0"
                          xmlns:gml="http://www.opengis.net/gml/3.2"
                          xmlns:S100="http://www.iho.int/s100gml/1.0"
                          xmlns:xlink="http://www.w3.org/1999/xlink"
                          gml:id="TEST">
              <S100:DatasetIdentificationInformation>
                <S100:productIdentifier>S-128</S100:productIdentifier>
              </S100:DatasetIdentificationInformation>
              <member>
                <S128:ElectronicChart gml:id="E001">
                  <geometry>
                    <S100:surfaceProperty>
                      <gml:Surface gml:id="S1">
                        <gml:patches>
                          <gml:PolygonPatch>
                            <gml:exterior>
                              <gml:LinearRing>
                                <gml:pos>41.68</gml:pos><gml:pos>21.61</gml:pos>
                                <gml:pos>41.68</gml:pos><gml:pos>22.90</gml:pos>
                                <gml:pos>40.10</gml:pos><gml:pos>22.90</gml:pos>
                                <gml:pos>40.10</gml:pos><gml:pos>21.61</gml:pos>
                                <gml:pos>41.68</gml:pos><gml:pos>21.61</gml:pos>
                              </gml:LinearRing>
                            </gml:exterior>
                          </gml:PolygonPatch>
                        </gml:patches>
                      </gml:Surface>
                    </S100:surfaceProperty>
                  </geometry>
                </S128:ElectronicChart>
              </member>
            </S128:Dataset>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gml));
        var ds = S128Dataset.Open(stream);

        var f = ds.Features.Single();
        Assert.Equal(GmlGeometryType.Surface, f.GeometryType);
        Assert.Equal(5, f.ExteriorRing.Length);
        Assert.All(f.ExteriorRing, p =>
        {
            Assert.InRange(p.Latitude, 40.0, 42.0);
            Assert.InRange(p.Longitude, 21.0, 23.0);
        });
    }

    /// <summary>
    /// When a dataset carries geometry but no <c>&lt;gml:Envelope&gt;</c>, the
    /// axis-order heuristic cannot run and the reader assumes lat-lon. That
    /// assumption must be surfaced as a diagnostic (never silent) so a
    /// lon-lat producer cannot yield a plausible-but-mirrored chart unnoticed.
    /// </summary>
    [Fact]
    public void Reader_EmitsAxisOrderWarning_WhenGeometryHasNoEnvelope()
    {
        const string gml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <S128:Dataset xmlns:S128="http://www.iho.int/S128/gml/1.0"
                          xmlns:gml="http://www.opengis.net/gml/3.2"
                          xmlns:S100="http://www.iho.int/s100gml/1.0"
                          xmlns:xlink="http://www.w3.org/1999/xlink"
                          gml:id="TEST">
              <S100:DatasetIdentificationInformation>
                <S100:productIdentifier>S-128</S100:productIdentifier>
              </S100:DatasetIdentificationInformation>
              <member>
                <S128:ElectronicChart gml:gmlId="GST.ElectronicChart.DK1">
                  <geometry>
                    <S100:surfaceProperty>
                      <gml:Polygon>
                        <gml:posList>50.4 -2.0 50.8 -2.0 50.8 -1.0 50.4 -1.0 50.4 -2.0</gml:posList>
                      </gml:Polygon>
                    </S100:surfaceProperty>
                  </geometry>
                </S128:ElectronicChart>
              </member>
            </S128:Dataset>
            """;

        var activities = CaptureOwnThreadActivities(() =>
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gml));
            _ = S128Dataset.Open(stream);
        });

        var open = activities.Single(a => a.OperationName == "s100.dataset.open");
        Assert.Contains(open.Events, e => e.Name == "s100.s128.axisOrderAssumed");
        Assert.Equal(true, open.GetTagItem("s100.s128.axisOrderAssumed"));
    }

    /// <summary>
    /// The axis-order warning must NOT fire when an envelope is present (the
    /// heuristic can verify the ordering).
    /// </summary>
    [Fact]
    public void Reader_DoesNotEmitAxisOrderWarning_WhenEnvelopePresent()
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
                        <gml:posList>50.4 -2.0 50.8 -2.0 50.8 -1.0 50.4 -1.0 50.4 -2.0</gml:posList>
                      </gml:LinearRing></gml:exterior>
                    </gml:PolygonPatch></gml:patches>
                  </S100:Surface></S100:surfaceProperty>
                  </S128:geometry>
                </S128:ElectronicProduct>
              </S128:members>
            </S128:Dataset>
            """;

        var activities = CaptureOwnThreadActivities(() =>
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gml));
            _ = S128Dataset.Open(stream);
        });

        var open = activities.Single(a => a.OperationName == "s100.dataset.open");
        Assert.DoesNotContain(open.Events, e => e.Name == "s100.s128.axisOrderAssumed");
        Assert.Null(open.GetTagItem("s100.s128.axisOrderAssumed"));
    }

    /// <summary>
    /// Captures S-128 activities produced solely by the supplied synchronous
    /// <paramref name="body"/> on the current test thread.
    /// </summary>
    /// <remarks>
    /// The <see cref="System.Diagnostics.ActivityListener"/> is process-global,
    /// so when xUnit runs S-128 test classes in parallel, activities from other
    /// concurrent <c>S128Dataset.Open(...)</c> calls would otherwise leak into
    /// the capture and break a <c>Single(...)</c> assertion. Because
    /// <c>S128Dataset.Open</c> is fully synchronous, its root activity starts
    /// and stops on this test's own managed thread; we therefore record only the
    /// activity ids started on that thread and keep only those when stopped.
    /// </remarks>
    /// <param name="body">The synchronous action whose activities to capture.</param>
    /// <returns>The activities started and stopped on the current test thread.</returns>
    private static List<System.Diagnostics.Activity> CaptureOwnThreadActivities(Action body)
    {
        int testThreadId = Environment.CurrentManagedThreadId;
        var ownThreadActivityIds = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        var activities = new System.Collections.Concurrent.ConcurrentBag<System.Diagnostics.Activity>();

        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = src => src.Name == "EncDotNet.S100.Datasets.S128",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                if (Environment.CurrentManagedThreadId == testThreadId)
                {
                    ownThreadActivityIds.TryAdd(activity.Id!, 0);
                }
            },
            ActivityStopped = activity =>
            {
                if (activity.Id is not null && ownThreadActivityIds.ContainsKey(activity.Id))
                {
                    activities.Add(activity);
                }
            },
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        body();

        return activities.ToList();
    }
}
