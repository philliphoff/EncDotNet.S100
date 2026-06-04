using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Pipelines.Vector.Xslt;

/// <summary>
/// Executes the S-100 Part 9 §9.4 XSLT portrayal engine and returns typed
/// drawing instructions. This is the XSLT-engine sibling of the Lua
/// <see cref="Lua.LuaRuleExecutor"/> under the shared
/// <see cref="IVectorRuleExecutor"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// The executor performs four sub-stages, each of which previously lived
/// inline in <see cref="VectorPipeline"/>:
/// <list type="number">
///   <item>FeatureXML acquisition from <see cref="IFeatureXmlSource"/>.</item>
///   <item>Rule selection — match dataset feature types to catalogue rules.</item>
///   <item>XSLT transformation — run each applicable XSLT rule against the FeatureXML.</item>
///   <item>Drawing-instruction assembly — parse the XSLT output into typed
///     objects via <see cref="Part9DisplayListReader"/>.</item>
/// </list>
/// </para>
/// <para>
/// Instances are <b>render-bound</b>: the FeatureXML source, catalogue, and
/// viewport are captured at construction and read at <see cref="Execute"/>
/// time. Do not cache or reuse an instance across viewport, source, or
/// catalogue changes; construct a fresh executor per render instead (this is
/// cheap — the compiled XSLT transforms are cached in the catalogue, not the
/// executor). The <see cref="MarinerSettings"/> argument to
/// <see cref="Execute"/> is ignored because the XSLT engine is driven only by
/// the colour palette and viewport scale, not mariner preferences.
/// </para>
/// </remarks>
public sealed class XsltRuleExecutor : IVectorRuleExecutor
{
    private readonly IFeatureXmlSource _source;
    private readonly IVectorPortrayalCatalogue _catalogue;
    private readonly Viewport? _viewport;

