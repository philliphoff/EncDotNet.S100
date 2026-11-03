using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S57.Tests.RoundTrip;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57.Tests;

/// <summary>
/// Tests that <see cref="S101ToS57Translator"/> output is a base cell
/// <see cref="S57DocumentWriter"/> accepts, and that it reads back through
/// <see cref="S57DocumentReader"/> unchanged.
/// </summary>
public class S101ToS57WriterTests
{
    private const int AttlObjnam = 116;
    private const int AttlNobjnm = 301;
    private const uint LargeFidn = 3_000_000_000;

    // A small cell: two named landmarks (one Latin-1, one Greek), a depth
    // area, soundings, coverage and the sounding datum.
    private static S101Document Cell(uint cmfy = 10_000_000, bool names = true)
    {
        var featureTypes = new Dictionary<ushort, string>
        {
            [1] = "Landmark",
            [2] = "DepthArea",
            [3] = "Sounding",
            [4] = "DataCoverage",
            [5] = "SoundingDatum",
        };
        var attributes = new Dictionary<ushort, string>
        {
            [1] = "featureName",
            [2] = "name",
            [3] = "nameUsage",
            [4] = "depthRangeMinimumValue",
            [5] = "depthRangeMaximumValue",
            [6] = "maximumDisplayScale",
            [7] = "verticalDatum",
        };
        var points = new Dictionary<uint, S101PointRecord>
        {
            [1] = new() { RecordId = 1, Y = 403_000_000, X = -740_000_000 },
            [2] = new() { RecordId = 2, Y = 403_000_000, X = -739_000_000 },
            [3] = new() { RecordId = 3, Y = 404_000_000, X = -739_000_000 },
            [4] = new() { RecordId = 4, Y = 403_500_000, X = -739_500_000 },
            [5] = new() { RecordId = 5, Y = 403_600_000, X = -739_600_000 },
        };
        var curves = new Dictionary<uint, S101CurveSegmentRecord>
        {
            [1] = Curve(1, 1, 2),
            [2] = Curve(2, 2, 3),
            [3] = Curve(3, 3, 1, (403_400_000, -739_800_000)),
        };
        var surfaces = new Dictionary<uint, S101SurfaceRecord>
        {
            [1] = new() { RecordId = 1, RingAssociations = [new(120, 1, 1, 1), new(120, 2, 1, 1), new(120, 3, 1, 1)] },
        };
        var multiPoints = new Dictionary<uint, S101MultiPointRecord>
        {
            [1] = new() { RecordId = 1, Points = [(403_100_000, -739_900_000, 55), (403_200_000, -739_800_000, 123)] },
        };

        return new S101Document
        {
            Identification = new S101DatasetIdentification
            {
                DatasetName = "US5TEST1.000",
                DatasetReferenceDate = "2025-05-01",
                DatasetEdition = "3",
            },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 10_000_000,
                CoordinateMultiplicationFactorY = cmfy,
                CoordinateMultiplicationFactorZ = 10,
            },
            FeatureTypeCatalogue = featureTypes,
            AttributeTypeCatalogue = attributes,
            Points = points,
            MultiPoints = multiPoints,
            CurveSegments = curves,
            CompositeCurves = new Dictionary<uint, S101CompositeCurveRecord>(),
            Surfaces = surfaces,
            Features = new List<S101FeatureRecord>
            {
                Feature(1, 1, 1, (110, 4), (1, "", 0), (2, "Cayo Francés", 1), (3, "1", 1)),
                Feature(2, 1, LargeFidn, (110, 5), (1, "", 0), (2, "Λιμάνι", 1), (3, "1", 1)),
                Feature(3, 2, 3, (130, 1), (4, "5", 0), (5, "10", 0)),
                Feature(4, 3, 4, (115, 1)),
                Feature(5, 4, 5, (130, 1), (6, "22000", 0)),
                Feature(6, 5, 6, (130, 1), (7, "12", 0)),
            }.Where(f => names || f.FeatureTypeCode != 1).ToList(),
            InformationTypes = new Dictionary<uint, S101InformationRecord>(),
            InformationTypeCatalogue = new Dictionary<ushort, string>(),
            InformationAssociationCatalogue = new Dictionary<ushort, string>(),
            FeatureAssociationCatalogue = new Dictionary<ushort, string>(),
            RoleCatalogue = new Dictionary<ushort, string>(),
        };

        static S101CurveSegmentRecord Curve(uint id, uint begin, uint end, params (int Y, int X)[] intermediates) => new()
        {
            RecordId = id,
            PointAssociations = [new(110, begin, 1), new(110, end, 2)],
            IntermediateCoordinates = intermediates,
        };

