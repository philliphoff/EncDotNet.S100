using EncDotNet.S100.DynamicSources.Ais;

namespace EncDotNet.S100.DynamicSources.Ais.Tests;

public class AisShipTypeClassTests
{
    [Theory]
    [InlineData(0, AisShipTypeClass.Unknown)]
    [InlineData(15, AisShipTypeClass.Other)]
    [InlineData(20, AisShipTypeClass.HighSpeedCraft)]
    [InlineData(29, AisShipTypeClass.HighSpeedCraft)]
    [InlineData(30, AisShipTypeClass.Fishing)]
    [InlineData(31, AisShipTypeClass.Tug)]
    [InlineData(32, AisShipTypeClass.Tug)]
    [InlineData(35, AisShipTypeClass.Military)]
    [InlineData(36, AisShipTypeClass.Sailing)]
    [InlineData(37, AisShipTypeClass.Pleasure)]
    [InlineData(40, AisShipTypeClass.HighSpeedCraft)]
    [InlineData(49, AisShipTypeClass.HighSpeedCraft)]
    [InlineData(50, AisShipTypeClass.PilotVessel)]
    [InlineData(51, AisShipTypeClass.SearchAndRescue)]
    [InlineData(52, AisShipTypeClass.Tug)]
    [InlineData(55, AisShipTypeClass.LawEnforcement)]
    [InlineData(60, AisShipTypeClass.Passenger)]
    [InlineData(69, AisShipTypeClass.Passenger)]
    [InlineData(70, AisShipTypeClass.Cargo)]
    [InlineData(79, AisShipTypeClass.Cargo)]
    [InlineData(80, AisShipTypeClass.Tanker)]
    [InlineData(89, AisShipTypeClass.Tanker)]
    [InlineData(90, AisShipTypeClass.Other)]
    [InlineData(255, AisShipTypeClass.Other)]
    public void ToClass_BucketsPerItuTable53(int code, AisShipTypeClass expected)
        => Assert.Equal(expected, AisShipTypeClassExtensions.ToClass(code));

    [Theory]
    [InlineData(AisShipTypeClass.Cargo, "cargo")]
    [InlineData(AisShipTypeClass.Tanker, "tanker")]
    [InlineData(AisShipTypeClass.SearchAndRescue, "sar")]
    [InlineData(AisShipTypeClass.Unknown, "unknown")]
    public void ToKindToken_IsLowerCaseAndStable(AisShipTypeClass cls, string expected)
        => Assert.Equal(expected, cls.ToKindToken());
}
