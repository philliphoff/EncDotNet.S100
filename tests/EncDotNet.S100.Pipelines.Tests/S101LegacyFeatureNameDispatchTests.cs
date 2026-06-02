using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression tests for the S-101 legacy feature-name compatibility shim
/// (<see cref="S101LegacyFeatureNames"/>). Datasets authored against a pre-2.0.0
/// edition of the S-101 Feature Catalogue report legacy feature class names
/// (e.g. <c>BuoyLateral</c>) that do not match the word-reversed rule modules in
/// the bundled 2.0.0 Portrayal Catalogue (e.g. <c>LateralBuoy.lua</c>). Without
/// normalization the Lua dispatcher (<c>main.lua</c>) fails its
/// <c>require(feature.Code)</c> and falls back to DEFAULT symbology
/// (the <c>QUESMRK1</c> "question mark" symbol).
/// </summary>
public class S101LegacyFeatureNameDispatchTests
{
    /// <summary>
    /// Fixture containing legacy-named buoy and beacon features
    /// (<c>BuoyLateral</c>, <c>BuoyCardinal</c>, <c>BuoySpecialPurposeGeneral</c>,
    /// <c>BeaconCardinal</c>).
    /// </summary>
    private const string BuoyBeaconFixture = "101AA00DS0020.000";

    /// <summary>The DEFAULT (rule-not-found) fallback symbol from Default.lua.</summary>
    private const string DefaultSymbol = "QUESMRK1";

    private static readonly string[] LegacyBuoyBeaconCodes =
    [
        "BuoyLateral",
        "BuoyCardinal",
        "BuoySpecialPurposeGeneral",
        "BeaconCardinal",
    ];

    [SkippableFact]
    public void LegacyNamedBuoyAndBeaconFeatures_DoNotFallBackToDefaultSymbology()
    {
        var fixturePath = ResolveFixturePath(BuoyBeaconFixture);
        Skip.IfNot(File.Exists(fixturePath),
            $"S-101 fixture not found at expected path: {fixturePath}");

        var dataset = S101Dataset.Open(fixturePath);
        var doc = dataset.Document;

        // Map record ID -> legacy feature class code for the features of interest.
        var legacyFeatures = new Dictionary<uint, string>();
        foreach (var feat in doc.Features)
        {
            if (!doc.FeatureTypeCatalogue.TryGetValue(feat.FeatureTypeCode, out var code))
                continue;
            if (LegacyBuoyBeaconCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
                legacyFeatures[feat.RecordId] = code;
        }

        Skip.If(legacyFeatures.Count == 0,
            "Fixture contains no legacy-named buoy or beacon features.");

        var emitted = RunExecutor(dataset);

        // Group emitted instructions by feature.
        var byFeature = emitted
            .Where(e => uint.TryParse(e.FeatureRef, out _))
            .GroupBy(e => uint.Parse(e.FeatureRef))
            .ToDictionary(g => g.Key, g => g.Select(e => e.InstructionString).ToList());

        var defaultedFeatures = new List<string>();
        var properlyPortrayed = 0;
        foreach (var (recordId, code) in legacyFeatures)
        {
            if (!byFeature.TryGetValue(recordId, out var instructions) || instructions.Count == 0)
            {
                // No instructions at all is itself a failure to portray.
                defaultedFeatures.Add($"{code} (ID={recordId}): no instructions");
                continue;
            }

            // A feature left on DEFAULT symbology emits only QUESMRK1 references.
            var hasNonDefault = instructions.Any(
                s => !s.Contains(DefaultSymbol, StringComparison.Ordinal));
            if (hasNonDefault)
                properlyPortrayed++;
            else
                defaultedFeatures.Add($"{code} (ID={recordId})");
        }

        Assert.True(
            defaultedFeatures.Count == 0,
            $"Expected all {legacyFeatures.Count} legacy-named buoy/beacon features to be " +
            $"portrayed with their proper 2.0.0 symbology, but {defaultedFeatures.Count} fell " +
            $"back to DEFAULT ({DefaultSymbol}): {string.Join(", ", defaultedFeatures)}.");

        Assert.True(properlyPortrayed > 0, "Expected at least one legacy feature to be portrayed.");
    }

    private static IReadOnlyList<EmittedInstruction> RunExecutor(S101Dataset dataset)
    {
        using var fcStream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new InvalidOperationException("S-101 feature catalogue not bundled.");
        var fc = FeatureCatalogueReader.Read(fcStream);

        using var pcSource = Specification.CreatePortrayalCatalogueSource("S-101");
        var pcProvider = PortrayalCatalogueProvider.OpenAsync(pcSource).GetAwaiter().GetResult();
        var luaEngine = new MoonSharpLuaEngine();
        var catalogue = new S101PortrayalCatalogue(pcProvider, luaEngine);
        catalogue.SwitchPalette(PaletteType.Day);

        var executor = new S101LuaRuleExecutor(luaEngine, dataset, catalogue, fc);
        return executor.ExecuteRaw(MarinerSettings.Default);
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
