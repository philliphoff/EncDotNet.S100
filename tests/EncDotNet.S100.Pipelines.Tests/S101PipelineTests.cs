using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests the S-101 dataset adapters (<see cref="S101FeatureXmlSource"/>)
/// wired through to the <see cref="VectorPipeline"/> end-to-end using
/// inline FeatureXML and XSLT rules as fakes.
/// </summary>
public class S101PipelineTests
{
    /// <summary>
    /// Verifies that S-101 FeatureXML sourced through a fake IFeatureXmlSource
    /// runs through XSLT and produces the expected PointInstruction.
    /// </summary>
    [Fact]
    public async Task S101_PointFeature_XsltProducesPointInstruction()
    {
        // Simulate FeatureXML that an S101FeatureXmlSource would emit
        var source = new InlineFeatureXmlSource(
            featureTypes: ["BOYLAT"],
            featureXml: """
                <Dataset xmlns="http://www.iho.int/s100/5.0">
                  <Feature id="42" type="BOYLAT">
                    <Geometry>
                      <Point lat="47.6" lon="-122.3"/>
                    </Geometry>
                    <Attribute code="COLOUR">3</Attribute>
                    <Attribute code="BOYSHP">1</Attribute>
                  </Feature>
                </Dataset>
                """);

        var xslt = CompileXslt("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:S100="http://www.iho.int/s100/5.0">
              <xsl:template match="/">
                <displayList>
                  <xsl:for-each select="//S100:Feature[@type='BOYLAT']">
                    <pointInstruction>
                      <featureReference><xsl:value-of select="@id"/></featureReference>
                      <drawingPriority>8</drawingPriority>
                      <viewingGroup>17020</viewingGroup>
                      <displayPlane>OverRadar</displayPlane>
                      <symbol reference="BOYLAT01"/>
                    </pointInstruction>
                  </xsl:for-each>
                </displayList>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeS101PortrayalCatalogue(
        [
            new PortrayalRule
            {
                Name = "BOYLAT",
                Type = PortrayalRuleType.Xslt,
                ExecutionOrder = 1,
                AppliesTo = ["BOYLAT"],
            },
        ],
        xsltRules: new() { ["BOYLAT"] = xslt });

        var pipeline = new VectorPipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Single(layer.Instructions);
        var inst = Assert.IsType<PointInstruction>(layer.Instructions[0]);
        Assert.Equal("42", inst.FeatureReference);
        Assert.Equal("BOYLAT01", inst.SymbolReference);
        Assert.Equal(17020, inst.ViewingGroup);
    }

    /// <summary>
    /// Verifies that area features (DEPARE) produce AreaInstructions through XSLT.
    /// </summary>
    [Fact]
    public async Task S101_AreaFeature_XsltProducesAreaInstruction()
    {
        var source = new InlineFeatureXmlSource(
            featureTypes: ["DEPARE"],
            featureXml: """
                <Dataset xmlns="http://www.iho.int/s100/5.0">
                  <Feature id="100" type="DEPARE">
                    <Geometry>
                      <Surface>
                        <Ring type="exterior">
                          <Point lat="47.5" lon="-122.4"/>
                          <Point lat="47.6" lon="-122.4"/>
                          <Point lat="47.6" lon="-122.3"/>
                          <Point lat="47.5" lon="-122.3"/>
                        </Ring>
                      </Surface>
                    </Geometry>
                    <Attribute code="DRVAL1">5.0</Attribute>
                    <Attribute code="DRVAL2">10.0</Attribute>
                  </Feature>
                </Dataset>
                """);

        var xslt = CompileXslt("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:S100="http://www.iho.int/s100/5.0">
              <xsl:template match="/">
                <displayList>
                  <xsl:for-each select="//S100:Feature[@type='DEPARE']">
                    <areaInstruction>
                      <featureReference><xsl:value-of select="@id"/></featureReference>
                      <drawingPriority>2</drawingPriority>
                      <viewingGroup>13010</viewingGroup>
                      <displayPlane>UnderRadar</displayPlane>
                      <areaFillReference reference="DEPARE01"/>
                    </areaInstruction>
                  </xsl:for-each>
                </displayList>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeS101PortrayalCatalogue(
        [
            new PortrayalRule { Name = "DEPARE", Type = PortrayalRuleType.Xslt, ExecutionOrder = 1, AppliesTo = ["DEPARE"] },
        ],
        xsltRules: new() { ["DEPARE"] = xslt });

        var pipeline = new VectorPipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Single(layer.Instructions);
        var inst = Assert.IsType<AreaInstruction>(layer.Instructions[0]);
        Assert.Equal("100", inst.FeatureReference);
        Assert.Equal(DisplayPlane.UnderRadar, inst.Plane);
        Assert.Equal("DEPARE01", inst.AreaFillReference);
    }

    /// <summary>
    /// Verifies that mixed S-101 feature types sort correctly through the pipeline.
    /// </summary>
    [Fact]
    public async Task S101_MixedFeatures_SortedByPlaneAndType()
    {
        var source = new InlineFeatureXmlSource(
            featureTypes: ["BOYLAT", "DEPCNT", "DEPARE"],
            featureXml: "<Dataset xmlns='http://www.iho.int/s100/5.0'/>");

        var xslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <displayList>
                  <pointInstruction>
                    <featureReference>buoy</featureReference>
                    <drawingPriority>5</drawingPriority>
                    <viewingGroup>17020</viewingGroup>
                    <displayPlane>OverRadar</displayPlane>
                    <symbol reference="BOYLAT01"/>
                  </pointInstruction>
                  <lineInstruction>
                    <featureReference>contour</featureReference>
                    <drawingPriority>5</drawingPriority>
                    <viewingGroup>33020</viewingGroup>
                    <displayPlane>OverRadar</displayPlane>
                    <lineStyleReference reference="DEPCNT02"/>
                  </lineInstruction>
                  <areaInstruction>
                    <featureReference>deparea</featureReference>
                    <drawingPriority>5</drawingPriority>
                    <viewingGroup>13010</viewingGroup>
                    <displayPlane>OverRadar</displayPlane>
                    <areaFillReference reference="DEPARE01"/>
                  </areaInstruction>
                </displayList>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeS101PortrayalCatalogue(
        [
            new PortrayalRule { Name = "All", Type = PortrayalRuleType.Xslt, ExecutionOrder = 1, AppliesTo = ["BOYLAT", "DEPCNT", "DEPARE"] }
        ],
        xsltRules: new() { ["All"] = xslt });

        var pipeline = new VectorPipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Equal(3, layer.Instructions.Count);
        // S-100 Part 9: area → line → point within same priority
        Assert.IsType<AreaInstruction>(layer.Instructions[0]);
        Assert.IsType<LineInstruction>(layer.Instructions[1]);
        Assert.IsType<PointInstruction>(layer.Instructions[2]);
    }

    /// <summary>
    /// Verifies that InlineFeatureXmlSource correctly reports feature types present.
    /// </summary>
    [Fact]
    public void FeatureXmlSource_ReportsCorrectFeatureTypes()
    {
        var source = new InlineFeatureXmlSource(
            featureTypes: ["BOYLAT", "DEPARE", "DEPCNT"],
            featureXml: "<Dataset/>");

        Assert.Equal(3, source.FeatureTypesPresent.Count);
        Assert.Contains("BOYLAT", source.FeatureTypesPresent);
        Assert.Contains("DEPARE", source.FeatureTypesPresent);
        Assert.Contains("DEPCNT", source.FeatureTypesPresent);
    }

    #region Helpers

    private static XslCompiledTransform CompileXslt(string xslt)
    {
        var transform = new XslCompiledTransform();
        using var reader = XmlReader.Create(new StringReader(xslt));
        transform.Load(reader);
        return transform;
    }

    #endregion

    #region Fakes

    /// <summary>
    /// A feature XML source backed by inline strings, simulating what
    /// S101FeatureXmlSource would produce from a real S-101 dataset.
    /// </summary>
    private sealed class InlineFeatureXmlSource : IFeatureXmlSource
    {
        private readonly string _featureXml;

        public InlineFeatureXmlSource(IReadOnlyList<string> featureTypes, string featureXml)
        {
            FeatureTypesPresent = featureTypes;
            _featureXml = featureXml;
        }

        public IReadOnlyList<string> FeatureTypesPresent { get; }

        public XmlReader GetFeatureXml() =>
            XmlReader.Create(new StringReader(_featureXml));
    }

    private sealed class FakeS101PortrayalCatalogue : IVectorPortrayalCatalogue
    {
        private readonly Dictionary<string, XslCompiledTransform> _xsltRules;

        public FakeS101PortrayalCatalogue(
            IReadOnlyList<PortrayalRule> rules,
            Dictionary<string, XslCompiledTransform>? xsltRules = null,
            ViewingGroupController? viewingGroups = null)
        {
            Rules = rules;
            _xsltRules = xsltRules ?? new();
            ViewingGroups = viewingGroups ?? new ViewingGroupController();
        }

        public string ProductSpec => "S-101";
        public string Edition => "1.2.0";
        public ColorPalette ActivePalette => ColorPalette.Default;
        public void SwitchPalette(PaletteType type) { }

        public IReadOnlyList<PortrayalRule> Rules { get; }
        public ViewingGroupController ViewingGroups { get; }

        public DisplayModeController DisplayModes { get; } = new();

        public DisplayPlaneController DisplayPlanes { get; } = new();

        public XslCompiledTransform GetCompiledRule(string ruleName) =>
            _xsltRules.TryGetValue(ruleName, out var t) ? t : throw new KeyNotFoundException(ruleName);

        public Script GetLuaScript(string scriptName) => throw new NotImplementedException();

        public SvgSymbol GetSymbol(string symbolName) =>
            new() { Name = symbolName, SvgContent = $"<svg id=\"{symbolName}\"/>" };

        public LineStyle GetLineStyle(string name) =>
            new() { Name = name, Width = 1.0f, Color = "#000000" };

        public AreaFill GetAreaFill(string name) =>
            new() { Name = name, Color = "#C8C8C8" };
    }

    #endregion
}
