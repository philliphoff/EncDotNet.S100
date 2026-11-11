using System.Text;
using System.Xml;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests that <see cref="PortrayalCatalogueReader"/> rejects a catalogue missing
/// an element or attribute that the model declares non-nullable with an
/// <see cref="XmlException"/> naming it, instead of leaving <see langword="null"/>
/// in a required property, while the documented lenient fields still parse.
/// </summary>
public class PortrayalCatalogueRequiredElementTests
{
    // A minimal but complete catalogue. Each test removes one required piece
    // by replacing its exact (unique) text.
    private const string ValidCatalogue = """
        <?xml version="1.0" encoding="utf-8"?>
        <pc:portrayalCatalog xmlns:pc="http://www.iho.int/S100PortrayalCatalog/5.2" productId="S-101" version="2.0.0">
          <pc:symbols>
            <pc:symbol id="BOYCAN01">
              <pc:fileName>BOYCAN01.svg</pc:fileName>
              <pc:fileType>Symbol</pc:fileType>
              <pc:fileFormat>SVG</pc:fileFormat>
            </pc:symbol>
          </pc:symbols>
          <pc:viewingGroupLayers>
            <pc:viewingGroupLayer id="layer1">
              <pc:viewingGroup>27010</pc:viewingGroup>
            </pc:viewingGroupLayer>
          </pc:viewingGroupLayers>
          <pc:displayModes>
            <pc:displayMode id="Standard">
              <pc:viewingGroupLayer>layer1</pc:viewingGroupLayer>
            </pc:displayMode>
          </pc:displayModes>
          <pc:displayPlanes>
            <pc:displayPlane id="OverRADAR" order="1"/>
          </pc:displayPlanes>
          <pc:context>
            <pc:parameter id="SafetyContour">
              <pc:type>Real</pc:type>
              <pc:default>30.0</pc:default>
            </pc:parameter>
          </pc:context>
          <pc:rules>
            <pc:ruleFile id="main">
              <pc:fileName>main.lua</pc:fileName>
              <pc:fileType>Rule</pc:fileType>
              <pc:fileFormat>LUA</pc:fileFormat>
              <pc:ruleType>TopLevelTemplate</pc:ruleType>
            </pc:ruleFile>
          </pc:rules>
        </pc:portrayalCatalog>
        """;

    private static PortrayalCatalogue Read(string xml) =>
        PortrayalCatalogueReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string Replace(string fragment, string replacement)
    {
        var index = ValidCatalogue.IndexOf(fragment, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Fragment not found: {fragment}");
        Assert.Equal(-1, ValidCatalogue.IndexOf(fragment, index + 1, StringComparison.Ordinal));
        return ValidCatalogue.Replace(fragment, replacement, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidCatalogue_Parses()
    {
        var pc = Read(ValidCatalogue);

        Assert.Equal("BOYCAN01.svg", Assert.Single(pc.Symbols).FileName);
        Assert.Equal("TopLevelTemplate", Assert.Single(pc.RuleFiles).RuleType);
        Assert.Equal("30.0", Assert.Single(pc.ContextParameters).Default);
    }

    [Theory]
    [InlineData("<pc:fileName>BOYCAN01.svg</pc:fileName>", "symbol", "fileName")]
    [InlineData("<pc:fileType>Symbol</pc:fileType>", "symbol", "fileType")]
    [InlineData("<pc:fileFormat>SVG</pc:fileFormat>", "symbol", "fileFormat")]
    [InlineData("<pc:fileName>main.lua</pc:fileName>", "ruleFile", "fileName")]
    [InlineData("<pc:fileType>Rule</pc:fileType>", "ruleFile", "fileType")]
    [InlineData("<pc:fileFormat>LUA</pc:fileFormat>", "ruleFile", "fileFormat")]
    [InlineData("<pc:ruleType>TopLevelTemplate</pc:ruleType>", "ruleFile", "ruleType")]
    [InlineData("<pc:type>Real</pc:type>", "parameter", "type")]
    [InlineData("<pc:default>30.0</pc:default>", "parameter", "default")]
    public void MissingRequiredElement_ThrowsXmlExceptionNamingIt(
        string fragment, string parent, string missing)
    {
        var exception = Assert.Throws<XmlException>(() => Read(Replace(fragment, "")));

        Assert.Contains($"'{parent}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"missing required element '{missing}'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""<pc:ruleFile id="main">""", "<pc:ruleFile>", "ruleFile")]
    [InlineData("""<pc:parameter id="SafetyContour">""", "<pc:parameter>", "parameter")]
    [InlineData("""<pc:viewingGroupLayer id="layer1">""", "<pc:viewingGroupLayer>", "viewingGroupLayer")]
    [InlineData("""<pc:displayMode id="Standard">""", "<pc:displayMode>", "displayMode")]
    [InlineData("""<pc:displayPlane id="OverRADAR" order="1"/>""", """<pc:displayPlane order="1"/>""", "displayPlane")]
    public void MissingIdAttribute_ThrowsXmlExceptionNamingIt(
        string fragment, string replacement, string element)
    {
        var exception = Assert.Throws<XmlException>(() => Read(Replace(fragment, replacement)));

        Assert.Contains($"'{element}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing required attribute 'id'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogItemWithoutId_ReadsEmptyId()
    {
        var pc = Read(Replace("""<pc:symbol id="BOYCAN01">""", "<pc:symbol>"));

        Assert.Equal("", Assert.Single(pc.Symbols).Id);
    }

    [Fact]
    public void RootWithoutProductIdOrVersion_ReadsEmptyStrings()
    {
        var pc = Read(Replace(""" productId="S-101" version="2.0.0">""", ">"));

        Assert.Equal("", pc.ProductId);
        Assert.Equal("", pc.Version);
    }
}
