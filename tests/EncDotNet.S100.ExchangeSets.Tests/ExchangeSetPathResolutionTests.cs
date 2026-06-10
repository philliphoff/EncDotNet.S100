using System.Runtime.CompilerServices;

namespace EncDotNet.S100.ExchangeSets.Tests;

/// <summary>
/// Tests covering source-relative path resolution for real-world S-100
/// exchange set layouts (IC-ENC): a separate <c>filePath</c> directory
/// element, Windows-style/leading-slash paths, <c>file:/</c> prefixes,
/// and dataset discovery items wrapped in product-specific elements and
/// namespaces.
/// </summary>
public class ExchangeSetPathResolutionTests
{
    private static string GetFixturePath(string fileName, [CallerFilePath] string callerFilePath = "")
    {
        return Path.Combine(Path.GetDirectoryName(callerFilePath)!, "..", "datasets", "ExchangeSets", fileName);
    }

    [Theory]
    [InlineData("101GB00502793.000", "101GB00502793.000")]
    [InlineData("file:/S-101/DATASET_FILES/101AU005BTB01.000", "S-101/DATASET_FILES/101AU005BTB01.000")]
    [InlineData("\\S102\\PBC_UTM11N_MLLW_LALB\\102USA16LGBAC200408.H5", "S102/PBC_UTM11N_MLLW_LALB/102USA16LGBAC200408.H5")]
    [InlineData("/leading/slash/file.000", "leading/slash/file.000")]
    public void NormalizeFileName_NormalizesPrefixSeparatorsAndLeadingSlash(string input, string expected)
    {
        Assert.Equal(expected, ExchangeSet.NormalizeFileName(input));
    }

    [Fact]
    public void ResolveRelativePath_JoinsFilePathAndFileName()
    {
        Assert.Equal(
            "101GB00502793/101GB00502793.000",
            ExchangeSet.ResolveRelativePath("101GB00502793", "101GB00502793.000"));
    }

    [Fact]
    public void ResolveRelativePath_NormalizesWindowsSeparatorsAndLeadingSlash()
    {
        Assert.Equal(
            "S102/PBC_UTM11N_MLLW_LALB/102USA16LGBAC200408.H5",
            ExchangeSet.ResolveRelativePath("\\S102\\PBC_UTM11N_MLLW_LALB", "102USA16LGBAC200408.H5"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveRelativePath_FallsBackToFileNameWhenFilePathAbsent(string? filePath)
    {
        Assert.Equal(
            "S-101/DATASET_FILES/101AU005BTB01.000",
            ExchangeSet.ResolveRelativePath(filePath, "file:/S-101/DATASET_FILES/101AU005BTB01.000"));
    }

    [Fact]
    public void ResolveRelativePath_DoesNotDoublePrefixWhenFileNameAlreadyCarriesDirectory()
    {
        Assert.Equal(
            "S102/PBC/file.H5",
            ExchangeSet.ResolveRelativePath("S102/PBC", "S102/PBC/file.H5"));
    }

    [Fact]
    public void Read_SeparateFilePath_CapturesFilePathAndResolvesRelativePath()
    {
        var catalogue = ExchangeCatalogueReader.Read(GetFixturePath("SeparateFilePath_CATALOG.XML"));

        var dataset = Assert.Single(catalogue.DatasetDiscoveryMetadata);
        Assert.Equal("101GB00502793.000", dataset.FileName);
        Assert.Equal("101GB00502793", dataset.FilePath);
        Assert.Equal("101GB00502793/101GB00502793.000", dataset.RelativePath);

        var support = Assert.Single(catalogue.SupportFileDiscoveryMetadata);
        Assert.Equal("support/101GB00G2BXXX.TXT", support.RelativePath);
    }

    [Fact]
    public void Read_ProductSpecificWrapper_ParsesDatasetsAndResolvesRelativePath()
    {
        var catalogue = ExchangeCatalogueReader.Read(GetFixturePath("ProductWrapper_CATALOG.XML"));

        Assert.Equal(2, catalogue.DatasetDiscoveryMetadata.Count);

        var first = catalogue.DatasetDiscoveryMetadata[0];
        Assert.Equal("102USA16LGBAC200408.H5", first.FileName);
        Assert.Equal("\\S102\\PBC_UTM11N_MLLW_LALB", first.FilePath);
        Assert.Equal("S102/PBC_UTM11N_MLLW_LALB/102USA16LGBAC200408.H5", first.RelativePath);
        Assert.Equal("S-102", first.ProductSpecification?.ProductIdentifier);
    }
}