        static S101FeatureRecord Feature(uint id, ushort type, uint fidn, (byte Rcnm, uint Id) geometry, params (ushort Code, string Value, ushort Parent)[] rows) => new()
        {
            RecordId = id,
            FeatureTypeCode = type,
            ProducingAgency = 550,
            FeatureIdentificationNumber = fidn,
            FeatureIdentificationSubdivision = 1,
            Attributes = rows.Select(r => new S101Attribute(r.Code, 1, r.Value, r.Parent)).ToList(),
            SpatialAssociations = [new S101SpatialAssociation(geometry.Rcnm, geometry.Id, 1)],
            InformationAssociations = [],
        };
    }

    [Fact]
    public void TranslatedCell_WritesAndReadsBackUnchanged()
    {
        var translated = new S101ToS57Translator().Translate(Cell());

        var bytes = S57DocumentWriter.Write(translated);
        var readBack = S57DocumentReader.Read(bytes);

        Assert.Empty(S57ModelDiff.Compare(translated, readBack));
        Assert.Equal(translated.FeatureRecords.Count, readBack.FeatureRecords.Count);
        Assert.Equal(translated.VectorRecords.Count, readBack.VectorRecords.Count);
    }

    [Fact]
    public void TranslatedCell_HasBaseCellIdentification()
    {
        var translated = new S101ToS57Translator().Translate(Cell());

        var readBack = S57DocumentReader.Read(S57DocumentWriter.Write(translated));

        var dsid = readBack.DataSetIdentification!;
        Assert.Equal((10, 1), (dsid.RecordName.RecordNameCode, dsid.RecordName.RecordId));
        Assert.Equal(1, dsid.ExchangePurpose);
        Assert.Equal(5, dsid.IntendedUsage);
        Assert.Equal("US5TEST1.000", dsid.DataSetName);
        Assert.Equal("20250501", dsid.IssueDate);
        Assert.Equal("03.1", dsid.S57EditionNumber);
        Assert.Equal((1, 1), (dsid.ProductSpecification, dsid.ApplicationProfile));
        Assert.Equal(550, dsid.ProducingAgency);
        Assert.Equal(2, dsid.DataStructure);

        var dspm = readBack.DataSetParameters!;
        Assert.Equal((20, 1), (dspm.RecordName.RecordNameCode, dspm.RecordName.RecordId));
        Assert.Equal(2, dspm.HorizontalDatum);
        Assert.Equal(12, dspm.SoundingDatum);
        Assert.Equal(22000, dspm.CompilationScale);
        Assert.Equal((10_000_000, 10), (dspm.CoordinateMultiplicationFactor, dspm.SoundingMultiplicationFactor));
        Assert.All(readBack.FeatureRecords, f => Assert.Equal((1, S57UpdateInstruction.Insert), (f.RecordVersion, f.UpdateInstruction)));
        Assert.All(readBack.VectorRecords, v => Assert.Equal((1, S57UpdateInstruction.Insert), (v.RecordVersion, v.UpdateInstruction)));
    }

    [Fact]
    public void Text_OutsideLatin1_MovesToNatfAtLevel2()
    {
        var diagnostics = new S101ToS57TranslationDiagnostics();
        var translated = new S101ToS57Translator().Translate(Cell(), diagnostics);

        var readBack = S57DocumentReader.Read(S57DocumentWriter.Write(translated));

        Assert.Equal((1, 2), (readBack.DataSetIdentification!.AttfLexicalLevel, readBack.DataSetIdentification.NatfLexicalLevel));
        var latin = Assert.Single(readBack.FeatureRecords, f => f.RecordName.FeatureId == 1);
        Assert.Equal("Cayo Francés", Assert.Single(latin.Attributes, a => a.AttributeCode == AttlObjnam).Value);
        var greek = Assert.Single(readBack.FeatureRecords, f => f.RecordName.FeatureId == unchecked((int)LargeFidn));
        Assert.DoesNotContain(greek.Attributes, a => a.AttributeCode == AttlObjnam);
        Assert.Equal("Λιμάνι", Assert.Single(greek.NationalAttributes, a => a.AttributeCode == AttlNobjnm).Value);
        Assert.Equal(1, diagnostics.MovedToNational["OBJNAM"]);
    }

    [Fact]
    public void Text_AllAscii_UsesLevel0()
    {
        var translated = new S101ToS57Translator().Translate(Cell(names: false));

        Assert.Equal((0, 0), (translated.DataSetIdentification!.AttfLexicalLevel, translated.DataSetIdentification.NatfLexicalLevel));
        Assert.NotEmpty(S57DocumentWriter.Write(translated));
    }

    [Fact]
    public void LargeFidn_RoundTripsThroughFoid()
    {
        var translated = new S101ToS57Translator().Translate(Cell());

        var readBack = S57DocumentReader.Read(S57DocumentWriter.Write(translated));

        var feature = Assert.Single(readBack.FeatureRecords, f => f.RecordName.FeatureId == unchecked((int)LargeFidn));
        Assert.Equal(LargeFidn, feature.LongName!.Value.FeatureId);
    }

    [Fact]
    public void Write_IsByteStable()
    {
        var translator = new S101ToS57Translator();

        Assert.Equal(S57DocumentWriter.Write(translator.Translate(Cell())), S57DocumentWriter.Write(translator.Translate(Cell())));
    }

    [Fact]
    public void Coordinates_DifferentCmfy_AreRescaledToComf()
    {
        // CMFY 1e8 with COMF 1e7: every Y is a tenth in S-57.
        var translated = new S101ToS57Translator().Translate(Cell(cmfy: 100_000_000));

        var node = Assert.Single(translated.VectorRecords, v => v.RecordName is { RecordNameCode: 110, RecordId: 4 });
        Assert.Equal((-739_500_000, 40_350_000), (node.Coordinates2D[0].X, node.Coordinates2D[0].Y));
        Assert.Equal(10_000_000, translated.DataSetParameters!.CoordinateMultiplicationFactor);
    }
}
