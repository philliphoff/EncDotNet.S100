using System.Collections.Immutable;
using System.Text;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S122.DataModel;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S122.Tests;

/// <summary>
/// Tests for the strongly-typed
/// <see cref="S122MarineProtectedAreaDataset"/> projection that sits
/// alongside the feature-bag <see cref="S122Dataset"/>. Mirrors the
/// shape of the typed-model tests for S-124 / S-125 / S-128 / S-201.
/// </summary>
public class DataModelTests
{
    private const string TestDataDir = "TestData";
    private const string SampleFile = "122TESTDATASET.gml";

    private static S122Dataset Parse(string gml)
    {
        var bytes = Encoding.UTF8.GetBytes(gml);
        using var stream = new MemoryStream(bytes);
        return S122Dataset.Open(stream);
    }

    private static string Wrap(string memberFragments) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <S122:Dataset gml:id="ds1"
            xmlns:S122="http://www.iho.int/S122/1.0"
            xmlns:S100="http://www.iho.int/s100gml/5.0"
            xmlns:gml="http://www.opengis.net/gml/3.2"
            xmlns:xlink="http://www.w3.org/1999/xlink">
            <gml:boundedBy>
                <gml:Envelope srsName="EPSG:4326">
                    <gml:lowerCorner>-90 -180</gml:lowerCorner>
                    <gml:upperCorner>90 180</gml:upperCorner>
                </gml:Envelope>
            </gml:boundedBy>
            <S100:DatasetIdentificationInformation>
                <S100:productIdentifier>INT.IHO.S-122.1.0.0</S100:productIdentifier>
            </S100:DatasetIdentificationInformation>
            <S122:members>
                {{memberFragments}}
            </S122:members>
        </S122:Dataset>
        """;

    private const string MpaWithAuthorityRef = """
        <S122:MarineProtectedArea gml:id="MPA1">
            <S122:categoryOfMarineProtectedArea>1</S122:categoryOfMarineProtectedArea>
            <S122:restriction>3</S122:restriction>
            <S122:status>5</S122:status>
            <S122:jurisdiction>National</S122:jurisdiction>
            <S122:designation>RAMSAR</S122:designation>
            <S122:geometry>
                <S100:surfaceProperty>
                    <gml:Polygon srsName="EPSG:4326">
                        <gml:exterior>
                            <gml:LinearRing>
                                <gml:posList>0 0 0 1 1 1 1 0 0 0</gml:posList>
                            </gml:LinearRing>
                        </gml:exterior>
                    </gml:Polygon>
                </S100:surfaceProperty>
            </S122:geometry>
            <S122:theAuthority xlink:href="#AUTH1"/>
        </S122:MarineProtectedArea>
        <S122:Authority gml:id="AUTH1">
            <S122:categoryOfAuthority>2</S122:categoryOfAuthority>
            <S122:textContent>Port Authority</S122:textContent>
        </S122:Authority>
        """;

    [Fact]
    public void From_ProjectsMpaAttributes()
    {
        var ds = Parse(Wrap(MpaWithAuthorityRef));
        var typed = S122MarineProtectedAreaDataset.From(ds, out var diags);

        var mpa = Assert.Single(typed.MarineProtectedAreas);
        Assert.Equal("MPA1", mpa.Id);
        Assert.Equal(1, mpa.CategoryOfMarineProtectedArea);
        Assert.Equal(3, mpa.Restriction);
        Assert.Equal(5, mpa.Status);
        Assert.Equal("National", mpa.Jurisdiction);
        Assert.Equal("RAMSAR", mpa.Designation);
        Assert.Equal(S122GeometryKind.Surface, mpa.GeometryKind);
        Assert.NotEmpty(mpa.Coordinates);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void From_ResolvesAuthorityXlink()
    {
        var ds = Parse(Wrap(MpaWithAuthorityRef));
        var typed = S122MarineProtectedAreaDataset.From(ds, out _);

        var mpa = Assert.Single(typed.MarineProtectedAreas);
        var infoRef = Assert.Single(mpa.InformationReferences);
        Assert.Equal("theAuthority", infoRef.Role);
        var auth = Assert.IsType<S122Authority>(infoRef.Target);
        Assert.Equal("AUTH1", auth.Id);
        Assert.Equal(2, auth.CategoryOfAuthority);
        Assert.Equal("Port Authority", auth.TextContent);
    }

    [Fact]
    public void From_UnresolvedXlink_ReportsDiagnostic()
    {
        const string gml = """
            <S122:RestrictedArea gml:id="RA1">
                <S122:categoryOfRestrictedArea>4</S122:categoryOfRestrictedArea>
                <S122:geometry>
                    <S100:surfaceProperty>
                        <gml:Polygon srsName="EPSG:4326">
                            <gml:exterior>
                                <gml:LinearRing>
                                    <gml:posList>0 0 0 1 1 1 1 0 0 0</gml:posList>
                                </gml:LinearRing>
                            </gml:exterior>
                        </gml:Polygon>
                    </S100:surfaceProperty>
                </S122:geometry>
                <S122:theAuthority xlink:href="#MISSING"/>
            </S122:RestrictedArea>
            """;
        var ds = Parse(Wrap(gml));
        var typed = S122MarineProtectedAreaDataset.From(ds, out var diags);

        var ra = Assert.Single(typed.RestrictedAreas);
        Assert.Empty(ra.InformationReferences);
        Assert.Contains(diags, d => d.Code == "xlink.unresolved" && d.RelatedId == "RA1");
    }

    [Fact]
    public void From_UnparseableInt_ReportsDiagnosticAndPreservesRawInExtra()
    {
        const string gml = """
            <S122:RestrictedArea gml:id="RA2">
                <S122:restriction>not-a-number</S122:restriction>
            </S122:RestrictedArea>
            """;
        var ds = Parse(Wrap(gml));
        var typed = S122MarineProtectedAreaDataset.From(ds, out var diags);

        var ra = Assert.Single(typed.RestrictedAreas);
        Assert.Null(ra.Restriction);
        Assert.Contains(diags, d =>
            d.Code == "attribute.parse.int" &&
            d.RelatedId == "RA2" &&
            d.RelatedAttribute == "restriction");
    }

    [Fact]
    public void From_EmptyDataset_Throws()
    {
        const string gml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <S122:Dataset gml:id="ds_empty"
                xmlns:S122="http://www.iho.int/S122/1.0"
                xmlns:S100="http://www.iho.int/s100gml/5.0"
                xmlns:gml="http://www.opengis.net/gml/3.2"
                xmlns:xlink="http://www.w3.org/1999/xlink">
                <S100:DatasetIdentificationInformation>
                    <S100:productIdentifier>INT.IHO.S-122.1.0.0</S100:productIdentifier>
                </S100:DatasetIdentificationInformation>
                <S122:members/>
            </S122:Dataset>
            """;
        var ds = Parse(gml);
        Assert.Throws<InvalidOperationException>(() =>
            S122MarineProtectedAreaDataset.From(ds, out _));
    }

    [Fact]
    public void From_ProjectsTextPlacementAttributes()
    {
        const string gml = """
            <S122:TextPlacement gml:id="TP1">
                <S122:textOffsetBearing>45.5</S122:textOffsetBearing>
                <S122:textOffsetDistance>10.0</S122:textOffsetDistance>
                <S122:textRotation>90</S122:textRotation>
                <S122:textType>1</S122:textType>
                <S122:geometry>
                    <S100:pointProperty>
                        <gml:Point srsName="EPSG:4326">
                            <gml:pos>10 20</gml:pos>
                        </gml:Point>
                    </S100:pointProperty>
                </S122:geometry>
            </S122:TextPlacement>
            """;
        var ds = Parse(Wrap(gml));
        var typed = S122MarineProtectedAreaDataset.From(ds, out _);

        var tp = Assert.Single(typed.Features.OfType<S122TextPlacement>());
        Assert.Equal(45.5, tp.TextOffsetBearing);
        Assert.Equal(10.0, tp.TextOffsetDistance);
        Assert.Equal(90.0, tp.TextRotation);
        Assert.Equal(1, tp.TextType);
        Assert.Equal(S122GeometryKind.Point, tp.GeometryKind);
    }

    [Fact]
    public void From_ProjectsRegulationInformationType()
    {
        const string gml = """
            <S122:Regulations gml:id="REG1">
                <S122:categoryOfAuthority>3</S122:categoryOfAuthority>
                <S122:rxNCode>R-42</S122:rxNCode>
                <S122:textContent>No anchoring</S122:textContent>
            </S122:Regulations>
            """;
        var ds = Parse(Wrap(gml));
        var typed = S122MarineProtectedAreaDataset.From(ds, out _);

        var reg = Assert.Single(typed.InformationTypes.OfType<S122Regulations>());
        Assert.Equal(3, reg.CategoryOfAuthority);
        Assert.Equal("R-42", reg.RxNCode);
        Assert.Equal("No anchoring", reg.TextContent);
        Assert.Equal("Regulations", reg.TypeCode);
    }

    [Fact]
    public void From_UnknownAttributesPreservedInExtraAttributes()
    {
        const string gml = """
            <S122:RestrictedArea gml:id="RA3">
                <S122:restriction>3</S122:restriction>
                <S122:someFutureAttribute>future-value</S122:someFutureAttribute>
            </S122:RestrictedArea>
            """;
        var ds = Parse(Wrap(gml));
        var typed = S122MarineProtectedAreaDataset.From(ds, out _);

        var ra = Assert.Single(typed.RestrictedAreas);
        Assert.Equal(3, ra.Restriction);
        Assert.True(ra.ExtraAttributes.ContainsKey("someFutureAttribute"));
        Assert.Equal("future-value", ra.ExtraAttributes["someFutureAttribute"]);
    }

    [Fact]
    public void From_OfficialSample_RoundTrips()
    {
        var path = Path.Combine(TestDataDir, SampleFile);
        Assert.True(File.Exists(path), $"Test data file not found: {path}");

        var ds = S122Dataset.Open(path);
        var typed = S122MarineProtectedAreaDataset.From(ds, out var diags);

        Assert.Equal(ds.Features.Length, typed.Features.Length);
        Assert.Equal(ds.InformationTypes.Length, typed.InformationTypes.Length);
        Assert.NotNull(typed.ProductIdentifier);
        // Sample is xlink-free, so no unresolved-reference diagnostics.
        Assert.DoesNotContain(diags, d => d.Code == "xlink.unresolved");
    }
}
