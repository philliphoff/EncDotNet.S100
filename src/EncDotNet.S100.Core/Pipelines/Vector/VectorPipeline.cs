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

    /// <summary>Creates a vector pipeline.</summary>
    /// <param name="luaExecutor">
    /// Optional Lua rule executor (S-100 Part 9A) whose instructions are
    /// appended after the built-in XSLT executor's. When
    /// <see langword="null"/> only XSLT rules run.
    /// </param>
    public VectorPipeline(IVectorRuleExecutor? luaExecutor = null)
    {
        _luaExecutor = luaExecutor;
    }

    /// <summary>
    /// Runs the portrayal rules for <paramref name="source"/> and returns the
    /// filtered, priority-sorted display list.
    /// </summary>
    /// <param name="source">Supplies the dataset's FeatureXML to the XSLT rules.</param>
    /// <param name="catalogue">
    /// The portrayal catalogue supplying the rules, and whose
    /// <see cref="IVectorPortrayalCatalogue.ViewingGroups"/> and
    /// <see cref="IVectorPortrayalCatalogue.DisplayPlanes"/> controllers
    /// filter the output.
    /// </param>
    /// <param name="viewport">
    /// Optional display area; when supplied its
    /// <see cref="Viewport.ScaleDenominator"/> is passed to the XSLT rules as
    /// the <c>displayScale</c> parameter.
    /// </param>
    /// <param name="mariner">
    /// Mariner display preferences passed to the rule executors; when
    /// <see langword="null"/> a default <see cref="MarinerSettings"/> is used.
    /// </param>
    /// <param name="cancellationToken">Signals that the render has been cancelled.</param>
    /// <returns>
    /// A layer whose <see cref="IVectorLayer.Instructions"/> are the merged
    /// XSLT and Lua instructions, filtered by viewing group and display plane
    /// and sorted by display plane, drawing priority and instruction type.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="catalogue"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
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
            var sorted = await ExecuteRulesCoreAsync(source, catalogue, viewport, mariner, activity, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var filtered = ApplyDisplayFilters(sorted, catalogue);

            PipelineMetrics.InstructionsOut.Record(filtered.Count, stageTag);
            activity?.SetTag("s100.pipeline.instructions.count", filtered.Count);

            IVectorLayer layer = new DefaultVectorLayer
            {
                Instructions = filtered,
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

    /// <summary>
    /// Runs the portrayal rules for <paramref name="source"/> and returns the
    /// priority-sorted display list <em>before</em> viewing-group and
    /// display-plane filtering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Filtering is a pure function of the rule output and the catalogue's
    /// <see cref="IVectorPortrayalCatalogue.ViewingGroups"/> and
    /// <see cref="IVectorPortrayalCatalogue.DisplayPlanes"/> state, so a host
    /// can keep this list and pass it to <see cref="ApplyDisplayFilters"/>
    /// when only the ECDIS display state changes (display category, viewing
    /// groups, display planes) instead of running the rules again.
    /// <c>ApplyDisplayFilters(await ExecuteRulesAsync(…), catalogue)</c>
    /// equals the instructions <see cref="ProcessAsync"/> returns.
    /// </para>
    /// <para>
    /// The XSLT rules receive the active display mode as a parameter, so when
    /// <see cref="RuleOutputDependsOnDisplayMode"/> is <see langword="true"/>
    /// the list is only reusable under the same display mode.
    /// </para>
    /// </remarks>
    /// <param name="source">Supplies the dataset's FeatureXML to the XSLT rules.</param>
    /// <param name="catalogue">The portrayal catalogue supplying the rules.</param>
    /// <param name="viewport">
    /// Optional display area; when supplied its
    /// <see cref="Viewport.ScaleDenominator"/> is passed to the XSLT rules as
    /// the <c>displayScale</c> parameter.
    /// </param>
    /// <param name="mariner">
    /// Mariner display preferences passed to the rule executors; when
    /// <see langword="null"/> a default <see cref="MarinerSettings"/> is used.
    /// </param>
    /// <param name="cancellationToken">Signals that the render has been cancelled.</param>
    /// <returns>
    /// The merged XSLT and Lua instructions, sorted by display plane, drawing
    /// priority and instruction type, with nothing filtered out.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="catalogue"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    public async Task<IReadOnlyList<DrawingInstruction>> ExecuteRulesAsync(
        IFeatureXmlSource source,
        IVectorPortrayalCatalogue catalogue,
        Viewport? viewport = null,
        MarinerSettings? mariner = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogue);
        using var activity = Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.execute_rules");
        activity?.SetTag(TelemetryTags.PipelineStage, "portray");
        return await ExecuteRulesCoreAsync(source, catalogue, viewport, mariner, activity, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the catalogue's current viewing-group and display-plane state
    /// to a display list from <see cref="ExecuteRulesAsync"/>, keeping its
    /// order.
    /// </summary>
    /// <param name="instructions">The unfiltered, priority-sorted rule output.</param>
    /// <param name="catalogue">
    /// The catalogue whose <see cref="IVectorPortrayalCatalogue.ViewingGroups"/>
    /// and <see cref="IVectorPortrayalCatalogue.DisplayPlanes"/> decide what is shown.
    /// </param>
    /// <returns>The visible instructions, in their original order.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="instructions"/> or <paramref name="catalogue"/> is <see langword="null"/>.
    /// </exception>
    public static IReadOnlyList<DrawingInstruction> ApplyDisplayFilters(
        IReadOnlyList<DrawingInstruction> instructions,
        IVectorPortrayalCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(catalogue);

        using (Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.viewing_groups"))
        {
            var stageStart = Stopwatch.GetTimestamp();
            var filtered = ApplyViewingGroups(instructions, catalogue.ViewingGroups);
            var planeFiltered = ApplyDisplayPlanes(filtered, catalogue.DisplayPlanes);
            RecordStageDuration(stageStart, "viewing_groups");
            PipelineMetrics.StageInstructionsCount.Record(
                planeFiltered.Count,
                new KeyValuePair<string, object?>(TelemetryTags.PipelineStage, "viewing_groups"));
            return planeFiltered;
        }
    }

    /// <summary>
    /// Returns whether the output of <see cref="ExecuteRulesAsync"/> for
    /// <paramref name="catalogue"/> can depend on the active display mode. Only
    /// XSLT rules receive it (as the <c>displayMode</c> parameter); Lua rules
    /// don't, so a Lua-only catalogue such as S-101's yields the same rule
    /// output in every display mode.
    /// </summary>
    /// <param name="catalogue">The portrayal catalogue.</param>
    /// <returns><see langword="true"/> when the catalogue has any XSLT rule.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="catalogue"/> is <see langword="null"/>.</exception>
    public static bool RuleOutputDependsOnDisplayMode(IVectorPortrayalCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        return catalogue.Rules.Any(static r => r.Type == PortrayalRuleType.Xslt);
    }

    private async Task<IReadOnlyList<DrawingInstruction>> ExecuteRulesCoreAsync(
        IFeatureXmlSource source,
        IVectorPortrayalCatalogue catalogue,
        Viewport? viewport,
        MarinerSettings? mariner,
        Activity? activity,
        CancellationToken cancellationToken)
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
        // XSLT output before priority sorting.
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

        // Priority sort. Filtering keeps order, so sorting before it gives the
        // same list as sorting after it, and lets the unfiltered list be reused.
        cancellationToken.ThrowIfCancellationRequested();
        using (Telemetry.ActivitySource.StartActivity("s100.pipeline.vector.stage.sort"))
        {
            var sortStart = Stopwatch.GetTimestamp();
            var sorted = SortByPriority(instructions);
            RecordStageDuration(sortStart, "sort");
            PipelineMetrics.StageInstructionsCount.Record(
                sorted.Count,
                new KeyValuePair<string, object?>(TelemetryTags.PipelineStage, "sort"));
            return sorted;
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

    /// <summary>
    /// Removes instructions with any hidden viewing group — an instruction
    /// assigned several groups is drawn only when all of them are visible
    /// (S-100 Part 9 §9-11.1.3; see
    /// <see cref="ViewingGroupController.IsVisible(DrawingInstruction)"/>).
    /// </summary>
    private static IReadOnlyList<DrawingInstruction> ApplyViewingGroups(
        IReadOnlyList<DrawingInstruction> instructions,
        ViewingGroupController viewingGroups)
    {
        return instructions
            .Where(viewingGroups.IsVisible)
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
    /// <summary>Drawn beneath the radar image.</summary>
    UnderRadar = 0,

    /// <summary>Drawn above the radar image.</summary>
    OverRadar = 1,
}

internal sealed class DefaultVectorLayer : IVectorLayer
{
    public required IReadOnlyList<DrawingInstruction> Instructions { get; init; }
}
