namespace EncDotNet.S100.ExchangeSets.Tests;

public class CryptographicHashTests
{
    [Fact]
    public void TryParse_ValidSha256Mrn_Succeeds()
    {
        var mrn = "urn:mrn:iho:s100:hash:sha256:a948904f2f0f479b8f8197694b30184b0d2ed1c1cd2a1ec0fb85d299a192a447";

        Assert.True(CryptographicHash.TryParse(mrn, out var hash));
        Assert.Equal("sha256", hash.Algorithm);
        Assert.Equal("a948904f2f0f479b8f8197694b30184b0d2ed1c1cd2a1ec0fb85d299a192a447", hash.HexValue);
    }

    [Fact]
    public void TryParse_IsCaseInsensitive_AndNormalizesToLower()
    {
        var mrn = "URN:MRN:IHO:S100:HASH:SHA256:ABCD";

        Assert.True(CryptographicHash.TryParse(mrn, out var hash));
        Assert.Equal("sha256", hash.Algorithm);
        Assert.Equal("abcd", hash.HexValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("urn:mrn:iho:s100:dsig:ecdsa:MGUC")]      // signature MRN, not hash
    [InlineData("urn:mrn:iho:s100:hash:sha256:")]          // missing value
    [InlineData("urn:mrn:iho:s100:hash:sha256")]           // missing value separator
    [InlineData("urn:mrn:iho:s100:hash:sha256:xyz")]       // non-hex value
    [InlineData("urn:mrn:iho:s100:hash:sha256:abc")]       // odd-length hex
    public void TryParse_InvalidInput_Fails(string? value)
    {
        Assert.False(CryptographicHash.TryParse(value, out var hash));
        Assert.Null(hash);
    }

    [Fact]
    public void Matches_ComparesHexCaseInsensitively()
    {
        Assert.True(CryptographicHash.TryParse("urn:mrn:iho:s100:hash:sha256:abcd", out var hash));

        Assert.True(hash.Matches("ABCD"));
        Assert.True(hash.Matches("abcd"));
        Assert.False(hash.Matches("ef01"));
        Assert.False(hash.Matches(null));
    }

    [Fact]
    public void ToString_RoundTripsThroughTryParse()
    {
        var original = "urn:mrn:iho:s100:hash:sha256:abcd";
        Assert.True(CryptographicHash.TryParse(original, out var hash));

        Assert.Equal(original, hash.ToString());
    }
}
