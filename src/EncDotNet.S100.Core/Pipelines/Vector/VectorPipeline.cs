using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Six-stage vector portrayal pipeline (S-100 Part 9):
/// <list type="number">
///   <item>FeatureXML acquisition from <see cref="IFeatureXmlSource"/></item>
///   <item>Rule selection — match dataset feature types to catalogue rules</item>
///   <item>XSLT transformation — run applicable XSLT rules against the FeatureXML</item>
///   <item>Lua execution — run applicable Lua rules with navigation context</item>
///   <item>Drawing instruction assembly — parse XML output into typed objects</item>
///   <item>Viewing group filtering and priority sorting</item>
/// </list>
/// </summary>
public class VectorPipeline
{
    private readonly ILuaEngine? _luaEngine;

    public VectorPipeline(ILuaEngine? luaEngine = null)
    {
        _luaEngine = luaEngine;
    }

    public Task<IVectorLayer> ProcessAsync(
        IFeatureXmlSource source,
        IVectorPortrayalCatalogue catalogue,
        NavigationContext? context = null)
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
        var drawingInstructionsDoc = RunXsltRules(featureDoc, applicableRules, catalogue, context);

        // Stage 4 — Lua execution
        RunLuaRules(drawingInstructionsDoc, applicableRules, catalogue, context);

