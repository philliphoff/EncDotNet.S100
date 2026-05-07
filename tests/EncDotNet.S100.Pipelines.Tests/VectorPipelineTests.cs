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

        // XSLT rule that emits a pointInstruction for each Buoy feature
        var xslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <displayList>
                  <xsl:for-each select="//Feature[@type='Buoy']">
                    <pointInstruction>
                      <featureReference><xsl:value-of select="@id"/></featureReference>
                      <drawingPriority>8</drawingPriority>
                      <viewingGroup>21010</viewingGroup>
                      <displayPlane>OverRadar</displayPlane>
                      <symbol reference="BOYLAT01"><rotation>0</rotation></symbol>
                    </pointInstruction>
                  </xsl:for-each>
                </displayList>
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
        Assert.Equal("1", inst.FeatureReference);
        Assert.Equal(DisplayPlane.OverRadar, inst.Plane);
        Assert.Equal("BOYLAT01", inst.SymbolReference);
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
                <displayList>
                  <lineInstruction>
                    <featureReference>2</featureReference>
                    <drawingPriority>4</drawingPriority>
                    <viewingGroup>23010</viewingGroup>
                    <displayPlane>UnderRadar</displayPlane>
                    <lineStyleReference reference="DEPCNT02"/>
                  </lineInstruction>
                </displayList>
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
        Assert.Equal("2", inst.FeatureReference);
        Assert.Equal(DisplayPlane.UnderRadar, inst.Plane);
        Assert.Equal("DEPCNT02", inst.LineStyleReference);
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
                <displayList>
                  <areaInstruction>
                    <featureReference>3</featureReference>
                    <drawingPriority>2</drawingPriority>
                    <viewingGroup>12410</viewingGroup>
                    <displayPlane>UnderRadar</displayPlane>
                    <areaFillReference reference="LANDF"/>
                  </areaInstruction>
                </displayList>
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
        Assert.Equal("3", inst.FeatureReference);
        Assert.Equal("LANDF", inst.AreaFillReference);
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
                <displayList>
                  <textInstruction>
                    <featureReference>4</featureReference>
                    <drawingPriority>9</drawingPriority>
                    <viewingGroup>33010</viewingGroup>
                    <displayPlane>OverRadar</displayPlane>
                    <text>12.5</text>
                    <font><size>12</size></font>
                  </textInstruction>
                </displayList>
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
        Assert.Equal("4", inst.FeatureReference);
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
                <displayList>
                  <pointInstruction>
                    <featureReference>SHOULD_NOT_APPEAR</featureReference>
                    <drawingPriority>1</drawingPriority>
                    <viewingGroup>1</viewingGroup>
                    <symbol reference="BOYLAT01"/>
                  </pointInstruction>
                </displayList>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var landXslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <displayList>
                  <areaInstruction>
                    <featureReference>1</featureReference>
                    <drawingPriority>2</drawingPriority>
                    <viewingGroup>12410</viewingGroup>
                    <displayPlane>UnderRadar</displayPlane>
                    <areaFillReference reference="LANDF"/>
                  </areaInstruction>
                </displayList>
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
        Assert.Equal("1", layer.Instructions[0].FeatureReference);
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
                <displayList>
                  <pointInstruction>
                    <featureReference>meta</featureReference>
                    <drawingPriority>0</drawingPriority>
                    <viewingGroup>10000</viewingGroup>
                    <displayPlane>UnderRadar</displayPlane>
                    <symbol reference="META01"/>
                  </pointInstruction>
                </displayList>
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
        Assert.Equal("meta", layer.Instructions[0].FeatureReference);
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
                <displayList>
                  <pointInstruction>
                    <featureReference>buoy</featureReference>
                    <drawingPriority>5</drawingPriority>
                    <viewingGroup>21010</viewingGroup>
                    <displayPlane>OverRadar</displayPlane>
                    <symbol reference="BOYLAT01"/>
                  </pointInstruction>
                  <areaInstruction>
                    <featureReference>area</featureReference>
                    <drawingPriority>5</drawingPriority>
                    <viewingGroup>13010</viewingGroup>
                    <displayPlane>OverRadar</displayPlane>
                    <areaFillReference reference="DEPARE"/>
                  </areaInstruction>
                  <lineInstruction>
                    <featureReference>contour</featureReference>
                    <drawingPriority>5</drawingPriority>
                    <viewingGroup>23010</viewingGroup>
                    <displayPlane>OverRadar</displayPlane>
                    <lineStyleReference reference="DEPCNT02"/>
                  </lineInstruction>
                  <textInstruction>
                    <featureReference>sounding</featureReference>
                    <drawingPriority>5</drawingPriority>
                    <viewingGroup>33010</viewingGroup>
                    <displayPlane>OverRadar</displayPlane>
                    <text>12.5</text>
                  </textInstruction>
                  <pointInstruction>
                    <featureReference>under_point</featureReference>
                    <drawingPriority>3</drawingPriority>
                    <viewingGroup>21010</viewingGroup>
                    <displayPlane>UnderRadar</displayPlane>
                    <symbol reference="BOYLAT01"/>
                  </pointInstruction>
                </displayList>
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
    public async Task ProcessAsync_MultipleXsltRules_AccumulateInstructions()
    {
        var source = new FakeFeatureXmlSource(
            featureTypes: ["Buoy", "LandArea"],
            featureXml: "<Dataset/>");

        var buoyXslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <displayList>
                  <pointInstruction>
                    <featureReference>buoy1</featureReference>
                    <drawingPriority>8</drawingPriority>
                    <viewingGroup>21010</viewingGroup>
                    <displayPlane>OverRadar</displayPlane>
                    <symbol reference="BOYLAT01"/>
                  </pointInstruction>
                </displayList>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var landXslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <displayList>
                  <areaInstruction>
                    <featureReference>land1</featureReference>
                    <drawingPriority>2</drawingPriority>
                    <viewingGroup>12410</viewingGroup>
                    <displayPlane>UnderRadar</displayPlane>
                    <areaFillReference reference="LANDF"/>
                  </areaInstruction>
                </displayList>
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

        public DisplayModeController DisplayModes { get; } = new();

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
