using EncDotNet.S100.Datasets.S57;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Round-trip tests for <see cref="S101DocumentWriter"/>: a document written to
/// ISO 8211 bytes and read back with <see cref="S101DocumentReader"/> must be
/// equivalent to the original.
/// </summary>
public class S101DocumentWriterTests
{
    [Fact]
    public void WriteThenRead_PreservesAllRecordKinds()
    {
        var original = BuildSampleDocument();

        var bytes = S101DocumentWriter.Write(original);
        using var stream = new MemoryStream(bytes);
        var roundTripped = S101DocumentReader.ReadFromStream(stream);

        // Identification
        Assert.Equal(original.Identification.RecordName, roundTripped.Identification.RecordName);
        Assert.Equal(original.Identification.RecordId, roundTripped.Identification.RecordId);
        Assert.Equal(original.Identification.ProductSpecification, roundTripped.Identification.ProductSpecification);
        Assert.Equal(original.Identification.DatasetName, roundTripped.Identification.DatasetName);
        Assert.Equal(original.Identification.DatasetTitle, roundTripped.Identification.DatasetTitle);
        Assert.Equal(original.Identification.DatasetReferenceDate, roundTripped.Identification.DatasetReferenceDate);
        Assert.Equal(original.Identification.ApplicationProfile, roundTripped.Identification.ApplicationProfile);

        // Structure info (coordinate scale factors)
        Assert.Equal(original.StructureInfo.CoordinateMultiplicationFactorX, roundTripped.StructureInfo.CoordinateMultiplicationFactorX);
        Assert.Equal(original.StructureInfo.CoordinateMultiplicationFactorY, roundTripped.StructureInfo.CoordinateMultiplicationFactorY);
        Assert.Equal(original.StructureInfo.CoordinateMultiplicationFactorZ, roundTripped.StructureInfo.CoordinateMultiplicationFactorZ);

        // Catalogues
        Assert.Equal(original.FeatureTypeCatalogue, roundTripped.FeatureTypeCatalogue);
        Assert.Equal(original.AttributeTypeCatalogue, roundTripped.AttributeTypeCatalogue);
        Assert.Equal(original.InformationTypeCatalogue, roundTripped.InformationTypeCatalogue);

        // Record counts
        Assert.Equal(original.Points.Count, roundTripped.Points.Count);
        Assert.Equal(original.MultiPoints.Count, roundTripped.MultiPoints.Count);
        Assert.Equal(original.CurveSegments.Count, roundTripped.CurveSegments.Count);
        Assert.Equal(original.CompositeCurves.Count, roundTripped.CompositeCurves.Count);
        Assert.Equal(original.Surfaces.Count, roundTripped.Surfaces.Count);
        Assert.Equal(original.Features.Count, roundTripped.Features.Count);
        Assert.Equal(original.InformationTypes.Count, roundTripped.InformationTypes.Count);
    }

    [Fact]
    public void WriteThenRead_PreservesPointCoordinates()
    {
        var original = BuildSampleDocument();
        var bytes = S101DocumentWriter.Write(original);
        using var stream = new MemoryStream(bytes);
        var rt = S101DocumentReader.ReadFromStream(stream);

        var op = original.Points[1];
        var rp = rt.Points[1];
        Assert.Equal(op.X, rp.X);
        Assert.Equal(op.Y, rp.Y);
        Assert.Equal(op.RecordVersion, rp.RecordVersion);
    }

    [Fact]
    public void WriteThenRead_PreservesMultiPointSoundings()
    {
        var original = BuildSampleDocument();
        var bytes = S101DocumentWriter.Write(original);
        using var stream = new MemoryStream(bytes);
        var rt = S101DocumentReader.ReadFromStream(stream);

        Assert.Equal(original.MultiPoints[10].Points, rt.MultiPoints[10].Points);
    }

    [Fact]
    public void WriteThenRead_PreservesCurveGeometryAndTopology()
    {
        var original = BuildSampleDocument();
        var bytes = S101DocumentWriter.Write(original);
        using var stream = new MemoryStream(bytes);
        var rt = S101DocumentReader.ReadFromStream(stream);

        var oc = original.CurveSegments[20];
        var rc = rt.CurveSegments[20];
        Assert.Equal(oc.IntermediateCoordinates, rc.IntermediateCoordinates);
        Assert.Equal(oc.PointAssociations, rc.PointAssociations);
    }

