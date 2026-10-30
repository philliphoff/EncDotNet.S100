using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The S-101 pick report built from the feature record (<see cref="S101PickReport"/>):
/// attributes with their parent index (#1156) and the information and feature
/// associations S-98 15.1 asks the report to follow (#1157).
/// </summary>
public sealed class S101PickReportTests
{
    private const ushort Buoy = 1, Light = 2;
    private const ushort Colour = 1, FeatureName = 2, Name = 3, Text = 4, FileReference = 5, Information = 6;
    private const ushort NauticalInformation = 1;
    private const ushort AdditionalInformation = 1, StructureEquipment = 1;
    private const ushort TheInformation = 1, TheEquipment = 2;

    private static readonly FeatureCatalogueDecoder Decoder = LoadDecoder();

    private static FeatureCatalogueDecoder LoadDecoder()
    {
        using var stream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new InvalidOperationException("S-101 feature catalogue not bundled.");
        return new FeatureCatalogueDecoder(FeatureCatalogueReader.Read(stream));
    }

    private static S101FeatureRecord Feature(
        uint id,
        ushort type,
        S101Attribute[]? attributes = null,
        S101FeatureAssociation[]? features = null,
        S101InformationAssociation[]? information = null) => new()
        {
            RecordId = id,
            FeatureTypeCode = type,
            Attributes = attributes ?? [],
            SpatialAssociations = [],
            FeatureAssociations = features ?? [],
            InformationAssociations = information ?? [],
        };

    private static S101Document Document(IReadOnlyList<S101FeatureRecord> features, params S101InformationRecord[] information) => new()
    {
        Identification = new S101DatasetIdentification { DatasetName = "PICK.000" },
        StructureInfo = new S101DatasetStructureInfo
        {
            CoordinateMultiplicationFactorX = 10_000_000,
            CoordinateMultiplicationFactorY = 10_000_000,
            CoordinateMultiplicationFactorZ = 10,
        },
        FeatureTypeCatalogue = new Dictionary<ushort, string> { [Buoy] = "CardinalBuoy", [Light] = "LightAllAround" },
        AttributeTypeCatalogue = new Dictionary<ushort, string>
        {
            [Colour] = "colour",
            [FeatureName] = "featureName",
            [Name] = "name",
            [Text] = "text",
            [FileReference] = "fileReference",
            [Information] = "information",
        },
        InformationTypeCatalogue = new Dictionary<ushort, string> { [NauticalInformation] = "NauticalInformation" },
        InformationAssociationCatalogue = new Dictionary<ushort, string> { [AdditionalInformation] = "AdditionalInformation" },
        FeatureAssociationCatalogue = new Dictionary<ushort, string> { [StructureEquipment] = "StructureEquipment" },
        RoleCatalogue = new Dictionary<ushort, string> { [TheInformation] = "theInformation", [TheEquipment] = "theEquipment" },
        Points = new Dictionary<uint, S101PointRecord>(),
        CurveSegments = new Dictionary<uint, S101CurveSegmentRecord>(),
        CompositeCurves = new Dictionary<uint, S101CompositeCurveRecord>(),
        Surfaces = new Dictionary<uint, S101SurfaceRecord>(),
        Features = features,
        InformationTypes = information.ToDictionary(i => i.RecordId),
    };

    private static S101InformationRecord Note(uint id, params S101Attribute[] attributes) => new()
    {
        RecordId = id,
        InformationTypeCode = NauticalInformation,
        Attributes = attributes,
    };

    [Fact]
    public void Attributes_KeepRepeatedValuesAndNestComplexOnes()
    {
        var buoy = Feature(1, Buoy,
        [
            new S101Attribute(Colour, 1, "6"),
            new S101Attribute(Colour, 2, "2"),
            new S101Attribute(FeatureName, 1, ""),
            new S101Attribute(Name, 1, "S Skerries", ParentIndex: 3),
        ]);

        var rows = S101PickReport.Attributes(Document([buoy]), buoy.Attributes, Decoder);

        Assert.Equal(["colour", "colour", "featureName"], rows.Select(r => r.Code));
        Assert.Equal(["Yellow", "Black"], rows.Take(2).Select(r => r.DisplayValue));
        Assert.Equal("S Skerries", Assert.Single(rows[2].Children).RawValue);
    }

    [Fact]
    public void InformationRows_HoldEachLinkedRecordsAttributesOnce()
    {
        var area = Feature(1, Buoy, information:
        [
            new(AdditionalInformation, 10, TheInformation),
            new(AdditionalInformation, 11, TheInformation),
            new(AdditionalInformation, 10, TheInformation),
            new(AdditionalInformation, 99, TheInformation),
        ]);
        var document = Document([area],
            Note(10, new S101Attribute(Information, 1, ""), new S101Attribute(Text, 1, "Shoaling reported 1990", ParentIndex: 1)),
            Note(11, new S101Attribute(Information, 1, ""), new S101Attribute(FileReference, 1, "NOTE.TXT", ParentIndex: 1)));

        var rows = S101PickReport.InformationRows(document, area, Decoder);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("NauticalInformation", r.Code));
        Assert.Equal("Nautical Information", rows[0].Name);
        Assert.Equal("Shoaling reported 1990", rows[0].Children[0].Children[0].RawValue);
        Assert.Equal(["NOTE.TXT"], S101PickReport.InformationFileReferences(document, area));
    }

    [Fact]
    public void FeatureReferences_NameTheAssociationRoleAndTarget()
    {
        var light = Feature(2, Light,
        [
            new S101Attribute(FeatureName, 1, ""),
            new S101Attribute(Name, 1, "Bell Rock", ParentIndex: 1),
        ]);
        var buoy = Feature(1, Buoy, features: [new(StructureEquipment, 2, TheEquipment), new(StructureEquipment, 99, TheEquipment)]);
        var document = Document([buoy, light]);

        var references = S101PickReport.FeatureReferences(
            document, buoy, id => document.Features.FirstOrDefault(f => f.RecordId == id), Decoder);

        Assert.Equal(2, references.Count);
        Assert.Equal("StructureEquipment", references[0].Role);
        Assert.Equal("theEquipment", references[0].ArcRole);
        Assert.Equal("2", references[0].TargetRef);
        Assert.Equal("Light All Around: Bell Rock", references[0].TargetDisplayName);
        Assert.Null(references[1].TargetName);
        Assert.Equal("99", references[1].TargetDisplayName);
    }
}
