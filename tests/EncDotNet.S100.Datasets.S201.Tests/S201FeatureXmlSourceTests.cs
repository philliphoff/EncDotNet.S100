using System.Linq;
using System.Xml.Linq;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.S201.Tests;

public class S201FeatureXmlSourceTests
{
    private static S201Dataset BuildDataset() => new()
    {
        ProductIdentifier = "S-201",
        DatasetIdentifier = "DS_TEST",
        Features =
        [
            new S201Feature
            {
                Id = "f1",
                FeatureType = "LateralBuoy",
                GeometryType = S100GeometryType.Point,
                Points = [(36.95, -76.0)],
                Curves = [],
                ExteriorRing = [],
                InteriorRings = [],
                Attributes = new Dictionary<string, string>
                {
                    ["categoryOfLateralMark"] = "1",
                },
                ComplexAttributes = [],
                InformationReferences = [new S201InformationReference
                {
                    Role = "AtoNStatus",
                    InformationRef = "info1",
                }],
                FeatureReferences = [new S201FeatureReference
                {
                    Role = "theParentFeature",
                    TargetRef = "structure1",
                }],
            },
        ],
        InformationTypes =
        [
            new S201InformationType
            {
                Id = "info1",
                TypeCode = "AtonStatusInformation",
                Attributes = new Dictionary<string, string>
                {
                    ["changeTypes"] = "1",
                },
                ComplexAttributes = [],
            },
        ],
    };

    [Fact]
    public void GetFeatureXml_EmitsDatasetWithFeaturesAndInformationTypes()
    {
        var source = new S201FeatureXmlSource(BuildDataset());

        var doc = XDocument.Load(source.GetFeatureXml());
        var root = doc.Root!;

        Assert.Equal("Dataset", root.Name.LocalName);
        Assert.NotNull(root.Element("Features"));
        Assert.NotNull(root.Element("InformationTypes"));

        var feature = root.Element("Features")!.Element("LateralBuoy")!;
        Assert.Equal("f1", feature.Attribute("id")!.Value);
        Assert.Equal("Point", feature.Attribute("primitive")!.Value);

        var atonStatus = feature.Element("AtoNStatus")!;
        Assert.Equal("info1", atonStatus.Attribute("informationRef")!.Value);

        var parent = feature.Element("theParentFeature")!;
        Assert.Equal("structure1", parent.Attribute("featureRef")!.Value);

        Assert.Equal("1", feature.Element("categoryOfLateralMark")!.Value);

        var info = root.Element("InformationTypes")!.Element("AtonStatusInformation")!;
        Assert.Equal("info1", info.Attribute("id")!.Value);
        Assert.Equal("1", info.Element("changeTypes")!.Value);
    }

    [Fact]
    public void FeatureTypesPresent_ReturnsUniqueFeatureCodes()
    {
        var source = new S201FeatureXmlSource(BuildDataset());
        Assert.Contains("LateralBuoy", source.FeatureTypesPresent);
    }
}