    [Fact]
    public void WriteThenRead_PreservesSurfaceRings()
    {
        var original = BuildSampleDocument();
        var bytes = S101DocumentWriter.Write(original);
        using var stream = new MemoryStream(bytes);
        var rt = S101DocumentReader.ReadFromStream(stream);

        Assert.Equal(original.Surfaces[40].RingAssociations, rt.Surfaces[40].RingAssociations);
    }

    [Fact]
    public void WriteThenRead_PreservesFeatureAttributesAndSpatialAssociations()
    {
        var original = BuildSampleDocument();
        var bytes = S101DocumentWriter.Write(original);
        using var stream = new MemoryStream(bytes);
        var rt = S101DocumentReader.ReadFromStream(stream);

        var of = original.Features[0];
        var rf = rt.Features.Single(f => f.RecordId == of.RecordId);
        Assert.Equal(of.FeatureTypeCode, rf.FeatureTypeCode);
        Assert.Equal(of.ProducingAgency, rf.ProducingAgency);
        Assert.Equal(of.FeatureIdentificationNumber, rf.FeatureIdentificationNumber);
        Assert.Equal(of.FeatureIdentificationSubdivision, rf.FeatureIdentificationSubdivision);

        Assert.Equal(of.Attributes.Count, rf.Attributes.Count);
        for (int i = 0; i < of.Attributes.Count; i++)
        {
            Assert.Equal(of.Attributes[i].NumericCode, rf.Attributes[i].NumericCode);
            Assert.Equal(of.Attributes[i].Value, rf.Attributes[i].Value);
        }

        Assert.Equal(of.SpatialAssociations.Count, rf.SpatialAssociations.Count);
        for (int i = 0; i < of.SpatialAssociations.Count; i++)
        {
            Assert.Equal(of.SpatialAssociations[i].RecordName, rf.SpatialAssociations[i].RecordName);
            Assert.Equal(of.SpatialAssociations[i].RecordId, rf.SpatialAssociations[i].RecordId);
            Assert.Equal(of.SpatialAssociations[i].Orientation, rf.SpatialAssociations[i].Orientation);
        }
    }

    [Fact]
    public void WriteToFile_WithEmptyPath_ThrowsArgumentException()
    {
        var document = BuildSampleDocument();

        Assert.Throws<ArgumentException>(() => S101DocumentWriter.WriteToFile("", document));
    }

    [Fact]
    public async Task WriteToFileAsync_WithEmptyPath_ThrowsArgumentException()
    {
        var document = BuildSampleDocument();

        await Assert.ThrowsAsync<ArgumentException>(() => S101DocumentWriter.WriteToFileAsync("", document));
    }

    [SkippableFact]
    public void ConvertRealS57Fixture_Translate_Write_Read_RoundTrips()
    {
        var fixture = LocateFixture(Path.Combine("S57", "US5MA1BO", "US5MA1BO.000"));
        Skip.If(fixture is null, "S-57 fixture US5MA1BO.000 not found.");

        var dataset = S57Dataset.Open(fixture!);
        var translator = new S57ToS101Translator(S57S101Mapping.Default, allowedEnumValues: null);
        var docA = translator.Translate(dataset);

        var bytes = S101DocumentWriter.Write(docA);
        Assert.NotEmpty(bytes);

        using var stream = new MemoryStream(bytes);
        var docB = S101DocumentReader.ReadFromStream(stream);

        Assert.Equal(docA.Points.Count, docB.Points.Count);
        Assert.Equal(docA.MultiPoints.Count, docB.MultiPoints.Count);
        Assert.Equal(docA.CurveSegments.Count, docB.CurveSegments.Count);
        Assert.Equal(docA.CompositeCurves.Count, docB.CompositeCurves.Count);
        Assert.Equal(docA.Surfaces.Count, docB.Surfaces.Count);
        Assert.Equal(docA.Features.Count, docB.Features.Count);
        Assert.Equal(docA.FeatureTypeCatalogue, docB.FeatureTypeCatalogue);
        Assert.Equal(docA.AttributeTypeCatalogue, docB.AttributeTypeCatalogue);

        // Spot-check that per-feature attribute values survive the round-trip.
        var fa = docA.Features.First(f => f.Attributes.Count > 0);
        var fb = docB.Features.Single(f => f.RecordId == fa.RecordId);
        Assert.Equal(
            fa.Attributes.Select(a => (a.NumericCode, a.Value)),
            fb.Attributes.Select(a => (a.NumericCode, a.Value)));
    }

