using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.S412.Tests;

public class S412DatasetReaderTests
{
    internal const string SampleFile = "TestData/4122C00_TESTDS1.GML";
    internal const string SyntheticFile = "TestData/synthetic_all_warnings.gml";

    [Fact]
    public void Sample_ReadsIdentificationAndMembers()
    {
        var dataset = S412Dataset.Open(SampleFile);

        Assert.Equal("S-412", dataset.ProductIdentifier);
        Assert.Equal("1.0.0", dataset.DeclaredEdition);
        Assert.Equal("DS1", dataset.DatasetIdentifier);
        var feature = Assert.Single(dataset.Features);
        Assert.Equal("WindWarning", feature.FeatureType);
        var info = Assert.Single(dataset.InformationTypes);
        Assert.Equal("DataProvider", info.TypeCode);
        Assert.Equal("ID0", info.Id);
    }

    [Fact]
    public void Sample_EnumerationValuesAreTheirCodes()
    {
        var feature = S412Dataset.Open(SampleFile).Features[0];

        // <windWarningThreshold code="3">storm force</windWarningThreshold>
        Assert.Equal("3", feature.Attributes["windWarningThreshold"]);
        Assert.Equal("8", feature.Attributes["beaufortForce"]);
        Assert.Equal("stuff", feature.Attributes["weatherWarningNumber"]);
    }

    [Fact]
    public void Sample_ReadsSurfaceLatitudeFirst()
    {
        var feature = S412Dataset.Open(SampleFile).Features[0];

        Assert.Equal(S100GeometryType.Surface, feature.GeometryType);
        Assert.Equal(9, feature.ExteriorRing.Count);
        Assert.Equal(new GeoPosition(59.98823074677464, -32.06206321307574), feature.ExteriorRing[0]);
    }

    [Fact]
    public void Sample_FixedDateRange_UnwrapsTruncatedDate()
    {
        var feature = S412Dataset.Open(SampleFile).Features[0];

        var range = Assert.Single(feature.ComplexAttributes, c => c.Code == "fixedDateRange");
        Assert.Equal("2025-01-01", range.SubAttributes["dateStart"]);
        Assert.Equal("20250101", range.SubAttributes["dateEnd"]);
        Assert.Equal("0600", range.SubAttributes["timeOfDayStart"]);
        Assert.Equal("1800", range.SubAttributes["timeOfDayEnd"]);
    }

    [Fact]
    public void Sample_DataProviderReference()
    {
        var feature = S412Dataset.Open(SampleFile).Features[0];

        var reference = Assert.Single(feature.References);
        Assert.Equal("dataProviderInformation", reference.Role);
        Assert.Equal("#ID0", reference.Href);
    }

    [Fact]
    public void Sample_DataProvider_NestedComplexAttributes()
    {
        var info = S412Dataset.Open(SampleFile).InformationTypes[0];

        var issuing = Assert.Single(info.ComplexAttributes, c => c.Code == "issuingService");
        Assert.Equal("11", issuing.SubAttributes["metareaName"]);
        Assert.Equal("UK", issuing.SubAttributes["countryName"]);
        // The first of the repeated <information> wins the dotted key.
        Assert.Equal("UK Met Office", issuing.SubAttributes["issuingServiceName.information.text"]);

        var name = Assert.Single(issuing.Groups);
        Assert.Equal("issuingServiceName", name.Code);
        Assert.Collection(
            name.Groups,
            g => Assert.Equal("eng", g.SubAttributes["language"]),
            g => Assert.Equal("Le bureau meteo", g.SubAttributes["text"]));
    }

    [Fact]
    public void Synthetic_ReadsEveryWarningType()
    {
        var dataset = S412Dataset.Open(SyntheticFile);

        Assert.Equal(
            ["WindWarning", "SignificantWaveHeightWarning", "RestrictedVisibilityWarning", "ThunderstormWarning", "IceAccretionWarning"],
            dataset.Features.Select(f => f.FeatureType));
        Assert.All(dataset.Features, f => Assert.Equal(S100GeometryType.Surface, f.GeometryType));
        Assert.Equal(AxisOrderOutcome.AsEncoded, dataset.AxisOrder?.Outcome);
    }

    [Fact]
    public void ReadMetadata_ReportsSpecAndExtent()
    {
        var metadata = S412Dataset.ReadMetadata(SyntheticFile);

        Assert.Equal("S-412", metadata.Spec.Name);
        Assert.NotNull(metadata.Extent);
        Assert.Equal(-38.0, metadata.Extent!.WestLongitude, 6);
        Assert.Equal(62.0, metadata.Extent.NorthLatitude, 6);
    }
}
