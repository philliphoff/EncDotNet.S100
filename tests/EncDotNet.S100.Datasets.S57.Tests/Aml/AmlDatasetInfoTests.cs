using EncDotNet.S100.TestSupport;
using EncDotNet.S57;
using EncDotNet.S57.Aml;

namespace EncDotNet.S100.Datasets.S57.Tests.Aml;

/// <summary>
/// AML cells are recognised from their <c>DSID</c> <c>PRSP</c> and described
/// from their header, their <c>m_clas</c> meta feature and their name (#1236).
/// The cells are synthetic, written with the upstream S-57 writer.
/// </summary>
public class AmlDatasetInfoTests
{
    private static S57Document RoundTrip(string name, SyntheticAmlCell.Options options)
    {
        var dir = Directory.CreateTempSubdirectory("aml-info-").FullName;
        try
        {
            return S57DocumentReader.ReadFromFile(SyntheticAmlCell.Write(dir, name, options));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(51, "UKM6UT00.000", AmlProduct.MFF, "MFF", "Maritime Foundation and Facilities")]
    [InlineData(52, "UKR6UT00.000", AmlProduct.RAL, "RAL", "Routes, Areas and Limits")]
    [InlineData(53, "UKL6UT00.000", AmlProduct.LBO, "LBO", "Large Bottom Objects")]
    [InlineData(54, "UKC6UT00.000", AmlProduct.CLB, "CLB", "Contour Line Bathymetry")]
    [InlineData(55, "UKE6UT00.000", AmlProduct.ESB, "ESB", "Environment, Seabed and Beach")]
    [InlineData(56, "UKS6UT00.000", AmlProduct.SBO, "SBO", "Small Bottom Objects")]
    public void Aml1Cell_IsRecognisedWithHeaderFacts(int prsp, string name, AmlProduct product, string code, string productName)
    {
        var document = RoundTrip(name, new SyntheticAmlCell.Options
        {
            ProductSpecification = prsp,
            Comment = SyntheticAmlCell.UkhoComment(),
        });

        Assert.True(AmlDatasetInfo.IsAml(document));
        var info = AmlDatasetInfo.FromDocument(document, name)!;

        Assert.Equal(product, info.Product);
        Assert.Equal(prsp, info.ProductSpecification);
        Assert.Equal(code, info.ProductCode);
        Assert.Equal(productName, info.ProductName);
        Assert.Equal("1.0", info.Version);
        Assert.False(info.IsAml30);
        Assert.Equal($"AML · {code}", info.TagText);
        Assert.Equal($"Additional Military Layers · {productName} · 1.0", info.TagToolTip);
        Assert.Equal("UNCLASSIFIED", info.ProtectiveMarking);
        Assert.Equal(AmlMarkingSource.Header, info.MarkingSource);
        Assert.Equal("NATO: North Atlantic Treaty Organisation", info.IdoStatus);
        Assert.Equal("UK", info.OwnerAuthority);
        Assert.Null(info.Caveat);
        Assert.Equal(6, info.ScaleBand);
        // The UKHO cells quote their first pair; the title still reads.
        Assert.Equal("North Atlantic Ocean - English Channel - 50°.622N - 1°.196W", info.Title);
        Assert.Equal(new AmlCellName { Producer = "UK", Product = code, ScaleBand = 6, ClassificationLetter = 'U', IsAml30Pattern = false }, info.FromName);
    }

    [Theory]
    [InlineData("AMLDEU7U_NKKK4824_CLB_SB7_TrialData.000", 107, "UNCLASSIFIED", "CLB", "AML 3.0 · CLB", 7)]
    [InlineData("AMLDEU0R_NKLK0540_LBO-TrialData.000", 100, "RESTRICTED", "LBO", "AML 3.0 · LBO", 0)]
    [InlineData("AMLDEU0U_NK_FMETEST.000", 100, "UNCLASSIFIED", null, "AML 3.0", 0)]
    public void Aml30Cell_WithoutComment_TakesMarkingFromCellName(
        string name, int intendedUsage, string marking, string? code, string tag, int band)
    {
        var document = RoundTrip(name, new SyntheticAmlCell.Options
        {
            ProductSpecification = 57,
            Edition = "3.0",
            IntendedUsage = intendedUsage,
        });

        var info = AmlDatasetInfo.FromDocument(document, name)!;

        Assert.Equal(AmlProduct.Integrated, info.Product);
        Assert.True(info.IsAml30);
        Assert.Equal(code, info.ProductCode);
        Assert.Equal(tag, info.TagText);
        Assert.Equal(marking, info.ProtectiveMarking);
        Assert.Equal(AmlMarkingSource.CellName, info.MarkingSource);
        Assert.Equal(band, info.ScaleBand);
        Assert.Null(info.Title);
        Assert.Equal(marking != "UNCLASSIFIED", info.IsMarkedAboveUnclassified);
    }

    [Fact]
    public void SecurityMetaFeature_GivesTheMarking_WhenTheHeaderDoesNot()
    {
        // 506 = Restricted in the AML 1.0 catalogue.
        var document = RoundTrip("UKC6UT00.000", new SyntheticAmlCell.Options { SecurityMarkingCode = 506 });

        var info = AmlDatasetInfo.FromDocument(document, "UKC6UT00.000")!;

        Assert.Equal("RESTRICTED", info.ProtectiveMarking);
        Assert.Equal(AmlMarkingSource.SecurityMetaFeature, info.MarkingSource);
    }

    [Fact]
    public void HeaderMarking_WinsOverMetaFeatureAndName()
    {
        var document = RoundTrip("UKC6UT00.000", new SyntheticAmlCell.Options
        {
            Comment = SyntheticAmlCell.UkhoComment("NATO RESTRICTED"),
            SecurityMarkingCode = 507,
        });

        var info = AmlDatasetInfo.FromDocument(document, "UKC6UT00.000")!;

        Assert.Equal("NATO RESTRICTED", info.ProtectiveMarking);
        Assert.Equal(AmlMarkingSource.Header, info.MarkingSource);
    }

    [Fact]
    public void Enc_IsNotAml()
    {
        var document = RoundTrip("UKC6UT00.000", new SyntheticAmlCell.Options { ProductSpecification = 1, Edition = "2.0", ApplicationProfile = 1 });

        Assert.False(AmlDatasetInfo.IsAml(document));
        Assert.Null(AmlDatasetInfo.FromDocument(document));
    }

    [Fact]
    public void Header_ReadsTheAmlFacts_FromTheLeadingRecords()
    {
        var dir = Directory.CreateTempSubdirectory("aml-header-").FullName;
        try
        {
            var path = SyntheticAmlCell.Write(dir, "AMLDEU6U_NKLK_SED_TrialData.000", new SyntheticAmlCell.Options
            {
                ProductSpecification = 57,
                Edition = "3.0",
            });

            var header = S57DatasetHeader.Read(path)!;

            Assert.Equal(57, header.ProductSpecification);
            Assert.NotNull(header.Aml);
            Assert.Equal("SED", header.Aml!.ProductCode);
            Assert.Equal("UNCLASSIFIED", header.Aml.ProtectiveMarking);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Translator_RefusesAnAmlCell()
    {
        var document = RoundTrip("UKC6UT00.000", new SyntheticAmlCell.Options());

        var error = Assert.Throws<ArgumentException>(() => new S57ToS101Translator().Translate(document));
        Assert.Contains("Additional Military Layers", error.Message);
    }
}
