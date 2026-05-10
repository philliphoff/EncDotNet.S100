using System.Globalization;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S102;

/// <summary>
/// S-102 portrayal catalogue that executes the BathymetryCoverage.lua script
/// from the official S-102 Portrayal Catalogue assets via an <see cref="ILuaEngine"/>.
/// Supports both two-shade and four-shade depth colour schemes.
/// </summary>
/// <remarks>
/// The official S-102 Lua portrayal scripts define:
/// <code>
///   function BathymetryCoverage(feature, featurePortrayal, contextParameters)
/// </code>
/// which emits drawing instructions via <c>featurePortrayal:AddInstructions(...)</c>.
/// This class provides stub <c>feature</c> and <c>featurePortrayal</c> objects,
/// captures the emitted instructions, and parses them into a <see cref="CoverageColorScheme"/>.
/// </remarks>
public class S102PortrayalCatalogue : ICoveragePortrayalCatalogue
{
    // IHO S-52 Day palette sRGB colours for depth tokens (defaults when palette has no entry)
    private static readonly Dictionary<string, string> DefaultColorTokens = new()
    {
        ["DEPIT"] = "#58AF9C",
        ["DEPVS"] = "#61B7FF",
        ["DEPMS"] = "#82CAFF",
        ["DEPMD"] = "#A7D9FB",
        ["DEPDW"] = "#C9EDFF",
    };

    private readonly ILuaEngine _luaEngine;
    private readonly PortrayalCatalogueProvider _provider;
    private string? _cachedLuaSource;

    /// <summary>
    /// Creates a catalogue backed by Lua script execution.
    /// </summary>
    /// <param name="luaEngine">The Lua engine used to run portrayal scripts.</param>
    /// <param name="provider">The portrayal catalogue provider that supplies script assets.</param>
    public S102PortrayalCatalogue(ILuaEngine luaEngine, PortrayalCatalogueProvider provider)
    {
        ArgumentNullException.ThrowIfNull(luaEngine);
        ArgumentNullException.ThrowIfNull(provider);
        _luaEngine = luaEngine;
        _provider = provider;
    }

    public SpecRef Spec => new("S-102", default);
    public string Edition => "3.0.0";
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    /// <summary>Whether to use four depth shading bands (true) or two (false).</summary>
    public bool FourShades { get; set; } = true;

    public void SwitchPalette(PaletteType type)
    {
        ActivePalette = ColorPalette.FromType(type);
    }

    public CoverageColorScheme ResolveColorScheme(MarinerSettings settings)
    {
        using var lua = _luaEngine.CreateContext();

        // Capture drawing instructions emitted by the Lua script
        var instructions = new List<string>();

        // Provide a contextParameters table matching the S-102 PC context parameter IDs
        var contextParams = new Dictionary<string, object?>
        {
            ["FourShades"] = FourShades,
            ["SafetyContour"] = settings.SafetyContour,
            ["ShallowContour"] = settings.ShallowContour,
            ["DeepContour"] = settings.DeepContour,
            ["SafetyDepth"] = settings.SafetyDepth,
        };
        lua.SetGlobal("contextParameters", contextParams);

        // Provide a stub featurePortrayal with AddInstructions method
        // The real script calls: featurePortrayal:AddInstructions(str)
        // In Lua, obj:Method(arg) is sugar for obj.Method(obj, arg),
        // so we register a global function and wire it via a table.
        lua.SetGlobal("_addInstructions",
            (Action<string>)(instr => instructions.Add(instr)));

        lua.Execute("""
            featurePortrayal = {}
            function featurePortrayal:AddInstructions(instr)
                _addInstructions(instr)
            end
            feature = { Code = 'BathymetryCoverage' }
            """);

        // Load and execute the BathymetryCoverage.lua script
        string luaSource = GetLuaSource();
        lua.Execute(luaSource);

        // Invoke BathymetryCoverage(feature, featurePortrayal, contextParameters) from Lua—
        // all three arguments are already globals, so we avoid marshalling round-trips
        // that would strip methods from the featurePortrayal table.
        lua.Execute("BathymetryCoverage(feature, featurePortrayal, contextParameters)");

        return ParseDrawingInstructions(instructions);
    }

