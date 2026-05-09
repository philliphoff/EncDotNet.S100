using System.Collections.Immutable;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S125.Tests;

public class S125FeatureXmlSourceTests
{
    private static S125Dataset BuildDataset() => new()
    {
        ProductIdentifier = "S-125",
        DatasetIdentifier = "DS_TEST",
        Features =
        [
            new S125Feature
            {
                Id = "f1",
                FeatureType = "LateralBuoy",
                GeometryType = GmlGeometryType.Point,
                Points = ImmutableArray.Create((36.95, -76.0)),
                Curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
                ExteriorRing = ImmutableArray<(double, double)>.Empty,
                InteriorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
                Attributes = ImmutableDictionary.CreateRange(new Dictionary<string, string>
                {
                    ["categoryOfLateralMark"] = "1",
                }),
                ComplexAttributes = ImmutableArray<S125ComplexAttribute>.Empty,
                InformationReferences = ImmutableArray.Create(new S125InformationReference
                {
                    Role = "AtoNStatus",
                    InformationRef = "info1",
                }),
            },
        ],
        InformationTypes =
        [
            new S125InformationType
            {
                Id = "info1",
                TypeCode = "AtonStatusInformation",
                Attributes = ImmutableDictionary.CreateRange(new Dictionary<string, string>
                {
                    ["changeTypes"] = "1",
                }),
                ComplexAttributes = ImmutableArray<S125ComplexAttribute>.Empty,
            },
        ],
    };

    [Fact]
    public void GetFeatureXml_EmitsDatasetWithFeaturesAndInformationTypes()
    {
        var source = new S125FeatureXmlSource(BuildDataset());

        var doc = XDocument.Load(source.GetFeatureXml());
        var root = doc.Root!;

        Assert.Equal("Dataset", root.Name.LocalName);
        Assert.NotNull(root.Element("Points"));
        Assert.NotNull(root.Element("Features"));
        Assert.NotNull(root.Element("InformationTypes"));

        var feature = root.Element("Features")!.Element("LateralBuoy")!;
        Assert.Equal("f1", feature.Attribute("id")!.Value);
        Assert.Equal("Point", feature.Attribute("primitive")!.Value);

        // Information reference must round-trip with informationRef attr.
        var atonStatus = feature.Element("AtoNStatus")!;
        Assert.Equal("info1", atonStatus.Attribute("informationRef")!.Value);

        // Simple attributes flowed through.
        Assert.Equal("1", feature.Element("categoryOfLateralMark")!.Value);

        // InformationTypes group carries the AtonStatusInformation instance.
        var info = root.Element("InformationTypes")!.Element("AtonStatusInformation")!;
        Assert.Equal("info1", info.Attribute("id")!.Value);
        Assert.Equal("1", info.Element("changeTypes")!.Value);
    }

    [Fact]
    public void FeatureTypesPresent_ReturnsUniqueFeatureCodes()
    {
        var source = new S125FeatureXmlSource(BuildDataset());
        Assert.Contains("LateralBuoy", source.FeatureTypesPresent);
    }
}
