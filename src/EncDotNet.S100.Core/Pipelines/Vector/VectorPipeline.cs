using System.Diagnostics;
using System.Xml.Linq;
using System.Xml.Xsl;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Six-stage vector portrayal pipeline (S-100 Part 9):
/// <list type="number">
///   <item>FeatureXML acquisition from <see cref="IFeatureXmlSource"/></item>
///   <item>Rule selection — match dataset feature types to catalogue rules</item>
///   <item>XSLT transformation — run applicable XSLT rules against the FeatureXML</item>
///   <item>Lua execution — delegate to <see cref="ILuaRuleExecutor"/> (Part 9A)</item>
///   <item>Drawing instruction assembly — parse XSLT output into typed objects and append Lua output</item>
///   <item>Viewing group filtering, display plane filtering, and priority sorting</item>
/// </list>
/// </summary>
public class VectorPipeline
{
    private readonly ILuaRuleExecutor? _luaExecutor;

    public VectorPipeline(ILuaRuleExecutor? luaExecutor = null)
    {
        _luaExecutor = luaExecutor;
    }

    public Task<IVectorLayer> ProcessAsync(
        IFeatureXmlSource source,
        IVectorPortrayalCatalogue catalogue,
        Viewport? viewport = null,
        MarinerSettings? mariner = null)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.process");
        activity?.SetTag(TelemetryTags.PipelineStage, "portray");
        var start = Stopwatch.GetTimestamp();
        var stageTag = new KeyValuePair<string, object?>(TelemetryTags.PipelineStage, "vector");

        // GC delta snapshots — process-wide, so there is noise from other
        // threads; useful for orders-of-magnitude comparisons, not precision.
        int gc0Before = GC.CollectionCount(0);
        int gc1Before = GC.CollectionCount(1);
        int gc2Before = GC.CollectionCount(2);

