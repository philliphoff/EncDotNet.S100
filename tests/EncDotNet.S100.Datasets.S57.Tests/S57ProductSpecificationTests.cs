namespace EncDotNet.S100.Datasets.S57.Tests;

/// <summary>
/// Tests for <see cref="S57ProductSpecification.TryParse"/>, which reads the
/// <c>DSID</c>/<c>PRSP</c> subfield an S-57 cell uses to declare its product
/// specification — the value that tells an inland ENC (<c>10</c>) from a
/// maritime one (<c>1</c>).
/// </summary>
public class S57ProductSpecificationTests
{
    [Theory]
    [InlineData("1", S57ProductSpecification.ElectronicNavigationalChart)]
    [InlineData("2", S57ProductSpecification.ObjectCatalogueDataDictionary)]
    [InlineData("10", S57ProductSpecification.InlandElectronicNavigationalChart)]
    [InlineData(" 10 ", S57ProductSpecification.InlandElectronicNavigationalChart)]
    public void TryParse_BinarySubfieldDigits_ReturnsCode(string value, int expected)
    {
        Assert.True(S57ProductSpecification.TryParse(value, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ENC")]
    [InlineData("INT.IHO.S-401.1.2")]
    [InlineData("-1")]
    public void TryParse_NonNumericOrSigned_ReturnsFalse(string? value)
    {
        Assert.False(S57ProductSpecification.TryParse(value, out _));
    }
}
