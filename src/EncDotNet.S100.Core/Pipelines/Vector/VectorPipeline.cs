using System.Diagnostics;
using EncDotNet.S100.Diagnostics;
using EncDotNet.S100.Pipelines.Vector.Xslt;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Vector portrayal pipeline (S-100 Part 9). The pipeline composes one or
/// more <see cref="IVectorRuleExecutor"/> engines, concatenates their typed
/// drawing instructions, and applies the shared post-processing stage:
/// <list type="number">
///   <item>XSLT rule execution — a built-in <see cref="XsltRuleExecutor"/>
///     (Part 9 §9.4) handles FeatureXML acquisition, rule selection,
///     transformation, and display-list assembly.</item>
///   <item>Lua rule execution — an optional injected
///     <see cref="IVectorRuleExecutor"/> (Part 9A).</item>
///   <item>Viewing-group filtering, display-plane filtering, and priority
///     sorting of the merged instruction list.</item>
/// </list>
/// </summary>
/// <remarks>
/// The two engines are honest siblings under <see cref="IVectorRuleExecutor"/>:
/// the XSLT executor is built in (it is the default vector engine), while the
/// Lua executor is supplied per product. Executor output is merged in
/// construction order — XSLT instructions first, then Lua — and then re-sorted
/// by <c>(Plane, DrawingPriority, TypeSortOrder)</c>. The sort is stable, so
/// that construction order is the tie-breaker for instructions that share an
/// identical sort key.
/// </remarks>
public class VectorPipeline
{
    private readonly IVectorRuleExecutor? _luaExecutor;

    public VectorPipeline(IVectorRuleExecutor? luaExecutor = null)
    {
        _luaExecutor = luaExecutor;
    }

    public async Task<IVectorLayer> ProcessAsync(
        IFeatureXmlSource source,
        IVectorPortrayalCatalogue catalogue,
        Viewport? viewport = null,
        MarinerSettings? mariner = null,
        CancellationToken cancellationToken = default)
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
            cancellationToken.ThrowIfCancellationRequested();
            var marinerSettings = mariner ?? new MarinerSettings();

            // XSLT rule execution (S-100 Part 9 §9.4). The built-in executor
            // owns FeatureXML acquisition, rule selection, transformation, and
            // display-list assembly; its internal telemetry spans cover those
            // sub-stages. It is render-bound, so it is constructed per call.
            var xsltExecutor = new XsltRuleExecutor(source, catalogue, viewport);
            var instructions = (await xsltExecutor.ExecuteAsync(marinerSettings, cancellationToken).ConfigureAwait(false)).ToList();
            activity?.SetTag("s100.pipeline.feature_types.count", xsltExecutor.LastFeatureTypeCount);
            activity?.SetTag("s100.pipeline.rules.count", xsltExecutor.LastRuleCount);

            // Lua rule execution (S-100 Part 9A). The optional injected executor
            // produces typed drawing instructions directly; append them to the
            // XSLT output before viewing-group filtering and priority sorting.
            if (_luaExecutor is not null)
            {
                using (Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.lua"))
                {
                    var stageStart = Stopwatch.GetTimestamp();
                    instructions.AddRange(await _luaExecutor.ExecuteAsync(marinerSettings, cancellationToken).ConfigureAwait(false));
                    RecordStageDuration(stageStart, "lua");
                    PipelineMetrics.StageInstructionsCount.Record(
                        instructions.Count,
                        new KeyValuePair<string, object?>(TelemetryTags.PipelineStage, "lua"));
                }
            }

            // Post-processing — viewing group filter + display plane filter + priority sort
            IReadOnlyList<DrawingInstruction> sorted;
            using (Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.viewing_groups"))
            {
                cancellationToken.ThrowIfCancellationRequested();
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

            return layer;
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

    // ── Post-processing: Display plane filtering ──────────────────────

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

    // ── Post-processing: Viewing group filtering and sort ──────────────

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
/// <remarks>
/// The numeric values are a persisted contract: they are written by ordinal
/// into the disk-backed portrayal cache
/// (<see cref="Caching.DrawingInstructionSerializer"/>). Do not renumber or
/// reorder existing members.
/// </remarks>
public enum DisplayPlane
{
    UnderRadar = 0,
    OverRadar = 1,
}

internal sealed class DefaultVectorLayer : IVectorLayer
{
    public required IReadOnlyList<DrawingInstruction> Instructions { get; init; }
}
