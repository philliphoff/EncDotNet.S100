using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Xml;
using System.Xml.Xsl;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Smoke tests for the observability instrumentation added across
/// the libraries. We don't assert exact span trees (those depend on
/// runtime data); we just verify that the named ActivitySources are
/// reachable by an <see cref="ActivityListener"/> and that a happy-path
/// call produces at least one activity with the expected name.
/// </summary>
public sealed class TelemetrySmokeTests
{
    [Fact]
    public void FeatureCatalogueReader_emits_parse_activity()
    {
        var observed = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "EncDotNet.S100.Features",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => observed.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        using var fcStream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new System.InvalidOperationException("S-101 feature catalogue not found.");
        _ = FeatureCatalogueReader.Read(fcStream);

        Assert.Contains(observed, a => a.OperationName == "s100.featurecatalogue.parse");
    }

    [Fact]
    public void All_libraries_register_named_activity_sources()
    {
        // Touch each library's Telemetry indirectly via a public type
        // to ensure the assembly is loaded before we ask the listener.
        _ = typeof(EncDotNet.S100.Diagnostics.S100Telemetry);
        _ = typeof(EncDotNet.S100.Features.FeatureCatalogueReader);
        _ = typeof(EncDotNet.S100.Specifications.Specification);

        var seen = new HashSet<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src =>
            {
                if (src.Name.StartsWith("EncDotNet.S100", System.StringComparison.Ordinal))
                {
                    seen.Add(src.Name);
                }
                return false;
            },
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.None,
        };
        ActivitySource.AddActivityListener(listener);

        // Force a parse so at least the Features ActivitySource is queried.
        using var fcStream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new System.InvalidOperationException("S-101 feature catalogue not found.");
        _ = FeatureCatalogueReader.Read(fcStream);

        Assert.Contains("EncDotNet.S100.Features", seen);
    }

    [Fact]
    public async Task VectorPipeline_emits_stage_spans()
    {
        var observed = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "EncDotNet.S100.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => observed.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var source = new FakeFeatureXmlSource(
            featureTypes: ["Buoy"],
            featureXml: "<Dataset><Feature id='1' type='Buoy'/></Dataset>");

        var xslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/"><displayList/></xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeVectorPortrayalCatalogue(
            [new PortrayalRule { Name = "R", Type = PortrayalRuleType.Xslt, ExecutionOrder = 1, AppliesTo = ["Buoy"] }],
            xsltRules: new() { ["R"] = xslt });

        await new VectorPipeline().ProcessAsync(source, catalogue);

        Assert.Contains(observed, a => a.OperationName == "s100.pipeline.vector.process");
        Assert.Contains(observed, a => a.OperationName == "s100.pipeline.vector.stage.feature_xml");
        Assert.Contains(observed, a => a.OperationName == "s100.pipeline.vector.stage.rule_select");
        Assert.Contains(observed, a => a.OperationName == "s100.pipeline.vector.stage.xslt");
        Assert.Contains(observed, a => a.OperationName == "s100.pipeline.vector.stage.assemble");
        Assert.Contains(observed, a => a.OperationName == "s100.pipeline.vector.stage.viewing_groups");
        Assert.Contains(observed, a => a.OperationName == "s100.pipeline.vector.stage.sort");
    }

    [Fact]
    public async Task VectorPipeline_tags_gc_deltas()
    {
        var observed = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "EncDotNet.S100.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => observed.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var source = new FakeFeatureXmlSource(
            featureTypes: ["Buoy"],
            featureXml: "<Dataset><Feature id='1' type='Buoy'/></Dataset>");

        var xslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/"><displayList/></xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeVectorPortrayalCatalogue(
            [new PortrayalRule { Name = "R", Type = PortrayalRuleType.Xslt, ExecutionOrder = 1, AppliesTo = ["Buoy"] }],
            xsltRules: new() { ["R"] = xslt });

        await new VectorPipeline().ProcessAsync(source, catalogue);

        var pipelineSpan = observed.First(a => a.OperationName == "s100.pipeline.vector.process");
        Assert.NotNull(pipelineSpan.GetTagItem("gc.gen0.delta"));
        Assert.NotNull(pipelineSpan.GetTagItem("gc.gen1.delta"));
        Assert.NotNull(pipelineSpan.GetTagItem("gc.gen2.delta"));
    }

    [Fact]
    public async Task VectorPipeline_emits_xslt_transform_span()
    {
        var observed = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "EncDotNet.S100.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => observed.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var source = new FakeFeatureXmlSource(
            featureTypes: ["Buoy"],
            featureXml: "<Dataset><Feature id='1' type='Buoy'/></Dataset>");

        var xslt = CompileXslt("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/"><displayList/></xsl:template>
            </xsl:stylesheet>
            """);

        var catalogue = new FakeVectorPortrayalCatalogue(
            [new PortrayalRule { Name = "TestRule", Type = PortrayalRuleType.Xslt, ExecutionOrder = 1, AppliesTo = ["Buoy"] }],
            xsltRules: new() { ["TestRule"] = xslt });

        await new VectorPipeline().ProcessAsync(source, catalogue);

        var transformSpan = observed.First(a => a.OperationName == "s100.xslt.transform");
        Assert.Equal("TestRule", transformSpan.GetTagItem("s100.xslt.rule"));
    }

    #region Helpers

    private static XslCompiledTransform CompileXslt(string xslt)
    {
        var transform = new XslCompiledTransform();
        using var reader = XmlReader.Create(new System.IO.StringReader(xslt));
        transform.Load(reader);
        return transform;
    }

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
            XmlReader.Create(new System.IO.StringReader(_featureXml));
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
