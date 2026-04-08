using System.Xml;
using System.Xml.Xsl;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

public class VectorPipelineTests
{
    [Fact]
    public async Task ProcessAsync_XsltPointRule_ProducesPointInstruction()
    {
        var source = new FakeFeatureXmlSource(
            featureTypes: ["Buoy"],
            featureXml: """
                <Dataset>
                  <Feature id="1" type="Buoy">
                    <Position lat="47.6" lon="-122.3"/>
                  </Feature>
                </Dataset>
                """);

        // XSLT rule that emits a PointInstruction for each Buoy feature
        var xslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <DrawingInstructions>
                  <xsl:for-each select="//Feature[@type='Buoy']">
                    <PointInstruction id="{@id}" priority="8" viewingGroup="21010" plane="OverRadar">
                      <Position lat="{Position/@lat}" lon="{Position/@lon}"/>
                      <Symbol ref="BOYLAT01" rotation="0"/>
                    </PointInstruction>
                  </xsl:for-each>
                </DrawingInstructions>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeVectorPortrayalCatalogue(
        [
            new PortrayalRule
            {
                Name = "BuoyRule",
                Type = PortrayalRuleType.Xslt,
                ExecutionOrder = 1,
                AppliesTo = ["Buoy"],
            },
        ],
        xsltRules: new() { ["BuoyRule"] = xslt });

        var pipeline = new VectorPipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Single(layer.Instructions);
        var inst = Assert.IsType<PointInstruction>(layer.Instructions[0]);
        Assert.Equal("1", inst.FeatureId);
        Assert.Equal(DisplayPlane.OverRadar, inst.Plane);
        Assert.Equal(47.6, inst.Latitude);
        Assert.Equal(-122.3, inst.Longitude);
        Assert.Equal("BOYLAT01", inst.Symbol.Name);
    }

    [Fact]
    public async Task ProcessAsync_XsltLineRule_ProducesLineInstruction()
    {
        var source = new FakeFeatureXmlSource(
            featureTypes: ["DepthContour"],
            featureXml: """
                <Dataset>
                  <Feature id="2" type="DepthContour"/>
                </Dataset>
                """);

        var xslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <DrawingInstructions>
                  <LineInstruction id="2" priority="4" viewingGroup="23010" plane="UnderRadar">
                    <Geometry>
                      <Point lat="47.6" lon="-122.4"/>
                      <Point lat="47.5" lon="-122.3"/>
                    </Geometry>
                    <LineStyle ref="DEPCNT02"/>
                  </LineInstruction>
                </DrawingInstructions>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeVectorPortrayalCatalogue(
        [
            new PortrayalRule
            {
                Name = "ContourRule",
                Type = PortrayalRuleType.Xslt,
                ExecutionOrder = 1,
                AppliesTo = ["DepthContour"],
            },
        ],
        xsltRules: new() { ["ContourRule"] = xslt });

        var pipeline = new VectorPipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Single(layer.Instructions);
        var inst = Assert.IsType<LineInstruction>(layer.Instructions[0]);
        Assert.Equal("2", inst.FeatureId);
        Assert.Equal(DisplayPlane.UnderRadar, inst.Plane);
        Assert.Equal(2, inst.Geometry.Count);
        Assert.Equal("DEPCNT02", inst.LineStyle.Name);
    }

    [Fact]
    public async Task ProcessAsync_XsltAreaRule_ProducesAreaInstruction()
    {
        var source = new FakeFeatureXmlSource(
            featureTypes: ["LandArea"],
            featureXml: "<Dataset><Feature id='3' type='LandArea'/></Dataset>");

        var xslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <DrawingInstructions>
                  <AreaInstruction id="3" priority="2" viewingGroup="12410" plane="UnderRadar">
                    <Ring>
                      <Point lat="47.5" lon="-122.4"/>
                      <Point lat="47.6" lon="-122.4"/>
                      <Point lat="47.6" lon="-122.3"/>
                      <Point lat="47.5" lon="-122.3"/>
                    </Ring>
                    <AreaFill ref="LANDF"/>
                  </AreaInstruction>
                </DrawingInstructions>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeVectorPortrayalCatalogue(
        [
            new PortrayalRule
            {
                Name = "LandRule",
                Type = PortrayalRuleType.Xslt,
                ExecutionOrder = 1,
                AppliesTo = ["LandArea"],
            },
        ],
        xsltRules: new() { ["LandRule"] = xslt });

        var pipeline = new VectorPipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Single(layer.Instructions);
        var inst = Assert.IsType<AreaInstruction>(layer.Instructions[0]);
        Assert.Equal("3", inst.FeatureId);
        Assert.Single(inst.Rings);
        Assert.Equal(4, inst.Rings[0].Count);
        Assert.Equal("LANDF", inst.AreaFill.Name);
    }

