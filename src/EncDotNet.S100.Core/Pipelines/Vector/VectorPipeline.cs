using System.Xml.Linq;
using System.Xml.Xsl;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Six-stage vector portrayal pipeline (S-100 Part 9):
/// <list type="number">
///   <item>FeatureXML acquisition from <see cref="IFeatureXmlSource"/></item>
///   <item>Rule selection — match dataset feature types to catalogue rules</item>
///   <item>XSLT transformation — run applicable XSLT rules against the FeatureXML</item>
///   <item>Lua execution — delegate to <see cref="ILuaRuleExecutor"/> (Part 9A)</item>
///   <item>Drawing instruction assembly — parse XSLT output into typed objects and append Lua output</item>
///   <item>Viewing group filtering and priority sorting</item>
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
        // Stage 1 — load FeatureXML into a navigable document
        XDocument featureDoc;
        using (var reader = source.GetFeatureXml())
        {
            featureDoc = XDocument.Load(reader);
        }

        // Stage 2 — select applicable rules
        var featureTypes = source.FeatureTypesPresent;
        var applicableRules = SelectRules(featureTypes, catalogue);

        // Stage 3 — XSLT transformation
        var drawingInstructionsDoc = RunXsltRules(featureDoc, applicableRules, catalogue, viewport);

        // Stage 5 — assemble typed drawing instructions from the XSLT output
        // using the canonical S-100 Part 9 lower-camel-case display-list reader.
        var instructions = Part9DisplayListReader.Read(drawingInstructionsDoc).ToList();

        // Stage 4 — Lua execution (S-100 Part 9A). The executor produces typed
        // drawing instructions directly; append them to the XSLT-stage output
        // before viewing-group filtering and priority sorting.
        if (_luaExecutor is not null)
        {
            instructions.AddRange(_luaExecutor.Execute(mariner ?? new MarinerSettings()));
        }

        // Stage 6 — viewing group filter + priority sort
        var filtered = ApplyViewingGroups(instructions, catalogue.ViewingGroups);
        var sorted = SortByPriority(filtered);

        IVectorLayer layer = new DefaultVectorLayer
        {
            Instructions = sorted,
        };

        return Task.FromResult(layer);
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
                args.AddParam(token, string.Empty, color);
            }

            // Pass display scale if a viewport is available
            if (viewport is not null)
            {
                args.AddParam("displayScale", string.Empty, viewport.ScaleDenominator);
            }

            var transform = catalogue.GetCompiledRule(rule.Name);
            var resultFragment = new XDocument();

            using (var inputReader = featureDoc.CreateReader())
            using (var writer = resultFragment.CreateWriter())
            {
                transform.Transform(inputReader, args, writer);
            }

            // Accumulate results — each rule emits instruction elements
            if (resultFragment.Root is not null)
            {
                drawingInstructions.Root!.Add(resultFragment.Root.Elements());
            }
        }

        return drawingInstructions;
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
