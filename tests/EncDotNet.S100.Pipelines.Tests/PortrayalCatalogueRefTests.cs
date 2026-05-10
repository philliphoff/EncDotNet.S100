using System.IO;
using System.Text;
using EncDotNet.S100.Core;
using EncDotNet.S100.Portrayals;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

public class PortrayalCatalogueRefTests
{
    private static PortrayalCatalogue Read(string xml) =>
        PortrayalCatalogueReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void Reader_PopulatesCatalogueRef_FromAttributes()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <pc:portrayalCatalog xmlns:pc="http://www.iho.int/S100PortrayalCatalog/5.2" productId="S-101" version="2.0.0" />
            """;
        var pc = Read(xml);
        Assert.Equal("S-101", pc.ProductId);
        Assert.Equal("2.0.0", pc.Version);
        Assert.Equal(new CatalogueRef("S-101", new SpecVersion(2, 0, 0)), pc.CatalogueRef);
    }

    [Fact]
    public void Reader_TwoComponentVersion_PadsToThree()
    {
        // S-127 PC ships with version "2.0" — must parse as 2.0.0.
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <pc:portrayalCatalog xmlns:pc="http://www.iho.int/S100PortrayalCatalog/5.2" productId="S-127" version="2.0" />
            """;
        var pc = Read(xml);
        Assert.Equal(new CatalogueRef("S-127", new SpecVersion(2, 0, 0)), pc.CatalogueRef);
    }

    [Fact]
    public void Reader_MissingMetadata_LeavesCatalogueRefNull()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <pc:portrayalCatalog xmlns:pc="http://www.iho.int/S100PortrayalCatalog/5.2" productId="" version="" />
            """;
        var pc = Read(xml);
        Assert.Null(pc.CatalogueRef);
    }
}