    [Fact]
    public async Task ProcessAsync_TextInstruction_Assembled()
    {
        var source = new FakeFeatureXmlSource(
            featureTypes: ["Sounding"],
            featureXml: "<Dataset><Feature id='4' type='Sounding'/></Dataset>");

        var xslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <DrawingInstructions>
                  <TextInstruction id="4" priority="9" viewingGroup="33010" plane="OverRadar">
                    <Position lat="47.55" lon="-122.35"/>
                    <Text>12.5</Text>
                    <TextStyle ref="TEXTA01" fontSize="12" color="#000000"/>
                  </TextInstruction>
                </DrawingInstructions>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeVectorPortrayalCatalogue(
        [
            new PortrayalRule
            {
                Name = "SoundingRule",
                Type = PortrayalRuleType.Xslt,
                ExecutionOrder = 1,
                AppliesTo = ["Sounding"],
            },
        ],
        xsltRules: new() { ["SoundingRule"] = xslt });

        var pipeline = new VectorPipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Single(layer.Instructions);
        var inst = Assert.IsType<TextInstruction>(layer.Instructions[0]);
        Assert.Equal("4", inst.FeatureId);
        Assert.Equal("12.5", inst.Text);
        Assert.Equal(12.0, inst.FontSize);
    }

    [Fact]
    public async Task ProcessAsync_RuleSelectionSkipsIrrelevantFeatureTypes()
    {
        // Source has only "LandArea" features
        var source = new FakeFeatureXmlSource(
            featureTypes: ["LandArea"],
            featureXml: "<Dataset><Feature id='1' type='LandArea'/></Dataset>");

        // Separate XSLT for the buoy rule (should NOT run)
        var buoyXslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <DrawingInstructions>
                  <PointInstruction id="SHOULD_NOT_APPEAR" priority="1" viewingGroup="1">
                    <Position lat="0" lon="0"/><Symbol ref="BOYLAT01"/>
                  </PointInstruction>
                </DrawingInstructions>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var landXslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <DrawingInstructions>
                  <AreaInstruction id="1" priority="2" viewingGroup="12410" plane="UnderRadar">
                    <Geometry><Point lat="47.5" lon="-122.4"/></Geometry>
                    <AreaFill ref="LANDF"/>
                  </AreaInstruction>
                </DrawingInstructions>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeVectorPortrayalCatalogue(
        [
            new PortrayalRule { Name = "BuoyRule", Type = PortrayalRuleType.Xslt, ExecutionOrder = 1, AppliesTo = ["Buoy"] },
            new PortrayalRule { Name = "LandRule", Type = PortrayalRuleType.Xslt, ExecutionOrder = 2, AppliesTo = ["LandArea"] },
        ],
        xsltRules: new() { ["BuoyRule"] = buoyXslt, ["LandRule"] = landXslt });

        var pipeline = new VectorPipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue);