        // Stage 5 — assemble typed drawing instructions
        var instructions = AssembleInstructions(drawingInstructionsDoc, catalogue);

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
        NavigationContext? context)
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

            // Pass display scale if navigation context is available
            if (context is not null)
            {
                args.AddParam("displayScale", string.Empty, context.ScaleDenominator);
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

    // ── Stage 4: Lua execution ─────────────────────────────────────────

    private void RunLuaRules(
        XDocument drawingInstructionsDoc,
        IReadOnlyList<PortrayalRule> rules,
        IVectorPortrayalCatalogue catalogue,
        NavigationContext? context)
    {
        var luaRules = rules.Where(r => r.Type == PortrayalRuleType.Lua).ToList();
        if (luaRules.Count == 0) return;

        if (_luaEngine is null)
        {
            throw new InvalidOperationException(
                "Lua rules are present but no ILuaEngine was provided to the pipeline.");
        }

        foreach (var rule in luaRules)
        {
            var script = catalogue.GetLuaScript(rule.Name);

            using var lua = _luaEngine.CreateContext();

            // Configure the S-100 host environment
            var contextParams = new Dictionary<string, object?>();
            if (context is not null)
            {
                contextParams["SafetyContour"] = context.SafetyContour;
                contextParams["SafetyDepth"] = context.SafetyDepth;
                contextParams["ShallowContour"] = context.ShallowContour;
                contextParams["DeepContour"] = context.DeepContour;
                contextParams["displayScale"] = context.ScaleDenominator;
            }

            S100LuaHost.Configure(
                lua,
                token => catalogue.ActivePalette.Resolve(token),
                contextParams);

            // Expose symbol lookup to Lua
            lua.SetGlobal("getSymbol", (Func<string, string>)(name =>
            {
                var sym = catalogue.GetSymbol(name);
                return sym.Name;
            }));

            // Execute the script to define its functions
            lua.Execute(script.Source);

            // Capture drawing instruction XML strings emitted by the script
            var emittedInstructions = new List<string>();
            lua.SetGlobal("_emitDrawingInstruction",
                (Action<string>)(xml => emittedInstructions.Add(xml)));

            // Provide an emit helper the script can call
            lua.Execute("""
                function emitDrawingInstruction(xml)
                    _emitDrawingInstruction(xml)
                end
                """);

            // Call the script's main entry point if it exists
            lua.Call("main", drawingInstructionsDoc.Root?.ToString() ?? "");

            // Merge emitted instructions into the accumulated document
            foreach (var xml in emittedInstructions)
            {
                try
                {
                    var element = XElement.Parse(xml);
                    drawingInstructionsDoc.Root!.Add(element);
                }
                catch (XmlException)
                {
                    // Skip malformed Lua output
                }
            }
        }
    }

    // ── Stage 5: Drawing instruction assembly ──────────────────────────

    private static IReadOnlyList<DrawingInstruction> AssembleInstructions(
        XDocument drawingInstructionsDoc,
        IVectorPortrayalCatalogue catalogue)
    {
        var instructions = new List<DrawingInstruction>();

        foreach (var element in drawingInstructionsDoc.Root?.Elements() ?? [])
        {
            var instruction = element.Name.LocalName switch
            {
                "PointInstruction" => AssemblePointInstruction(element, catalogue),
                "LineInstruction" => AssembleLineInstruction(element, catalogue),
                "AreaInstruction" => AssembleAreaInstruction(element, catalogue),
                "TextInstruction" => AssembleTextInstruction(element),
                _ => null,
            };

            if (instruction is not null)
            {
                instructions.Add(instruction);
            }
        }

        return instructions;
    }

    private static DrawingInstruction? AssemblePointInstruction(
        XElement element, IVectorPortrayalCatalogue catalogue)
    {
        var symbolRef = element.Element("Symbol")?.Attribute("ref")?.Value;
        if (symbolRef is null) return null;

        var position = element.Element("Position");
        if (position is null) return null;

        return new PointInstruction
        {
            FeatureId = element.Attribute("id")?.Value ?? "",
            Plane = ParsePlane(element.Attribute("plane")?.Value),
            ViewingGroup = ParseInt(element.Attribute("viewingGroup")?.Value),
            DrawingPriority = ParseInt(element.Attribute("priority")?.Value),
            Latitude = ParseDouble(position.Attribute("lat")?.Value),
            Longitude = ParseDouble(position.Attribute("lon")?.Value),
            Symbol = catalogue.GetSymbol(symbolRef),
            Rotation = ParseDouble(element.Element("Symbol")?.Attribute("rotation")?.Value),
            Scale = ParseDouble(element.Element("Symbol")?.Attribute("scale")?.Value, 1.0),
        };
    }

    private static DrawingInstruction? AssembleLineInstruction(
        XElement element, IVectorPortrayalCatalogue catalogue)
    {
        var styleRef = element.Element("LineStyle")?.Attribute("ref")?.Value;
        if (styleRef is null) return null;

        var geometryElement = element.Element("Geometry");
        var coords = ParseCoordinates(geometryElement);

        return new LineInstruction
        {
            FeatureId = element.Attribute("id")?.Value ?? "",
            Plane = ParsePlane(element.Attribute("plane")?.Value),
            ViewingGroup = ParseInt(element.Attribute("viewingGroup")?.Value),
            DrawingPriority = ParseInt(element.Attribute("priority")?.Value),
            Geometry = coords,
            LineStyle = catalogue.GetLineStyle(styleRef),
        };
    }

    private static DrawingInstruction? AssembleAreaInstruction(
        XElement element, IVectorPortrayalCatalogue catalogue)
    {
        var fillRef = element.Element("AreaFill")?.Attribute("ref")?.Value;
        if (fillRef is null) return null;

        var rings = new List<IReadOnlyList<(double, double)>>();
        foreach (var ringElement in element.Elements("Ring"))
        {
            rings.Add(ParseCoordinates(ringElement));
        }

        // If no Ring elements, try Geometry as a single ring
        if (rings.Count == 0)
        {
            var geometry = element.Element("Geometry");
            if (geometry is not null)
            {
                rings.Add(ParseCoordinates(geometry));
            }
        }

        var outlineRef = element.Element("OutlineStyle")?.Attribute("ref")?.Value;

        return new AreaInstruction
        {
            FeatureId = element.Attribute("id")?.Value ?? "",
            Plane = ParsePlane(element.Attribute("plane")?.Value),
            ViewingGroup = ParseInt(element.Attribute("viewingGroup")?.Value),
            DrawingPriority = ParseInt(element.Attribute("priority")?.Value),
            Rings = rings,
            AreaFill = catalogue.GetAreaFill(fillRef),
            OutlineStyle = outlineRef is not null ? catalogue.GetLineStyle(outlineRef) : null,
        };
    }

    private static DrawingInstruction? AssembleTextInstruction(XElement element)
    {
        var position = element.Element("Position");
        var textContent = element.Element("Text")?.Value;
        if (position is null || textContent is null) return null;

        var textStyle = element.Element("TextStyle");

        return new TextInstruction
        {
            FeatureId = element.Attribute("id")?.Value ?? "",
            Plane = ParsePlane(element.Attribute("plane")?.Value),
            ViewingGroup = ParseInt(element.Attribute("viewingGroup")?.Value),
            DrawingPriority = ParseInt(element.Attribute("priority")?.Value),
            Latitude = ParseDouble(position.Attribute("lat")?.Value),
            Longitude = ParseDouble(position.Attribute("lon")?.Value),
            Text = textContent,
            FontRef = textStyle?.Attribute("ref")?.Value,
            FontSize = ParseDouble(textStyle?.Attribute("fontSize")?.Value, 10.0),
            Color = textStyle?.Attribute("color")?.Value ?? "#000000",
            Rotation = ParseDouble(element.Attribute("rotation")?.Value),
        };
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

    // ── Parsing helpers ────────────────────────────────────────────────

    private static IReadOnlyList<(double Latitude, double Longitude)> ParseCoordinates(
        XElement? container)
    {
        if (container is null) return [];

        return container.Elements("Point")
            .Select(p => (
                Latitude: ParseDouble(p.Attribute("lat")?.Value),
                Longitude: ParseDouble(p.Attribute("lon")?.Value)))
            .ToList();
    }

    private static DisplayPlane ParsePlane(string? value)
    {
        if (value is not null && Enum.TryParse<DisplayPlane>(value, ignoreCase: true, out var plane))
            return plane;
        return DisplayPlane.OverRadar;
    }

    private static int ParseInt(string? value, int defaultValue = 0) =>
        int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;

    private static double ParseDouble(string? value, double defaultValue = 0.0) =>
        double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
}

/// <summary>
/// A styled vector layer carrying ordered drawing instructions produced
/// by portrayal rule evaluation, ready for rendering.
/// </summary>
public interface IVectorLayer
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
