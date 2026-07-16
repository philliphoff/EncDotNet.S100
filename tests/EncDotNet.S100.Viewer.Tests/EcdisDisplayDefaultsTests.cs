using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public class EcdisDisplayDefaultsTests
{
    [Fact]
    public void Apply_FreshProfile_HidesS101MarinerSelectorPatterns()
    {
        var settings = new ViewerSettings();

        var applied = EcdisDisplayDefaults.Apply(settings);

        Assert.True(applied);
        Assert.True(settings.EcdisDefaultsApplied);
        Assert.Equal("90000,90010,90011", settings.EcdisHiddenViewingGroups["S-101"]);
    }

    [Fact]
    public void Apply_IsIdempotent_DoesNotReSeedAfterFlagSet()
    {
        var settings = new ViewerSettings();
        EcdisDisplayDefaults.Apply(settings);

        // Mariner re-enables the shallow water pattern by clearing the override.
        settings.EcdisHiddenViewingGroups["S-101"] = "90010,90011";

        var appliedAgain = EcdisDisplayDefaults.Apply(settings);

        Assert.False(appliedAgain);
        Assert.Equal("90010,90011", settings.EcdisHiddenViewingGroups["S-101"]);
    }

    [Fact]
    public void Apply_PreservesExistingHiddenIds()
    {
        var settings = new ViewerSettings();
        settings.EcdisHiddenViewingGroups["S-101"] = "27070";

        EcdisDisplayDefaults.Apply(settings);

        var ids = settings.EcdisHiddenViewingGroups["S-101"]
            .Split(',')
            .Select(int.Parse)
            .OrderBy(i => i)
            .ToArray();
        Assert.Equal(new[] { 27070, 90000, 90010, 90011 }, ids);
    }

    [Fact]
    public void DefaultHiddenViewingGroups_DeclaresS101Selectors()
    {
        Assert.True(EcdisDisplayDefaults.DefaultHiddenViewingGroups.TryGetValue("S-101", out var ids));
        Assert.Equal(new[] { 90000, 90010, 90011 }, ids!.OrderBy(i => i).ToArray());
    }
}