    /// <summary>Initialises a new render-bound XSLT rule executor.</summary>
    /// <param name="source">Supplies the S-100 Part 9 FeatureXML to transform.</param>
    /// <param name="catalogue">Supplies the portrayal rules, compiled XSLT transforms, and active colour palette.</param>
    /// <param name="viewport">Optional viewport; when supplied its scale denominator is passed to each rule as the <c>displayScale</c> XSLT parameter.</param>
    public XsltRuleExecutor(
        IFeatureXmlSource source,
        IVectorPortrayalCatalogue catalogue,
        Viewport? viewport = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogue);
        _source = source;
        _catalogue = catalogue;
        _viewport = viewport;
    }

    /// <summary>
    /// The number of distinct dataset feature types observed during the most
    /// recent <see cref="Execute"/> call, for process-level telemetry.
    /// </summary>
    public int LastFeatureTypeCount { get; private set; }

    /// <summary>
    /// The number of applicable catalogue rules (XSLT and Lua) selected during
    /// the most recent <see cref="Execute"/> call, for process-level telemetry.
    /// Matches the legacy <c>rules.count</c> dimension: this is the count of
    /// rules whose feature-type predicate matched, not the count of XSLT rules
    /// actually transformed.
    /// </summary>
    public int LastRuleCount { get; private set; }

    /// <inheritdoc />
    /// <remarks>The <paramref name="mariner"/> argument is ignored; see the type remarks.</remarks>
    public IReadOnlyList<DrawingInstruction> Execute(MarinerSettings mariner, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stageTag = new KeyValuePair<string, object?>(TelemetryTags.PipelineStage, "vector");

        // Stage 1 — load FeatureXML into a navigable document
        XDocument featureDoc;
        using (Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.feature_xml"))
        {
            var stageStart = Stopwatch.GetTimestamp();
            using (var reader = _source.GetFeatureXml(cancellationToken))
            {
                featureDoc = XDocument.Load(reader);
            }
            RecordStageDuration(stageStart, "feature_xml");
        }

        // Stage 2 — select applicable rules
        IReadOnlyList<PortrayalRule> applicableRules;
        using (var ruleSelectActivity = Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.rule_select"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stageStart = Stopwatch.GetTimestamp();
            var featureTypes = _source.FeatureTypesPresent;
            PipelineMetrics.FeaturesIn.Record(featureTypes.Count, stageTag);
            LastFeatureTypeCount = featureTypes.Count;
            ruleSelectActivity?.SetTag("s100.pipeline.feature_types.count", featureTypes.Count);
            applicableRules = SelectRules(featureTypes, _catalogue);
            LastRuleCount = applicableRules.Count;
            ruleSelectActivity?.SetTag("s100.pipeline.rules.count", applicableRules.Count);
            RecordStageDuration(stageStart, "rule_select");
        }

        // Stage 3 — XSLT transformation
        XDocument drawingInstructionsDoc;
        using (Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.xslt"))
        {
            var stageStart = Stopwatch.GetTimestamp();
            drawingInstructionsDoc = RunXsltRules(featureDoc, applicableRules, _catalogue, _viewport, cancellationToken);
            RecordStageDuration(stageStart, "xslt");
        }

        // Stage 5 — assemble typed drawing instructions from the XSLT output
        // using the canonical S-100 Part 9 lower-camel-case display-list reader.
        List<DrawingInstruction> instructions;
        using (Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.assemble"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stageStart = Stopwatch.GetTimestamp();
            instructions = Part9DisplayListReader.Read(drawingInstructionsDoc).ToList();
            RecordStageDuration(stageStart, "assemble");
            PipelineMetrics.StageInstructionsCount.Record(
                instructions.Count,
                new KeyValuePair<string, object?>(TelemetryTags.PipelineStage, "assemble"));
        }

        return instructions;
    }

    private static void RecordStageDuration(long stageStart, string stageName)
    {
        PipelineMetrics.StageDuration.Record(
            (Stopwatch.GetTimestamp() - stageStart) * 1000.0 / Stopwatch.Frequency,
            new KeyValuePair<string, object?>(TelemetryTags.PipelineStage, stageName));
    }

    // ── Stage 2: Rule selection ─────────────────────────────────────────

    private static IReadOnlyList<PortrayalRule> SelectRules(
        IReadOnlyList<string> featureTypesPresent,
        IVectorPortrayalCatalogue catalogue)
    {
        var featureTypeSet = new HashSet<string>(featureTypesPresent, StringComparer.OrdinalIgnoreCase);

        return catalogue.Rules
            .Where(r => r.AlwaysApply || r.AppliesTo.Any(t => featureTypeSet.Contains(t)))
            .OrderBy(r => r.ExecutionOrder)
            .ToList();
    }

    // ── Stage 3: XSLT transformation ───────────────────────────────────

    private static XDocument RunXsltRules(
        XDocument featureDoc,
        IReadOnlyList<PortrayalRule> rules,
        IVectorPortrayalCatalogue catalogue,
        Viewport? viewport,
        CancellationToken cancellationToken)
    {
        var drawingInstructions = new XDocument(
            new XElement("DrawingInstructions"));

        foreach (var rule in rules.Where(r => r.Type == PortrayalRuleType.Xslt))
        {
            // Cancellation is honoured between rules. A single
            // XslCompiledTransform.Transform call is not interruptible, so a
            // very large/slow individual rule cannot be abandoned mid-flight.
            cancellationToken.ThrowIfCancellationRequested();

            var args = new XsltArgumentList();

            // Pass colour palette tokens as XSLT parameters
            foreach (var (token, color) in catalogue.ActivePalette.Colors)
            {
                // Some product specs (e.g. S-122) include colour tokens whose
                // names are not valid XML NCNames (e.g. "00011"). XSLT
                // parameter names must be NCNames, so skip any that aren't —
                // the XSLT cannot reference them by name in any case.
                if (!IsValidNCName(token))
                {
                    continue;
                }

                args.AddParam(token, string.Empty, color);
            }

            // Pass display scale if a viewport is available
            if (viewport is not null)
            {
                args.AddParam("displayScale", string.Empty, viewport.ScaleDenominator);
            }

            var transform = catalogue.GetCompiledRule(rule.Name);
            var resultFragment = new XDocument();

            var transformStart = Stopwatch.GetTimestamp();
            using (var transformActivity = Telemetry.ActivitySource.StartActivity("s100.xslt.transform"))
            {
                transformActivity?.SetTag(TelemetryTags.XsltRule, rule.Name);

                using (var inputReader = featureDoc.CreateReader())
                using (var writer = resultFragment.CreateWriter())
                {
                    transform.Transform(inputReader, args, writer);
                }
            }
            PipelineMetrics.XsltTransformDuration.Record(
                (Stopwatch.GetTimestamp() - transformStart) * 1000.0 / Stopwatch.Frequency,
                new KeyValuePair<string, object?>(TelemetryTags.XsltRule, rule.Name));

            // Accumulate results — each rule emits instruction elements
            if (resultFragment.Root is not null)
            {
                drawingInstructions.Root!.Add(resultFragment.Root.Elements());
            }
        }

        return drawingInstructions;
    }

    private static bool IsValidNCName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        try
        {
            XmlConvert.VerifyNCName(name);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
