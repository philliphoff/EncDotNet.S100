namespace EncDotNet.S100.Datasets.S57.Tests;

public class S101FeatureAttributeBindingsTests
{
    [Theory]
    [InlineData("Bridge", "bridgeConstruction", true)]       // [0..1]
    [InlineData("Bridge", "categoryOfOpeningBridge", true)]  // [0..1]
    [InlineData("Bridge", "openingBridge", true)]            // [0..1]
    [InlineData("Bridge", "bridgeFunction", false)]          // [0..*]
    [InlineData("SpanFixed", "bridgeConstruction", false)]   // not bound
    [InlineData(null, "bridgeConstruction", false)]
    public void IsSingleValued_FollowsCatalogueMultiplicity(string? featureCode, string attributeCode, bool expected)
    {
        Assert.Equal(expected, S101FeatureAttributeBindings.Default.IsSingleValued(featureCode, attributeCode));
    }

    [Fact]
    public void BindsInformationType_FollowsEveryTypeOfABinding()
    {
        var s401 = S101FeatureAttributeBindings.ForSpec("S-401");

        // One AdditionalInformation binding lists five information types.
        Assert.True(s401.BindsInformationType("LockBasin", "AdditionalInformation", "NauticalInformation"));
        Assert.True(s401.BindsInformationType("LockBasin", "AdditionalInformation", "TimeScheduleInGeneral"));
        Assert.True(s401.BindsInformationType("Bridge", "AdditionalInformation", "ServiceHours"));
        Assert.False(s401.BindsInformationType("Bridge", "AdditionalInformation", "TimeScheduleInGeneral"));
        Assert.False(s401.BindsInformationType(null, "AdditionalInformation", "NauticalInformation"));
        Assert.False(S101FeatureAttributeBindings.Default.BindsInformationType("LockBasin", "AdditionalInformation", "TimeScheduleInGeneral"));
    }

    [Fact]
    public void Binds_CoversInformationTypeAttributes()
    {
        var s401 = S101FeatureAttributeBindings.ForSpec("S-401");

        Assert.True(s401.Binds("TimeScheduleInGeneral", "typeOfShip"));
        Assert.True(s401.IsSingleValued("TimeScheduleInGeneral", "categoryOfTimeAndBehaviour"));
        Assert.False(s401.Binds("LockBasin", "typeOfShip"));
        Assert.False(s401.DefinesFeatureType("TimeScheduleInGeneral"));
    }
}