        // Only the land rule should have produced output
        Assert.Single(layer.Instructions);
        Assert.Equal("1", layer.Instructions[0].FeatureId);
    }

    [Fact]
    public async Task ProcessAsync_AlwaysApplyRule_RunsRegardlessOfFeatureTypes()
    {
        var source = new FakeFeatureXmlSource(
            featureTypes: ["Buoy"],
            featureXml: "<Dataset><Feature id='1' type='Buoy'/></Dataset>");

        var metaXslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <DrawingInstructions>
                  <PointInstruction id="meta" priority="0" viewingGroup="10000" plane="UnderRadar">
                    <Position lat="0" lon="0"/><Symbol ref="META01"/>
                  </PointInstruction>
                </DrawingInstructions>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeVectorPortrayalCatalogue(
        [
            // AlwaysApply rule with no AppliesTo match for "Buoy"
            new PortrayalRule
            {
                Name = "MetaRule",
                Type = PortrayalRuleType.Xslt,
                ExecutionOrder = 0,
                AppliesTo = [],
                AlwaysApply = true,
            },
        ],
        xsltRules: new() { ["MetaRule"] = metaXslt });

        var pipeline = new VectorPipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Single(layer.Instructions);
        Assert.Equal("meta", layer.Instructions[0].FeatureId);
    }

    [Fact]
    public async Task ProcessAsync_HiddenViewingGroup_Filtered()
    {
        var source = new FakeFeatureXmlSource(
            featureTypes: ["Buoy"],
            featureXml: "<Dataset><Feature id='1' type='Buoy'/></Dataset>");

        var xslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <DrawingInstructions>
                  <PointInstruction id="1" priority="8" viewingGroup="21010" plane="OverRadar">
                    <Position lat="47.6" lon="-122.3"/><Symbol ref="BOYLAT01"/>
                  </PointInstruction>
                </DrawingInstructions>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var viewingGroups = new ViewingGroupController();
        viewingGroups.SetVisible(21010, false);

        var catalogue = new FakeVectorPortrayalCatalogue(
        [
            new PortrayalRule { Name = "BuoyRule", Type = PortrayalRuleType.Xslt, ExecutionOrder = 1, AppliesTo = ["Buoy"] },
        ],
        xsltRules: new() { ["BuoyRule"] = xslt },
        viewingGroups: viewingGroups);

        var pipeline = new VectorPipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Empty(layer.Instructions);
    }

    [Fact]
    public async Task ProcessAsync_MixedTypes_SortedByPlane_Priority_TypeOrder()
    {
        var source = new FakeFeatureXmlSource(
            featureTypes: ["Buoy", "DepthArea", "DepthContour", "Sounding"],
            featureXml: "<Dataset/>");

        // Emit all four types in a single XSLT (different priorities/planes)
        var xslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <DrawingInstructions>
                  <PointInstruction id="buoy" priority="5" viewingGroup="21010" plane="OverRadar">
                    <Position lat="47.6" lon="-122.3"/><Symbol ref="BOYLAT01"/>
                  </PointInstruction>
                  <AreaInstruction id="area" priority="5" viewingGroup="13010" plane="OverRadar">
                    <Geometry><Point lat="47.5" lon="-122.4"/></Geometry>
                    <AreaFill ref="DEPARE"/>
                  </AreaInstruction>
                  <LineInstruction id="contour" priority="5" viewingGroup="23010" plane="OverRadar">
                    <Geometry><Point lat="47.6" lon="-122.4"/><Point lat="47.5" lon="-122.3"/></Geometry>
                    <LineStyle ref="DEPCNT02"/>
                  </LineInstruction>
                  <TextInstruction id="sounding" priority="5" viewingGroup="33010" plane="OverRadar">
                    <Position lat="47.55" lon="-122.35"/><Text>12.5</Text>
                  </TextInstruction>
                  <PointInstruction id="under_point" priority="3" viewingGroup="21010" plane="UnderRadar">
                    <Position lat="47.6" lon="-122.3"/><Symbol ref="BOYLAT01"/>
                  </PointInstruction>
                </DrawingInstructions>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeVectorPortrayalCatalogue(
        [
            new PortrayalRule { Name = "All", Type = PortrayalRuleType.Xslt, ExecutionOrder = 1, AppliesTo = ["Buoy", "DepthArea", "DepthContour", "Sounding"] },
        ],
        xsltRules: new() { ["All"] = xslt });

        var pipeline = new VectorPipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Equal(5, layer.Instructions.Count);

        // First: UnderRadar, priority 3
        Assert.Equal(DisplayPlane.UnderRadar, layer.Instructions[0].Plane);
        Assert.IsType<PointInstruction>(layer.Instructions[0]);

        // Then OverRadar priority 5, sorted: area(0) < line(1) < point(2) < text(3)
        Assert.IsType<AreaInstruction>(layer.Instructions[1]);
        Assert.IsType<LineInstruction>(layer.Instructions[2]);
        Assert.IsType<PointInstruction>(layer.Instructions[3]);
        Assert.IsType<TextInstruction>(layer.Instructions[4]);
    }

    [Fact]
    public async Task ProcessAsync_NoApplicableRules_ProducesEmptyLayer()
    {
        var source = new FakeFeatureXmlSource(
            featureTypes: ["UnknownType"],
            featureXml: "<Dataset/>");

        var catalogue = new FakeVectorPortrayalCatalogue(
        [
            new PortrayalRule { Name = "BuoyRule", Type = PortrayalRuleType.Xslt, ExecutionOrder = 1, AppliesTo = ["Buoy"] },
        ],
        xsltRules: new());

        var pipeline = new VectorPipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Empty(layer.Instructions);
    }

    [Fact]
    public async Task ProcessAsync_LuaRulesWithoutEngine_Throws()
    {
        var source = new FakeFeatureXmlSource(
            featureTypes: ["Light"],
            featureXml: "<Dataset><Feature id='1' type='Light'/></Dataset>");

        var catalogue = new FakeVectorPortrayalCatalogue(
        [
            new PortrayalRule { Name = "LightRule", Type = PortrayalRuleType.Lua, ExecutionOrder = 1, AppliesTo = ["Light"] },
        ],
        xsltRules: new());

        // No Lua engine provided
        var pipeline = new VectorPipeline();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.ProcessAsync(source, catalogue));
    }

    [Fact]
    public async Task ProcessAsync_MultipleXsltRules_AccumulateInstructions()
    {
        var source = new FakeFeatureXmlSource(
            featureTypes: ["Buoy", "LandArea"],
            featureXml: "<Dataset/>");

        var buoyXslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <DrawingInstructions>
                  <PointInstruction id="buoy1" priority="8" viewingGroup="21010" plane="OverRadar">
                    <Position lat="47.6" lon="-122.3"/><Symbol ref="BOYLAT01"/>
                  </PointInstruction>
                </DrawingInstructions>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var landXslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <DrawingInstructions>
                  <AreaInstruction id="land1" priority="2" viewingGroup="12410" plane="UnderRadar">
                    <Geometry><Point lat="47.5" lon="-122.4"/></Geometry>
                    <AreaFill ref="LANDF"/>
                  </AreaInstruction>
                </DrawingInstructions>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeVectorPortrayalCatalogue(
        [
            new PortrayalRule { Name = "BuoyRule", Type = PortrayalRuleType.Xslt, ExecutionOrder = 1, AppliesTo = ["Buoy"] },
            new PortrayalRule { Name = "LandRule", Type = PortrayalRuleType.Xslt, ExecutionOrder = 2, AppliesTo = ["LandArea"] },
        ],
        xsltRules: new() { ["BuoyRule"] = buoyXslt, ["LandRule"] = landXslt });

        var pipeline = new VectorPipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Equal(2, layer.Instructions.Count);
        // Sorted: UnderRadar area first, then OverRadar point
        Assert.IsType<AreaInstruction>(layer.Instructions[0]);
        Assert.IsType<PointInstruction>(layer.Instructions[1]);
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

    private sealed class FakeFeatureXmlSource : IFeatureXmlSource
    {
        private readonly string _featureXml;

        public FakeFeatureXmlSource(IReadOnlyList<string> featureTypes, string featureXml)
        {
            FeatureTypesPresent = featureTypes;
            _featureXml = featureXml;
        }

        public IReadOnlyList<string> FeatureTypesPresent { get; }

        public XmlReader GetFeatureXml() =>
            XmlReader.Create(new StringReader(_featureXml));
    }

    private sealed class FakeVectorPortrayalCatalogue : IVectorPortrayalCatalogue
    {
        private readonly Dictionary<string, XslCompiledTransform> _xsltRules;

        public FakeVectorPortrayalCatalogue(
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