    private static S101Document BuildSampleDocument()
    {
        return new S101Document
        {
            Identification = new S101DatasetIdentification
            {
                RecordName = 10,
                RecordId = 1,
                EncodingSpecification = "S-100 Part 10a",
                EncodingSpecificationEdition = "5.2.0",
                ProductSpecification = "INT.IHO.S-101.1.0",
                ProductSpecificationEdition = "1.0.0",
                ApplicationProfile = "1",
                DatasetName = "US5MA1BO.000",
                DatasetTitle = "Sample Cell",
                DatasetReferenceDate = "20240101",
                DatasetLanguage = "eng",
                DatasetAbstract = "abstract",
                DatasetEdition = "1",
            },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 10_000_000,
                CoordinateMultiplicationFactorY = 10_000_000,
                CoordinateMultiplicationFactorZ = 10,
            },
            FeatureTypeCatalogue = new Dictionary<ushort, string> { [42] = "DepthArea", [7] = "Sounding" },
            AttributeTypeCatalogue = new Dictionary<ushort, string> { [100] = "valdco", [200] = "objnam" },
            InformationTypeCatalogue = new Dictionary<ushort, string> { [5] = "SpatialQuality" },
            InformationAssociationCatalogue = new Dictionary<ushort, string>(),
            FeatureAssociationCatalogue = new Dictionary<ushort, string>(),
            RoleCatalogue = new Dictionary<ushort, string>(),
            Points = new Dictionary<uint, S101PointRecord>
            {
                [1] = new S101PointRecord { RecordId = 1, X = -710_000_000, Y = 420_000_000, RecordVersion = 1, UpdateInstruction = S101UpdateInstruction.Insert },
            },
            MultiPoints = new Dictionary<uint, S101MultiPointRecord>
            {
                [10] = new S101MultiPointRecord
                {
                    RecordId = 10,
                    Points = [(420_000_100, -710_000_100, 53), (420_000_200, -710_000_200, 128)],
                    RecordVersion = 1,
                    UpdateInstruction = S101UpdateInstruction.Insert,
                },
            },
            CurveSegments = new Dictionary<uint, S101CurveSegmentRecord>
            {
                [20] = new S101CurveSegmentRecord
                {
                    RecordId = 20,
                    PointAssociations =
                    [
                        new S101PointAssociation(110, 1, 1),
                        new S101PointAssociation(110, 2, 2),
                    ],
                    IntermediateCoordinates = [(420_000_050, -710_000_050), (420_000_060, -710_000_070)],
                    RecordVersion = 1,
                    UpdateInstruction = S101UpdateInstruction.Insert,
                },
            },
            CompositeCurves = new Dictionary<uint, S101CompositeCurveRecord>
            {
                [30] = new S101CompositeCurveRecord
                {
                    RecordId = 30,
                    CurveComponents = [new S101CurveUsage(120, 20, 1)],
                    RecordVersion = 1,
                    UpdateInstruction = S101UpdateInstruction.Insert,
                },
            },
            Surfaces = new Dictionary<uint, S101SurfaceRecord>
            {
                [40] = new S101SurfaceRecord
                {
                    RecordId = 40,
                    RingAssociations =
                    [
                        new S101RingAssociation(125, 30, 1, 1),
                        new S101RingAssociation(125, 31, 1, 2),
                    ],
                    RecordVersion = 1,
                    UpdateInstruction = S101UpdateInstruction.Insert,
                },
            },
            Features =
            [
                new S101FeatureRecord
                {
                    RecordId = 50,
                    FeatureTypeCode = 42,
                    ProducingAgency = 550,
                    FeatureIdentificationNumber = 123_456,
                    FeatureIdentificationSubdivision = 1,
                    Attributes =
                    [
                        new S101Attribute(200, 1, "Boston Harbor"),
                        new S101Attribute(100, 1, "10.5"),
                    ],
                    SpatialAssociations = [new S101SpatialAssociation(130, 40, 1)],
                    RecordVersion = 1,
                    UpdateInstruction = S101UpdateInstruction.Insert,
                },
            ],
            InformationTypes = new Dictionary<uint, S101InformationRecord>
            {
                [60] = new S101InformationRecord
                {
                    RecordId = 60,
                    InformationTypeCode = 5,
                    Attributes = [new S101Attribute(200, 1, "quality note")],
                    RecordVersion = 1,
                    UpdateInstruction = S101UpdateInstruction.Insert,
                },
            },
        };
    }

    private static string? LocateFixture(string relativeUnderDatasets)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", relativeUnderDatasets);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
