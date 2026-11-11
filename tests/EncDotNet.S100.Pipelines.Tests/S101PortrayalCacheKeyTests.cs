using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the invalidation contract of the portrayal caches' keys:
/// <see cref="S101DatasetProcessor.BuildRuleOutputCacheKey"/> must change
/// exactly when an input to the Lua rules changes (the mariner settings, and the
/// display mode only for catalogues whose XSLT rules read it), and
/// <see cref="S101DatasetProcessor.BuildVisibleAreaSignature"/> exactly when the
/// set of visible area fills, the pattern clip's only input, changes.
/// </summary>
public class S101PortrayalCacheKeyTests
{
    [Fact]
    public void IdenticalInputs_ProduceEqualKeys()
    {
        var a = S101DatasetProcessor.BuildRuleOutputCacheKey(MarinerSettings.Default, displayModeId: null);
        var b = S101DatasetProcessor.BuildRuleOutputCacheKey(MarinerSettings.Default, displayModeId: null);

        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentSafetyContour_ProducesDifferentKey()
    {
        var baseline = S101DatasetProcessor.BuildRuleOutputCacheKey(MarinerSettings.Default, displayModeId: null);
        var changed = S101DatasetProcessor.BuildRuleOutputCacheKey(
            MarinerSettings.Default with { SafetyContour = Depth.FromMetres(10.0) }, displayModeId: null);

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void DifferentNationalLanguage_ProducesDifferentKey()
    {
        var baseline = S101DatasetProcessor.BuildRuleOutputCacheKey(MarinerSettings.Default, displayModeId: null);
        var changed = S101DatasetProcessor.BuildRuleOutputCacheKey(
            MarinerSettings.Default with { NationalLanguage = "fra" }, displayModeId: null);

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void DifferentBooleanToggle_ProducesDifferentKey()
    {
        var baseline = S101DatasetProcessor.BuildRuleOutputCacheKey(MarinerSettings.Default, displayModeId: null);
        var changed = S101DatasetProcessor.BuildRuleOutputCacheKey(
            MarinerSettings.Default with { SimplifiedSymbols = true }, displayModeId: null);

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void DisplayMode_IsFoldedOnlyWhenRulesDependOnIt()
    {
        var lua = S101DatasetProcessor.BuildRuleOutputCacheKey(MarinerSettings.Default, displayModeId: null);
        var standard = S101DatasetProcessor.BuildRuleOutputCacheKey(MarinerSettings.Default, displayModeId: "Standard");
        var displayBase = S101DatasetProcessor.BuildRuleOutputCacheKey(MarinerSettings.Default, displayModeId: "DisplayBase");

        Assert.NotEqual(lua, standard);
        Assert.NotEqual(standard, displayBase);
    }

    [Fact]
    public void VisibleAreaSignature_IgnoresNonAreaInstructions()
    {
        var area = Area("1");
        var point = new PointInstruction { FeatureReference = "2", SymbolReference = "UWTROC03" };
        IReadOnlyList<DrawingInstruction> unfiltered = [area, point];

        // Hiding a point (rocks, say) leaves the area fills, and so the clip, unchanged.
        Assert.Equal(
            S101DatasetProcessor.BuildVisibleAreaSignature(unfiltered, unfiltered),
            S101DatasetProcessor.BuildVisibleAreaSignature(unfiltered, [area]));
    }

    [Fact]
    public void VisibleAreaSignature_ChangesWhenAnAreaIsHidden()
    {
        var a = Area("1");
        var b = Area("2");
        IReadOnlyList<DrawingInstruction> unfiltered = [a, b];

        var both = S101DatasetProcessor.BuildVisibleAreaSignature(unfiltered, unfiltered);
        var onlyA = S101DatasetProcessor.BuildVisibleAreaSignature(unfiltered, [a]);
        var onlyB = S101DatasetProcessor.BuildVisibleAreaSignature(unfiltered, [b]);

        Assert.NotEqual(both, onlyA);
        Assert.NotEqual(both, onlyB);
        Assert.NotEqual(onlyA, onlyB);
    }

    [Fact]
    public void VisibleAreaSignature_MatchesByIdentity_NotContent()
    {
        // Two instructions with identical content are still different areas.
        var a = Area("1");
        var twin = Area("1");
        IReadOnlyList<DrawingInstruction> unfiltered = [a, twin];

        Assert.NotEqual(
            S101DatasetProcessor.BuildVisibleAreaSignature(unfiltered, [a]),
            S101DatasetProcessor.BuildVisibleAreaSignature(unfiltered, [twin]));
    }

    private static AreaInstruction Area(string featureReference) =>
        new() { FeatureReference = featureReference, AreaFillReference = "DIAMOND1", DrawingPriority = 5 };
}
