using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression tests for the S-101 feature name labelling fix.
/// <para/>
/// The upstream Lua portrayal model's <c>GetFeatureName</c> requires both
/// <c>name</c> AND <c>nameUsage</c> on every <c>featureName</c> sub-attribute,
/// but the S-101 Feature Catalogue (Edition 1.x) declares <c>nameUsage</c>
/// with multiplicity <c>0..1</c> — it is optional. FC-conformant ENCs that
/// omit <c>nameUsage</c> previously rendered no label. The
/// <see cref="S101LuaRuleExecutor"/> adapter patch corrects this.
/// </summary>
public class S101FeatureNameTextTests
{
    /// <summary>
    /// The S-101 IHO V12 fixture <c>101AA00DS0008.000</c> contains many
    /// surface and point features with a <c>featureName</c> attribute whose
    /// <c>nameUsage</c> sub-attribute is omitted (FC-conformant). Without
    /// the adapter patch, none of these emit a <c>TextInstruction:</c>.
    /// </summary>
    private const string FixtureFile = "101AA00DS0008.000";

    [SkippableFact]
    public void NamedSurfaceFeatures_EmitTextInstruction()
    {
        var (emitted, namedSurfaces, _) = RunPipelineAndClassify();
        Skip.If(namedSurfaces.Count == 0, "Fixture has no named surface features.");

        var withText = emitted
            .Where(e => uint.TryParse(e.FeatureRef, out var id)
                && namedSurfaces.TryGetValue(id, out var name)
                && e.InstructionString.Contains("TextInstruction:" + name, StringComparison.Ordinal))
            .Select(e => e.FeatureRef)
            .ToHashSet();

        Assert.True(
            withText.Count > 0,
            $"Expected at least one surface featureName label to be emitted as TextInstruction; " +
            $"found 0 of {namedSurfaces.Count} named surface features.");
    }

    [SkippableFact]
    public void NamedPointFeatures_EmitTextInstruction()
    {
        var (emitted, _, namedPoints) = RunPipelineAndClassify();
        Skip.If(namedPoints.Count == 0, "Fixture has no named point features.");

        var withText = emitted
            .Where(e => uint.TryParse(e.FeatureRef, out var id)
                && namedPoints.TryGetValue(id, out var name)
                && e.InstructionString.Contains("TextInstruction:" + name, StringComparison.Ordinal))
            .Select(e => e.FeatureRef)
            .ToHashSet();

        Assert.True(
            withText.Count > 0,
            $"Expected at least one point featureName label to be emitted as TextInstruction; " +
            $"found 0 of {namedPoints.Count} named point features.");
    }

    private static (IReadOnlyList<EmittedInstruction> Emitted,
                    IReadOnlyDictionary<uint, string> NamedSurfaces,
                    IReadOnlyDictionary<uint, string> NamedPoints) RunPipelineAndClassify()
    {
        var fixturePath = ResolveFixturePath(FixtureFile);
        Skip.IfNot(File.Exists(fixturePath),
            $"S-101 fixture not found at expected path: {fixturePath}");

        var dataset = S101Dataset.Open(fixturePath);

        using var fcStream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new InvalidOperationException("S-101 feature catalogue not bundled.");
        var fc = FeatureCatalogueReader.Read(fcStream);

        using var pcSource = Specification.CreatePortrayalCatalogueSource("S-101");
        var pcProvider = PortrayalCatalogueProvider.OpenAsync(pcSource).GetAwaiter().GetResult();
        var luaEngine = new MoonSharpLuaEngine();
        var catalogue = new S101PortrayalCatalogue(pcProvider, luaEngine);
        catalogue.SwitchPalette(PaletteType.Day);

        var executor = new S101LuaRuleExecutor(luaEngine, dataset, catalogue, fc);
        var emitted = executor.ExecuteRaw(MarinerSettings.Default);

        // Classify named features: featureName (NATC=48) holds an empty
        // top-level marker followed by a `name` sibling sub-attribute
        // (NATC=49) per S-100 Part 10a ISO 8211 layout.
        var doc = dataset.Document;
        ushort? featureNameCode = null;
        ushort? nameCode = null;
        foreach (var (code, attrName) in doc.AttributeTypeCatalogue)
        {
            if (string.Equals(attrName, "featureName", StringComparison.OrdinalIgnoreCase))
                featureNameCode = code;
            if (string.Equals(attrName, "name", StringComparison.OrdinalIgnoreCase))
                nameCode = code;
        }
        Skip.IfNot(featureNameCode is not null && nameCode is not null,
            "Fixture catalogue does not define featureName/name attributes.");

        var namedSurfaces = new Dictionary<uint, string>();
        var namedPoints = new Dictionary<uint, string>();
        foreach (var feat in doc.Features)
        {
            if (feat.SpatialAssociations.Length == 0) continue;
            if (!feat.Attributes.Any(a => a.NumericCode == featureNameCode))
                continue;

            // Find the `name` sub-attribute value (first instance).
            string? value = null;
            foreach (var attr in feat.Attributes)
            {
                if (attr.NumericCode == nameCode && !string.IsNullOrEmpty(attr.Value))
                {
                    value = attr.Value;
                    break;
                }
            }
            if (value is null) continue;

            // RCNM 130 = Surface; 110 = Point (S-100 Part 10a).
            var rcnm = feat.SpatialAssociations[0].RecordName;
            if (rcnm == 130) namedSurfaces[feat.RecordId] = value;
            else if (rcnm == 110) namedPoints[feat.RecordId] = value;
        }

        return (emitted, namedSurfaces, namedPoints);
    }

    /// <summary>
    /// Resolves the bundled S-101 test dataset by walking up from the test
    /// assembly's base directory until the <c>tests/datasets/S101</c>
    /// folder is found.
    /// </summary>
    private static string ResolveFixturePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", "S101", "S-101", "DATASET_FILES", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine("tests", "datasets", "S101", "S-101", "DATASET_FILES", fileName);
    }
}
