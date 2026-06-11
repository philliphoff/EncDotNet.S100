using System.Xml.Linq;
using EncDotNet.S100.Gml;
using Xunit;

namespace EncDotNet.S100.Core.Tests;

public class GmlDatasetIdentificationTests
{
    [Theory]
    [InlineData("http://www.iho.int/s100gml/5.0")]
    [InlineData("http://www.iho.int/S100/profile/s100gml/1.0")]
    public void ReadDeclaredEdition_ReadsProductEdition_FromEitherNamespace(string ns)
    {
        XNamespace n = ns;
        var root = new XElement("Dataset",
            new XElement(n + "DatasetIdentificationInformation",
                new XElement(n + "productEdition", "2.0.0")));

        Assert.Equal("2.0.0", GmlDatasetIdentification.ReadDeclaredEdition(root));
    }

    [Fact]
    public void ReadDeclaredEdition_Trims_Whitespace()
    {
        XNamespace n = "http://www.iho.int/s100gml/5.0";
        var root = new XElement("Dataset",
            new XElement(n + "DatasetIdentificationInformation",
                new XElement(n + "productEdition", "  1.0.0  ")));

        Assert.Equal("1.0.0", GmlDatasetIdentification.ReadDeclaredEdition(root));
    }

    [Fact]
    public void ReadDeclaredEdition_ReturnsNull_WhenProductEditionAbsent()
    {
        XNamespace n = "http://www.iho.int/s100gml/5.0";
        var root = new XElement("Dataset",
            new XElement(n + "DatasetIdentificationInformation"));

        Assert.Null(GmlDatasetIdentification.ReadDeclaredEdition(root));
    }

    [Fact]
    public void ReadDeclaredEdition_ReturnsNull_WhenIdentificationBlockAbsent()
    {
        var root = new XElement("Dataset");

        Assert.Null(GmlDatasetIdentification.ReadDeclaredEdition(root));
    }
}
