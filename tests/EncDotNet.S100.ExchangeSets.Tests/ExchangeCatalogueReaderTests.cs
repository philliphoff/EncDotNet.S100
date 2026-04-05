using System.Runtime.CompilerServices;

namespace EncDotNet.S100.ExchangeSets.Tests;

public class ExchangeCatalogueReaderTests
{
    private static string GetCatalogPath([CallerFilePath] string callerFilePath = "")
    {
        return Path.Combine(Path.GetDirectoryName(callerFilePath)!, "..", "datasets", "S101", "CATALOG.XML");
    }

    private static ExchangeCatalogue ReadTestCatalogue()
    {
        return ExchangeCatalogueReader.Read(GetCatalogPath());
    }

    [Fact]
    public void Read_FromFilePath_ReturnsCatalogue()
    {
        var catalogue = ExchangeCatalogueReader.Read(GetCatalogPath());

        Assert.NotNull(catalogue);
    }

    [Fact]
    public void Read_FromStream_ReturnsCatalogue()
    {
        using var stream = File.OpenRead(GetCatalogPath());
        var catalogue = ExchangeCatalogueReader.Read(stream);

        Assert.NotNull(catalogue);
    }

    [Fact]
    public void Identifier_HasExpectedValues()
    {
        var catalogue = ReadTestCatalogue();

        Assert.Equal("IHO_V12", catalogue.Identifier.Identifier);
        Assert.Equal("2023-01-16T12:18:10.336Z", catalogue.Identifier.DateTime);
    }

    [Fact]
    public void Contact_HasExpectedOrganization()
    {
        var catalogue = ReadTestCatalogue();

        Assert.NotNull(catalogue.Contact);
        Assert.Equal("International Hydrographic Organisation", catalogue.Contact.Organization);
    }

    [Fact]
    public void Contact_HasExpectedAddress()
    {
        var catalogue = ReadTestCatalogue();

        Assert.NotNull(catalogue.Contact);
        Assert.Equal("Quai Ste Antoine", catalogue.Contact.DeliveryPoint);
        Assert.Equal("Monte Carlo", catalogue.Contact.City);
        Assert.Equal("Monaco", catalogue.Contact.AdministrativeArea);
        Assert.Equal("ba11 5hf", catalogue.Contact.PostalCode);
    }

    [Fact]
    public void Comment_HasExpectedValue()
    {
        var catalogue = ReadTestCatalogue();

        Assert.Equal("Colleciton of all current S-164 test datasets.", catalogue.Comment);
    }

    [Fact]
    public void Datasets_HasExpectedCount()
    {
        var catalogue = ReadTestCatalogue();

        Assert.Equal(19, catalogue.DatasetDiscoveryMetadata.Count);
    }

    [Fact]
    public void SupportFiles_IsEmpty()
    {
        var catalogue = ReadTestCatalogue();

        Assert.Empty(catalogue.SupportFileDiscoveryMetadata);
    }

    [Fact]
    public void CatalogueFiles_IsEmpty()
    {
        var catalogue = ReadTestCatalogue();

        Assert.Empty(catalogue.CatalogueDiscoveryMetadata);
    }

    [Fact]
    public void FirstDataset_HasExpectedFileName()
    {
        var catalogue = ReadTestCatalogue();

        Assert.Equal("S-101/DATASET_FILES/101AA0000DS0009.000", catalogue.DatasetDiscoveryMetadata[0].FileName);
    }

    [Fact]
    public void FirstDataset_HasExpectedFlags()
    {
        var dataset = ReadTestCatalogue().DatasetDiscoveryMetadata[0];

        Assert.False(dataset.CompressionFlag);
        Assert.False(dataset.DataProtection);
        Assert.False(dataset.Copyright);
        Assert.True(dataset.NotForNavigation);
    }

    [Fact]
    public void FirstDataset_HasExpectedMetadata()
    {
        var dataset = ReadTestCatalogue().DatasetDiscoveryMetadata[0];

        Assert.Equal("DSA", dataset.DigitalSignatureReference);
        Assert.Equal("newDataset", dataset.Purpose);
        Assert.Equal(1, dataset.EditionNumber);
        Assert.Equal("2023-01-16", dataset.IssueDate);
        Assert.Equal("ISO/IEC 8211", dataset.EncodingFormat);
    }

    [Fact]
    public void FirstDataset_HasExpectedProductSpecification()
    {
        var dataset = ReadTestCatalogue().DatasetDiscoveryMetadata[0];

        Assert.NotNull(dataset.ProductSpecification);
        Assert.Equal("S-101", dataset.ProductSpecification.ProductIdentifier);
        Assert.Equal(1, dataset.ProductSpecification.Number);
    }

    [Fact]
    public void FirstDataset_HasExpectedProducingAgency()
    {
        var dataset = ReadTestCatalogue().DatasetDiscoveryMetadata[0];

        Assert.Equal("AA00", dataset.ProducingAgency);
    }

    [Fact]
    public void AllDatasets_HaveConsistentProperties()
    {
        var catalogue = ReadTestCatalogue();

        Assert.All(catalogue.DatasetDiscoveryMetadata, dataset =>
        {
            Assert.Equal("newDataset", dataset.Purpose);
            Assert.True(dataset.NotForNavigation);
            Assert.NotNull(dataset.EditionNumber);
            Assert.Equal("DSA", dataset.DigitalSignatureReference);
            Assert.Equal("ISO/IEC 8211", dataset.EncodingFormat);
            Assert.Equal("AA00", dataset.ProducingAgency);
            Assert.Equal("S-101", dataset.ProductSpecification?.ProductIdentifier);
        });
    }

    [Fact]
    public void ProductSpecification_IsNullAtRootLevel()
    {
        var catalogue = ReadTestCatalogue();

        Assert.Null(catalogue.ProductSpecification);
    }

    [Fact]
    public void DefaultLocale_IsNull()
    {
        var catalogue = ReadTestCatalogue();

        Assert.Null(catalogue.DefaultLocaleLanguage);
        Assert.Null(catalogue.DefaultLocaleCharacterEncoding);
    }
}
