using System.Xml;
using System.Xml.Xsl;
using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Pipelines.Vector.Xslt;

namespace EncDotNet.S100.Pipelines.Tests;

public class XsltRuleExecutorTests
{
    [Fact]
    public void Execute_XsltPointRule_ReturnsTypedInstruction()
    {
        var source = new FakeFeatureXmlSource(
            ["Buoy"],
            """
            <Dataset>
              <Feature id="1" type="Buoy"><Position lat="47.6" lon="-122.3"/></Feature>
            </Dataset>
            """);

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
            [new PortrayalRule { Name = "BuoyRule", Type = PortrayalRuleType.Xslt, ExecutionOrder = 1, AppliesTo = ["Buoy"] }],
            xsltRules: new() { ["BuoyRule"] = xslt });

        var executor = new XsltRuleExecutor(source, catalogue);

        // The mariner argument is ignored by the XSLT engine; pass an explicit
        // non-default value to confirm it has no effect.
        var instructions = executor.ExecuteAsync(new MarinerSettings { FourShades = true }).GetAwaiter().GetResult();

        var inst = Assert.IsType<PointInstruction>(Assert.Single(instructions));
        Assert.Equal("1", inst.FeatureReference);
    }

    [Fact]
    public void Execute_ExposesFeatureTypeAndRuleCountsForTelemetry()
    {
        var source = new FakeFeatureXmlSource(
            ["Buoy", "Light"],
            "<Dataset><Feature id='1' type='Buoy'/></Dataset>");

        var xslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/"><displayList/></xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeVectorPortrayalCatalogue(
            [
                new PortrayalRule { Name = "BuoyRule", Type = PortrayalRuleType.Xslt, ExecutionOrder = 1, AppliesTo = ["Buoy"] },
                new PortrayalRule { Name = "FogRule", Type = PortrayalRuleType.Xslt, ExecutionOrder = 2, AppliesTo = ["Fog"] },
            ],
            xsltRules: new() { ["BuoyRule"] = xslt });

        var executor = new XsltRuleExecutor(source, catalogue);
        executor.ExecuteAsync(MarinerSettings.Default).GetAwaiter().GetResult();

        Assert.Equal(2, executor.LastFeatureTypeCount);

        // Only the Buoy rule's predicate matches the present feature types; the
        // Fog rule does not. This is the legacy "applicable rules" count.
        Assert.Equal(1, executor.LastRuleCount);
    }

    [Fact]
    public void Execute_PreCancelledToken_Throws()
    {
        var source = new FakeFeatureXmlSource(["Buoy"], "<Dataset/>");
        var catalogue = new FakeVectorPortrayalCatalogue([]);
        var executor = new XsltRuleExecutor(source, catalogue);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => executor.ExecuteAsync(MarinerSettings.Default, cts.Token).GetAwaiter().GetResult());
    }

    private static XslCompiledTransform CompileXslt(string xslt)
    {
        var transform = new XslCompiledTransform();
        using var reader = XmlReader.Create(new StringReader(xslt));
        transform.Load(reader);
        return transform;
    }

    private sealed class FakeFeatureXmlSource(IReadOnlyList<string> featureTypes, string featureXml) : IFeatureXmlSource
    {
        public IReadOnlyList<string> FeatureTypesPresent { get; } = featureTypes;

        public XmlReader GetFeatureXml(CancellationToken cancellationToken = default) =>
            XmlReader.Create(new StringReader(featureXml));
    }

    private sealed class FakeVectorPortrayalCatalogue(
        IReadOnlyList<PortrayalRule> rules,
        Dictionary<string, XslCompiledTransform>? xsltRules = null) : IVectorPortrayalCatalogue
    {
        private readonly Dictionary<string, XslCompiledTransform> _xsltRules = xsltRules ?? new();

        public SpecRef Spec => new("S-101", default);
        public string Edition => "1.2.0";
        public ColorPalette ActivePalette => ColorPalette.Default;
        public ValueTask SwitchPaletteAsync(PaletteType type, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public IReadOnlyList<PortrayalRule> Rules { get; } = rules;
        public ViewingGroupController ViewingGroups { get; } = new();
        public DisplayModeController DisplayModes { get; } = new();
        public DisplayPlaneController DisplayPlanes { get; } = new();

        public ValueTask<XslCompiledTransform> GetCompiledRuleAsync(string ruleName, CancellationToken cancellationToken = default) =>
            new(_xsltRules.TryGetValue(ruleName, out var t) ? t : throw new KeyNotFoundException(ruleName));

        public ValueTask<IReadOnlyList<string>> GetLuaSourceNamesAsync(CancellationToken cancellationToken = default) =>
            new(Array.Empty<string>());
        public ValueTask<string?> GetLuaSourceAsync(string fileName, CancellationToken cancellationToken = default) =>
            new((string?)null);
        public IReadOnlyList<LuaContextParameter> ContextParameters => [];

        public ValueTask<SvgSymbol> GetSymbolAsync(string symbolName, CancellationToken cancellationToken = default) =>
            new(new SvgSymbol { Name = symbolName, SvgContent = $"<svg id=\"{symbolName}\"/>" });

        public ValueTask<LineStyle> GetLineStyleAsync(string name, CancellationToken cancellationToken = default) =>
            new(new LineStyle { Name = name, Width = 1.0f, Color = "#000000" });

        public ValueTask<AreaFill> GetAreaFillAsync(string name, CancellationToken cancellationToken = default) =>
            new(new AreaFill { Name = name, Color = "#C8C8C8" });
    }
}
