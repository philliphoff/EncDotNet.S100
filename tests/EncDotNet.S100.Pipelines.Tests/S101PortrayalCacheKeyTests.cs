using System.Collections.Generic;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the invalidation contract of
/// <see cref="S101DatasetProcessor.BuildPortrayalCacheKey"/>: the key
/// must change exactly when an input that changes the emitted
/// drawing-instruction list changes (mariner settings or ECDIS display
/// state), and must be stable across inputs that only affect later
/// rendering (palette / symbol / text scale — none of which are even
/// parameters of the key builder).
/// </summary>
public class S101PortrayalCacheKeyTests
{
    private static EcdisDisplaySettings Ecdis(
        EcdisDisplayCategory category = EcdisDisplayCategory.Standard,
        IReadOnlySet<int>? hiddenS101ViewingGroups = null,
        IReadOnlySet<DisplayPlane>? hiddenPlanes = null)
    {
        var settings = new EcdisDisplaySettings { Category = category };
        if (hiddenS101ViewingGroups is not null)
        {
            settings = settings with
            {
                HiddenViewingGroups = new Dictionary<string, IReadOnlySet<int>>
                {
                    ["S-101"] = hiddenS101ViewingGroups,
                },
            };
        }
        if (hiddenPlanes is not null)
        {
            settings = settings with { HiddenDisplayPlanes = hiddenPlanes };
        }
        return settings;
    }

    [Fact]
    public void IdenticalInputs_ProduceEqualKeys()
    {
        var a = S101DatasetProcessor.BuildPortrayalCacheKey(MarinerSettings.Default, Ecdis());
        var b = S101DatasetProcessor.BuildPortrayalCacheKey(MarinerSettings.Default, Ecdis());

        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentSafetyContour_ProducesDifferentKey()
    {
        var baseline = S101DatasetProcessor.BuildPortrayalCacheKey(MarinerSettings.Default, Ecdis());
        var changed = S101DatasetProcessor.BuildPortrayalCacheKey(
            MarinerSettings.Default with { SafetyContour = Depth.FromMetres(10.0) }, Ecdis());

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void DifferentNationalLanguage_ProducesDifferentKey()
    {
        var baseline = S101DatasetProcessor.BuildPortrayalCacheKey(MarinerSettings.Default, Ecdis());
        var changed = S101DatasetProcessor.BuildPortrayalCacheKey(
            MarinerSettings.Default with { NationalLanguage = "fra" }, Ecdis());

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void DifferentBooleanToggle_ProducesDifferentKey()
    {
        var baseline = S101DatasetProcessor.BuildPortrayalCacheKey(MarinerSettings.Default, Ecdis());
        var changed = S101DatasetProcessor.BuildPortrayalCacheKey(
            MarinerSettings.Default with { SimplifiedSymbols = true }, Ecdis());

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void DifferentEcdisCategory_ProducesDifferentKey()
    {
        var standard = S101DatasetProcessor.BuildPortrayalCacheKey(
            MarinerSettings.Default, Ecdis(EcdisDisplayCategory.Standard));
        var displayBase = S101DatasetProcessor.BuildPortrayalCacheKey(
            MarinerSettings.Default, Ecdis(EcdisDisplayCategory.DisplayBase));

        Assert.NotEqual(standard, displayBase);
    }

    [Fact]
    public void DifferentHiddenViewingGroups_ProducesDifferentKey()
    {
        var none = S101DatasetProcessor.BuildPortrayalCacheKey(MarinerSettings.Default, Ecdis());
        var hidden = S101DatasetProcessor.BuildPortrayalCacheKey(
            MarinerSettings.Default, Ecdis(hiddenS101ViewingGroups: new HashSet<int> { 22 }));

        Assert.NotEqual(none, hidden);
    }

    [Fact]
    public void HiddenViewingGroupOrder_DoesNotAffectKey()
    {
        var a = S101DatasetProcessor.BuildPortrayalCacheKey(
            MarinerSettings.Default, Ecdis(hiddenS101ViewingGroups: new HashSet<int> { 22, 5, 31 }));
        var b = S101DatasetProcessor.BuildPortrayalCacheKey(
            MarinerSettings.Default, Ecdis(hiddenS101ViewingGroups: new HashSet<int> { 31, 22, 5 }));

        Assert.Equal(a, b);
    }

    [Fact]
    public void HiddenViewingGroupsForOtherSpec_DoNotAffectKey()
    {
        var baseline = S101DatasetProcessor.BuildPortrayalCacheKey(MarinerSettings.Default, Ecdis());
        var otherSpec = new EcdisDisplaySettings
        {
            HiddenViewingGroups = new Dictionary<string, IReadOnlySet<int>>
            {
                ["S-201"] = new HashSet<int> { 7 },
            },
        };

        Assert.Equal(baseline, S101DatasetProcessor.BuildPortrayalCacheKey(MarinerSettings.Default, otherSpec));
    }

    [Fact]
    public void DifferentHiddenDisplayPlanes_ProducesDifferentKey()
    {
        var none = S101DatasetProcessor.BuildPortrayalCacheKey(MarinerSettings.Default, Ecdis());
        var hidden = S101DatasetProcessor.BuildPortrayalCacheKey(
            MarinerSettings.Default,
            Ecdis(hiddenPlanes: new HashSet<DisplayPlane> { DisplayPlane.UnderRadar }));

        Assert.NotEqual(none, hidden);
    }
}
