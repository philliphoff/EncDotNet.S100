using System.Runtime.CompilerServices;
using System.Xml;

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
    public void Read_LegacyS100EcSchema_ParsesDatasetsWithoutWrapper()
    {
        // The legacy S100EC schema used by JCOMM/IHO S-411 places the
        // S100_DatasetDiscoveryMetadata records directly under the
        // catalogue root, with no <datasetDiscoveryMetadata> wrapper
        // (unlike modern S-100 Part 17). The reader must still find them.
        const string xml = """
            <ec:S100_ExchangeCatalogue xmlns:ec="http://www.iho.int/S100EC">
                <ec:identifier>
                    <ec:identifier>S411_TEST</ec:identifier>
                    <ec:editionNumber>1.1.0</ec:editionNumber>
                </ec:identifier>
                <ec:S100_DatasetDiscoveryMetadata>
                    <ec:fileName>S411_TEST.gml</ec:fileName>
                    <ec:filePath>/data/S411_TEST.gml</ec:filePath>
                    <ec:description>S-411 Ice Data Set</ec:description>
                    <ec:editionNumber>1.1.0</ec:editionNumber>
                    <ec:updateNumber>not applicable</ec:updateNumber>
                </ec:S100_DatasetDiscoveryMetadata>
            </ec:S100_ExchangeCatalogue>
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var catalogue = ExchangeCatalogueReader.Read(stream);

        Assert.Equal("S411_TEST", catalogue.Identifier.Identifier);
        var dataset = Assert.Single(catalogue.DatasetDiscoveryMetadata);
        Assert.Equal("S411_TEST.gml", dataset.FileName);
        Assert.Equal("/data/S411_TEST.gml", dataset.FilePath);
    }

    [Fact]
    public void Read_EmptyDatasetWrapper_FallsBackToRootLevelDatasets()
    {
        const string xml = """
            <S100XC:S100_ExchangeCatalogue xmlns:S100XC="http://www.iho.int/s100/xc/5.0">
                <S100XC:identifier>
                    <S100XC:identifier>TEST</S100XC:identifier>
                    <S100XC:dateTime>2024-01-01</S100XC:dateTime>
                </S100XC:identifier>
                <S100XC:datasetDiscoveryMetadata />
                <S100XC:S100_DatasetDiscoveryMetadata>
                    <S100XC:fileName>test.000</S100XC:fileName>
                    <S100XC:filePath>data/test.000</S100XC:filePath>
                </S100XC:S100_DatasetDiscoveryMetadata>
            </S100XC:S100_ExchangeCatalogue>
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var catalogue = ExchangeCatalogueReader.Read(stream);

        var dataset = Assert.Single(catalogue.DatasetDiscoveryMetadata);
        Assert.Equal("test.000", dataset.FileName);
        Assert.Equal("data/test.000", dataset.FilePath);
    }

    [Fact]
    public void Dataset_ExpectedHash_IsParsedFromHashMrn()
    {
        const string xml = """
            <S100XC:S100_ExchangeCatalogue xmlns:S100XC="http://www.iho.int/s100/xc/5.0">
                <S100XC:identifier>
                    <S100XC:identifier>TEST</S100XC:identifier>
                    <S100XC:dateTime>2024-01-01</S100XC:dateTime>
                </S100XC:identifier>
                <S100XC:datasetDiscoveryMetadata>
                    <S100XC:S100_DatasetDiscoveryMetadata>
                        <S100XC:fileName>test.000</S100XC:fileName>
                        <S100XC:resourceHash>urn:mrn:iho:s100:hash:sha256:a948904f2f0f479b8f8197694b30184b0d2ed1c1cd2a1ec0fb85d299a192a447</S100XC:resourceHash>
                    </S100XC:S100_DatasetDiscoveryMetadata>
                </S100XC:datasetDiscoveryMetadata>
            </S100XC:S100_ExchangeCatalogue>
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var catalogue = ExchangeCatalogueReader.Read(stream);

        var dataset = Assert.Single(catalogue.DatasetDiscoveryMetadata);
        Assert.NotNull(dataset.ExpectedHash);
        Assert.Equal("sha256", dataset.ExpectedHash.Algorithm);
        Assert.Equal(
            "a948904f2f0f479b8f8197694b30184b0d2ed1c1cd2a1ec0fb85d299a192a447",
            dataset.ExpectedHash.HexValue);
    }

    [Fact]
    public void Dataset_ExpectedHash_IsNullWhenAbsent()
    {
        var catalogue = ReadTestCatalogue();

        Assert.All(catalogue.DatasetDiscoveryMetadata, ds => Assert.Null(ds.ExpectedHash));
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
    public void SupportFiles_RepeatedInlineElements_WithFileLocation_AreParsed()
    {
        // Real-world S-101 trial cells repeat <supportFileDiscoveryMetadata>
        // with the fields inline and declare the directory via <fileLocation>.
        const string xml = """
            <S100XC:S100_ExchangeCatalogue xmlns:S100XC="http://www.iho.int/s100/xc/5.0">
                <S100XC:identifier>
                    <S100XC:identifier>TEST</S100XC:identifier>
                    <S100XC:dateTime>2024-01-01</S100XC:dateTime>
                </S100XC:identifier>
                <S100XC:supportFileDiscoveryMetadata>
                    <S100XC:fileName>101GB00N00659.TXT</S100XC:fileName>
                    <S100XC:fileLocation>support</S100XC:fileLocation>
                </S100XC:supportFileDiscoveryMetadata>
                <S100XC:supportFileDiscoveryMetadata>
                    <S100XC:fileName>101GB00N00660.TXT</S100XC:fileName>
                    <S100XC:fileLocation>support</S100XC:fileLocation>
                </S100XC:supportFileDiscoveryMetadata>
            </S100XC:S100_ExchangeCatalogue>
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var catalogue = ExchangeCatalogueReader.Read(stream);

        Assert.Equal(2, catalogue.SupportFileDiscoveryMetadata.Count);
        var first = catalogue.SupportFileDiscoveryMetadata[0];
        Assert.Equal("101GB00N00659.TXT", first.FileName);
        Assert.Equal("support", first.FilePath);
        Assert.Equal("support/101GB00N00659.TXT", first.RelativePath);
    }

    [Fact]
    public void SupportFiles_ContainerWithTypedChildren_AreParsed()
    {
        // The other valid encoding: a single container wrapping a typed
        // S100_SupportFileDiscoveryMetadata record using <filePath>.
        const string xml = """
            <S100XC:S100_ExchangeCatalogue xmlns:S100XC="http://www.iho.int/s100/xc/5.0">
                <S100XC:identifier>
                    <S100XC:identifier>TEST</S100XC:identifier>
                    <S100XC:dateTime>2024-01-01</S100XC:dateTime>
                </S100XC:identifier>
                <S100XC:supportFileDiscoveryMetadata>
                    <S100XC:S100_SupportFileDiscoveryMetadata>
                        <S100XC:fileName>101GB00G2BXXX.TXT</S100XC:fileName>
                        <S100XC:filePath>support</S100XC:filePath>
                    </S100XC:S100_SupportFileDiscoveryMetadata>
                </S100XC:supportFileDiscoveryMetadata>
            </S100XC:S100_ExchangeCatalogue>
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var catalogue = ExchangeCatalogueReader.Read(stream);

        var entry = Assert.Single(catalogue.SupportFileDiscoveryMetadata);
        Assert.Equal("101GB00G2BXXX.TXT", entry.FileName);
        Assert.Equal("support/101GB00G2BXXX.TXT", entry.RelativePath);
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

    private static ExchangeCatalogue ReadXml(string xml)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        return ExchangeCatalogueReader.Read(stream);
    }

    [Fact]
    public void Read_MissingIdentifierElement_ThrowsXmlExceptionNamingElement()
    {
        const string xml = """
            <S100XC:S100_ExchangeCatalogue xmlns:S100XC="http://www.iho.int/s100/xc/5.0">
                <S100XC:exchangeCatalogueComment>No identifier</S100XC:exchangeCatalogueComment>
            </S100XC:S100_ExchangeCatalogue>
            """;

        var exception = Assert.Throws<XmlException>(() => ReadXml(xml));

        Assert.Contains("'S100_ExchangeCatalogue'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'identifier'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_MissingIdentifierValue_ThrowsXmlExceptionNamingElement()
    {
        const string xml = """
            <S100XC:S100_ExchangeCatalogue xmlns:S100XC="http://www.iho.int/s100/xc/5.0">
                <S100XC:identifier>
                    <S100XC:dateTime>2024-01-01</S100XC:dateTime>
                </S100XC:identifier>
            </S100XC:S100_ExchangeCatalogue>
            """;

        var exception = Assert.Throws<XmlException>(() => ReadXml(xml));

        Assert.Contains("missing required element 'identifier'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("datasetDiscoveryMetadata", "S100_DatasetDiscoveryMetadata")]
    [InlineData("supportFileDiscoveryMetadata", "S100_SupportFileDiscoveryMetadata")]
    [InlineData("catalogueDiscoveryMetadata", "S100_CatalogueDiscoveryMetadata")]
    public void Read_DiscoveryRecordWithoutFileName_ThrowsXmlExceptionNamingElement(
        string wrapper, string record)
    {
        var xml = $"""
            <S100XC:S100_ExchangeCatalogue xmlns:S100XC="http://www.iho.int/s100/xc/5.0">
                <S100XC:identifier>
                    <S100XC:identifier>TEST</S100XC:identifier>
                    <S100XC:dateTime>2024-01-01</S100XC:dateTime>
                </S100XC:identifier>
                <S100XC:{wrapper}>
                    <S100XC:{record}>
                        <S100XC:filePath>data</S100XC:filePath>
                    </S100XC:{record}>
                </S100XC:{wrapper}>
            </S100XC:S100_ExchangeCatalogue>
            """;

        var exception = Assert.Throws<XmlException>(() => ReadXml(xml));

        Assert.Contains($"'{record}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'fileName'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_LegacyS100EcIdentifierDate_IsUsedAsDateTime()
    {
        // Legacy S100EC catalogues (e.g. S-411) carry identifier/date wrapping
        // a gco:Date instead of identifier/dateTime.
        const string xml = """
            <ec:S100_ExchangeCatalogue xmlns:ec="http://www.iho.int/S100EC"
                                       xmlns:gco="http://www.isotc211.org/2005/gco">
                <ec:identifier>
                    <ec:identifier>S411_TEST</ec:identifier>
                    <ec:date>
                        <gco:Date>20260420</gco:Date>
                    </ec:date>
                </ec:identifier>
            </ec:S100_ExchangeCatalogue>
            """;

        var catalogue = ReadXml(xml);

        Assert.Equal("20260420", catalogue.Identifier.DateTime);
    }

    [Fact]
    public void Read_IdentifierWithoutDateTimeOrDate_YieldsEmptyDateTime()
    {
        const string xml = """
            <ec:S100_ExchangeCatalogue xmlns:ec="http://www.iho.int/S100EC">
                <ec:identifier>
                    <ec:identifier>S411_TEST</ec:identifier>
                </ec:identifier>
            </ec:S100_ExchangeCatalogue>
            """;

        var catalogue = ReadXml(xml);

        Assert.Equal("", catalogue.Identifier.DateTime);
    }
}
