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
}