    public IReadOnlyList<ContourStyle> Contours => [];

    private string GetLuaSource()
    {
        if (_cachedLuaSource is not null)
        {
            return _cachedLuaSource;
        }

        var ruleFile = _provider.Catalogue.RuleFiles
            .FirstOrDefault(r => r.FileName.Contains("BathymetryCoverage", StringComparison.OrdinalIgnoreCase));

        if (ruleFile is null)
        {
            throw new InvalidOperationException(
                "The S-102 portrayal catalogue does not contain a BathymetryCoverage rule file.");
        }

        using var stream = _provider.FetchAssetAsync(ruleFile).GetAwaiter().GetResult();
        using var reader = new StreamReader(stream);
        _cachedLuaSource = reader.ReadToEnd();
        return _cachedLuaSource;
    }

    /// <summary>
    /// Parses the drawing instruction strings emitted by BathymetryCoverage.lua
    /// into a <see cref="CoverageColorScheme"/>.
    /// </summary>
    /// <remarks>
    /// The script emits instructions like:
    /// <code>
    ///   CoverageColor:DEPVS,0;LookupEntry:Shallow Water,0,30,geLtInterval;CoverageFill:depth
    ///   CoverageColor:DEPIT,0;LookupEntry:Intertidal,,0,ltSemiInterval
    /// </code>
    /// Each instruction string contains semicolon-separated directives. We extract
    /// <c>CoverageColor</c> and <c>LookupEntry</c> pairs to build color bands.
    /// </remarks>
    private CoverageColorScheme ParseDrawingInstructions(List<string> instructions)
    {
        var bands = new List<ColorBand>();
        string? fieldName = "depth";

        foreach (var instruction in instructions)
        {
            string? currentToken = null;
            string? label = null;
            float minValue = float.MinValue;
            float maxValue = float.MaxValue;

            var directives = instruction.Split(';');
            foreach (var directive in directives)
            {
                var colonIdx = directive.IndexOf(':');
                if (colonIdx < 0) continue;

                var key = directive[..colonIdx];
                var value = directive[(colonIdx + 1)..];

                switch (key)
                {
                    case "CoverageColor":
                    {
                        // CoverageColor:TOKEN,transparency
                        var parts = value.Split(',');
                        currentToken = parts[0];
                        break;
                    }
                    case "LookupEntry":
                    {
                        // LookupEntry:label,min,max,intervalType
                        // min/max may be empty for semi-intervals
                        var parts = value.Split(',');
                        label = parts.Length > 0 ? parts[0] : null;

                        if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                            minValue = float.Parse(parts[1], CultureInfo.InvariantCulture);

                        if (parts.Length > 2 && !string.IsNullOrEmpty(parts[2]))
                            maxValue = float.Parse(parts[2], CultureInfo.InvariantCulture);

                        break;
                    }
                    case "CoverageFill":
                    {
                        fieldName = value;
                        break;
                    }
                }
            }

            if (currentToken is not null)
            {
                bands.Add(new ColorBand
                {
                    MinValue = minValue,
                    MaxValue = maxValue,
                    Color = ResolveColorToken(currentToken),
                    Label = label,
                });
            }
        }

        return new CoverageColorScheme { FieldName = fieldName ?? "depth", Bands = bands };
    }

    private string ResolveColorToken(string token)
    {
        // Try the active palette first, then fall back to default IHO S-52 colours
        var resolved = ActivePalette.Resolve(token);
        if (resolved != "#000000")
            return resolved;

        return DefaultColorTokens.TryGetValue(token, out var hex) ? hex : "#000000";
    }
}