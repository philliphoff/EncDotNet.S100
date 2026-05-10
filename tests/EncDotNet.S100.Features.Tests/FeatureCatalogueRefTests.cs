using System.IO;
using System.Text;
using EncDotNet.S100.Core;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Features.Tests;

public class FeatureCatalogueRefTests
{
    private static FeatureCatalogue Read(string xml) =>
        FeatureCatalogueReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void Reader_PopulatesCatalogueRef_FromProductIdAndVersionNumber()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <S100FC:S100_FC_FeatureCatalogue
                xmlns:S100FC="http://www.iho.int/S100FC/5.2"
                xmlns:S100Base="http://www.iho.int/S100Base/5.0"
                xmlns:S100CI="http://www.iho.int/S100CI/5.0">
              <S100FC:name>Test</S100FC:name>
              <S100FC:versionNumber>2.0.0</S100FC:versionNumber>
              <S100FC:versionDate>2024-10-16</S100FC:versionDate>
              <S100FC:productId>S-101</S100FC:productId>
            </S100FC:S100_FC_FeatureCatalogue>
            """;
        var fc = Read(xml);
        Assert.Equal("S-101", fc.ProductId);
        Assert.Equal(new CatalogueRef("S-101", new SpecVersion(2, 0, 0)), fc.CatalogueRef);
    }

    [Fact]
    public void Reader_NoProductId_LeavesCatalogueRefNull()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <S100FC:S100_FC_FeatureCatalogue
                xmlns:S100FC="http://www.iho.int/S100FC/5.2"
                xmlns:S100Base="http://www.iho.int/S100Base/5.0"
                xmlns:S100CI="http://www.iho.int/S100CI/5.0">
              <S100FC:name>Test</S100FC:name>
              <S100FC:versionNumber>1.0.0</S100FC:versionNumber>
              <S100FC:versionDate>2024-10-16</S100FC:versionDate>
            </S100FC:S100_FC_FeatureCatalogue>
            """;
        var fc = Read(xml);
        Assert.Null(fc.ProductId);
        Assert.Null(fc.CatalogueRef);
    }

    [Fact]
    public void Reader_NormalisesProductId_InCatalogueRef()
    {
        // "S101" (no hyphen) must normalise to canonical "S-101".
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <S100FC:S100_FC_FeatureCatalogue
                xmlns:S100FC="http://www.iho.int/S100FC/5.2"
                xmlns:S100Base="http://www.iho.int/S100Base/5.0"
                xmlns:S100CI="http://www.iho.int/S100CI/5.0">
              <S100FC:name>Test</S100FC:name>
              <S100FC:versionNumber>2.0.0</S100FC:versionNumber>
              <S100FC:versionDate>2024-10-16</S100FC:versionDate>
              <S100FC:productId>S101</S100FC:productId>
            </S100FC:S100_FC_FeatureCatalogue>
            """;
        var fc = Read(xml);
        Assert.Equal("S101", fc.ProductId);
        Assert.Equal("S-101", fc.CatalogueRef!.Value.Name);
    }
}
