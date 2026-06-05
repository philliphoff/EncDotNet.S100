using EncDotNet.S100.Datasets.S101;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Unit tests for <see cref="S101LegacyFeatureNames"/>, the legacy (pre-2.0.0)
/// → S-101 Edition 2.0.0 feature class name normalization used to dispatch the
/// bundled 2.0.0 Portrayal Catalogue Lua rules (S-100 Part 9A).
/// </summary>
public class S101LegacyFeatureNamesTests
{
    [Theory]
    [InlineData("BuoyCardinal", "CardinalBuoy")]
    [InlineData("BuoyInstallation", "InstallationBuoy")]
    [InlineData("BuoyIsolatedDanger", "IsolatedDangerBuoy")]
    [InlineData("BuoyLateral", "LateralBuoy")]
    [InlineData("BuoySafeWater", "SafeWaterBuoy")]
    [InlineData("BuoySpecialPurposeGeneral", "SpecialPurposeGeneralBuoy")]
    [InlineData("BuoyNewDangerMarking", "EmergencyWreckMarkingBuoy")]
    [InlineData("BeaconCardinal", "CardinalBeacon")]
    [InlineData("BeaconIsolatedDanger", "IsolatedDangerBeacon")]
    [InlineData("BeaconLateral", "LateralBeacon")]
    [InlineData("BeaconSafeWater", "SafeWaterBeacon")]
    [InlineData("BeaconSpecialPurposeGeneral", "SpecialPurposeGeneralBeacon")]
    public void Normalize_LegacyBuoyOrBeacon_ReturnsRenamed(string legacy, string expected)
    {
        Assert.Equal(expected, S101LegacyFeatureNames.Normalize(legacy));
    }

    [Theory]
    [InlineData("buoylateral", "LateralBuoy")]
    [InlineData("BUOYLATERAL", "LateralBuoy")]
    [InlineData("BeaconCARDINAL", "CardinalBeacon")]
    public void Normalize_IsCaseInsensitive(string legacy, string expected)
    {
        Assert.Equal(expected, S101LegacyFeatureNames.Normalize(legacy));
    }

    [Theory]
    [InlineData("LateralBuoy")]
    [InlineData("CardinalBeacon")]
    [InlineData("LightFloat")]
    [InlineData("RadarTransponderBeacon")]
    [InlineData("DepthArea")]
    [InlineData("SomeUnrelatedFeature")]
    public void Normalize_NonLegacyName_ReturnsUnchanged(string name)
    {
        Assert.Equal(name, S101LegacyFeatureNames.Normalize(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Normalize_NullOrEmpty_ReturnsInput(string? input)
    {
        Assert.Equal(input, S101LegacyFeatureNames.Normalize(input!));
    }

    [Theory]
    [InlineData("1", "Dolphin")]
    [InlineData("2", "Dolphin")]
    [InlineData("3", "Bollard")]
    [InlineData("5", "Pile")]
    [InlineData("7", "MooringBuoy")]
    public void Normalize_MooringWarpingFacility_MappedCategory_ReturnsTarget(string category, string expected)
    {
        var result = S101LegacyFeatureNames.Normalize(
            "MooringWarpingFacility",
            code => code == "categoryOfMooringWarpingFacility" ? category : null);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("4")]   // tie-up wall — no clean 2.0.0 equivalent
    [InlineData("6")]   // chain/wire/cable — no clean 2.0.0 equivalent
    [InlineData("99")]  // unknown enumerant
    [InlineData("")]    // present but empty
    public void Normalize_MooringWarpingFacility_UnmappedCategory_ReturnsUnchanged(string category)
    {
        var result = S101LegacyFeatureNames.Normalize(
            "MooringWarpingFacility",
            code => category);

        Assert.Equal("MooringWarpingFacility", result);
    }

    [Fact]
    public void Normalize_MooringWarpingFacility_NullLookupResult_ReturnsUnchanged()
    {
        var result = S101LegacyFeatureNames.Normalize(
            "MooringWarpingFacility",
            code => null);

        Assert.Equal("MooringWarpingFacility", result);
    }

    [Fact]
    public void Normalize_MooringWarpingFacility_NoLookup_ReturnsUnchanged()
    {
        Assert.Equal(
            "MooringWarpingFacility",
            S101LegacyFeatureNames.Normalize("MooringWarpingFacility"));
    }

    [Fact]
    public void Normalize_MooringWarpingFacility_LookupRequestsCategoryAttribute()
    {
        string? requested = null;
        S101LegacyFeatureNames.Normalize(
            "MooringWarpingFacility",
            code => { requested = code; return "3"; });

        Assert.Equal("categoryOfMooringWarpingFacility", requested);
    }
}