        try
        {
            // Stage 1 — load FeatureXML into a navigable document
            XDocument featureDoc;
            using (Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.feature_xml"))
            {
                var stageStart = Stopwatch.GetTimestamp();
                using (var reader = source.GetFeatureXml())
                {
                    featureDoc = XDocument.Load(reader);
                }
                RecordStageDuration(stageStart, "feature_xml");
            }

            // Stage 2 — select applicable rules
            IReadOnlyList<PortrayalRule> applicableRules;
            using (Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.rule_select"))
            {
                var stageStart = Stopwatch.GetTimestamp();
                var featureTypes = source.FeatureTypesPresent;
                PipelineMetrics.FeaturesIn.Record(featureTypes.Count, stageTag);
                activity?.SetTag("s100.pipeline.feature_types.count", featureTypes.Count);
                applicableRules = SelectRules(featureTypes, catalogue);
                activity?.SetTag("s100.pipeline.rules.count", applicableRules.Count);
                RecordStageDuration(stageStart, "rule_select");
            }

            // Stage 3 — XSLT transformation
            XDocument drawingInstructionsDoc;
            using (Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.xslt"))
            {
                var stageStart = Stopwatch.GetTimestamp();
                drawingInstructionsDoc = RunXsltRules(featureDoc, applicableRules, catalogue, viewport);
                RecordStageDuration(stageStart, "xslt");
            }

            // Stage 5 — assemble typed drawing instructions from the XSLT output
            // using the canonical S-100 Part 9 lower-camel-case display-list reader.
            List<DrawingInstruction> instructions;
            using (Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.assemble"))
            {
                var stageStart = Stopwatch.GetTimestamp();
                instructions = Part9DisplayListReader.Read(drawingInstructionsDoc).ToList();
                RecordStageDuration(stageStart, "assemble");
                PipelineMetrics.StageInstructionsCount.Record(
                    instructions.Count,
                    new KeyValuePair<string, object?>(TelemetryTags.PipelineStage, "assemble"));
            }

            // Stage 4 — Lua execution (S-100 Part 9A). The executor produces typed
            // drawing instructions directly; append them to the XSLT-stage output
            // before viewing-group filtering and priority sorting.
            if (_luaExecutor is not null)
            {
                using (Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.lua"))
                {
                    var stageStart = Stopwatch.GetTimestamp();
                    instructions.AddRange(_luaExecutor.Execute(mariner ?? new MarinerSettings()));
                    RecordStageDuration(stageStart, "lua");
                    PipelineMetrics.StageInstructionsCount.Record(
                        instructions.Count,
                        new KeyValuePair<string, object?>(TelemetryTags.PipelineStage, "lua"));
                }
            }

            // Stage 6 — viewing group filter + display plane filter + priority sort
            IReadOnlyList<DrawingInstruction> sorted;
            using (Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.viewing_groups"))
            {
                var stageStart = Stopwatch.GetTimestamp();
                var filtered = ApplyViewingGroups(instructions, catalogue.ViewingGroups);
                var planeFiltered = ApplyDisplayPlanes(filtered, catalogue.DisplayPlanes);
                RecordStageDuration(stageStart, "viewing_groups");
                PipelineMetrics.StageInstructionsCount.Record(
                    planeFiltered.Count,
                    new KeyValuePair<string, object?>(TelemetryTags.PipelineStage, "viewing_groups"));

                using (Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.sort"))
                {
                    var sortStart = Stopwatch.GetTimestamp();
                    sorted = SortByPriority(planeFiltered);
                    RecordStageDuration(sortStart, "sort");
                    PipelineMetrics.StageInstructionsCount.Record(
                        sorted.Count,
                        new KeyValuePair<string, object?>(TelemetryTags.PipelineStage, "sort"));
                }
            }

            PipelineMetrics.InstructionsOut.Record(sorted.Count, stageTag);
            activity?.SetTag("s100.pipeline.instructions.count", sorted.Count);

            IVectorLayer layer = new DefaultVectorLayer
            {
                Instructions = sorted,
            };

            return Task.FromResult(layer);
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            activity?.SetTag(TelemetryTags.GcGen0Delta, GC.CollectionCount(0) - gc0Before);
            activity?.SetTag(TelemetryTags.GcGen1Delta, GC.CollectionCount(1) - gc1Before);
            activity?.SetTag(TelemetryTags.GcGen2Delta, GC.CollectionCount(2) - gc2Before);

            PipelineMetrics.Duration.Record(
                (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency,
                stageTag);
        }
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
        Viewport? viewport)
    {
        var drawingInstructions = new XDocument(
            new XElement("DrawingInstructions"));

        foreach (var rule in rules.Where(r => r.Type == PortrayalRuleType.Xslt))
        {
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
            System.Xml.XmlConvert.VerifyNCName(name);
            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    // ── Stage 6: Display plane filtering ──────────────────────────────

    /// <summary>
    /// Removes instructions whose <see cref="DrawingInstruction.Plane"/>
    /// is hidden by the controller (S-100 Part 9 §11.6). Runs after
    /// viewing-group filtering so the input list is already reduced.
    /// </summary>
    private static IReadOnlyList<DrawingInstruction> ApplyDisplayPlanes(
        IReadOnlyList<DrawingInstruction> instructions,
        DisplayPlaneController displayPlanes)
    {
        if (displayPlanes.HiddenPlanes.Count == 0) return instructions;
        return instructions
            .Where(i => displayPlanes.IsVisible(i.Plane))
            .ToList();
    }

    // ── Stage 6: Viewing group filtering and sort ──────────────────────

    private static IReadOnlyList<DrawingInstruction> ApplyViewingGroups(
        IReadOnlyList<DrawingInstruction> instructions,
        ViewingGroupController viewingGroups)
    {
        return instructions
            .Where(i => viewingGroups.IsVisible(i.ViewingGroup))
            .ToList();
    }

    private static IReadOnlyList<DrawingInstruction> SortByPriority(
        IReadOnlyList<DrawingInstruction> instructions)
    {
        // S-100 Part 9 sort order:
        // 1. DisplayPlane (UnderRadar before OverRadar)
        // 2. DrawingPriority (ascending)
        // 3. Type: areas (0) → lines (1) → points (2) → text (3)
        return instructions
            .OrderBy(i => i.Plane)
            .ThenBy(i => i.DrawingPriority)
            .ThenBy(i => i.TypeSortOrder)
            .ToList();
    }
}

/// <summary>
/// A styled vector layer carrying ordered drawing instructions produced
/// by portrayal rule evaluation, ready for rendering.
/// </summary>
public interface IVectorLayer : IPortrayalLayer
{
    /// <summary>Drawing instructions in back-to-front render order.</summary>
    IReadOnlyList<DrawingInstruction> Instructions { get; }
}

/// <summary>
/// The display plane a feature is drawn on (S-52/S-100 portrayal model).
/// </summary>
public enum DisplayPlane
{
    UnderRadar = 0,
    OverRadar = 1,
}

internal sealed class DefaultVectorLayer : IVectorLayer
{
    public required IReadOnlyList<DrawingInstruction> Instructions { get; init; }
}
