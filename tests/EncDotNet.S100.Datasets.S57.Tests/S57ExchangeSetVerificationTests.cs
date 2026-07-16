using EncDotNet.S100.ExchangeSets;
using EncDotNet.S57.ExchangeSets;
using S57Outcome = EncDotNet.S57.ExchangeSets.S57VerificationOutcome;

namespace EncDotNet.S100.Datasets.S57.Tests;

/// <summary>
/// Unit tests for <see cref="S57ExchangeSetVerification"/>, which maps the
/// upstream S-57 verification result onto this repository's S-100 result model.
/// Uses synthetic S-57 results (the S-57 result types expose <c>init</c>
/// setters) so no real ENC data is required.
/// </summary>
public sealed class S57ExchangeSetVerificationTests
{
    [Fact]
    public void MapOutcome_MapsEveryMemberByName()
    {
        foreach (S57Outcome value in Enum.GetValues<S57Outcome>())
        {
            VerificationOutcome mapped = S57ExchangeSetVerification.MapOutcome(value);
            Assert.Equal(value.ToString(), mapped.ToString());
        }
    }

    [Fact]
    public void MapOutcome_CoversEntireS100Enum()
    {
        // Every S-100 outcome must be reachable from an S-57 outcome of the same
        // name, otherwise the two enumerations have silently diverged.
        var s57Names = Enum.GetNames<S57Outcome>().ToHashSet();
        foreach (string name in Enum.GetNames<VerificationOutcome>())
            Assert.Contains(name, s57Names);
    }

    [Fact]
    public void MapFile_PreservesBothDimensionsIndependently()
    {
        var s57 = new S57FileVerificationResult
        {
            FileName = "US5WA70M\\US5WA70M.000",
            SignatureOutcome = S57Outcome.NotSigned,
            ChecksumOutcome = S57Outcome.Ok,
            ExpectedCrc = "A8292AA7",
            ActualCrc = "A8292AA7",
        };

        FileVerificationResult mapped = S57ExchangeSetVerification.MapFile(s57);

        Assert.Equal("US5WA70M\\US5WA70M.000", mapped.FileName);
        Assert.Equal(VerificationOutcome.NotSigned, mapped.Outcome);
        Assert.Equal(VerificationOutcome.Ok, mapped.ChecksumOutcome);
        Assert.Null(mapped.ComputedSha256);
        Assert.Contains("CRC expected=A8292AA7 actual=A8292AA7", mapped.Detail);
    }

    [Fact]
    public void MapFile_SurfacesCrcMismatchInDetail()
    {
        var s57 = new S57FileVerificationResult
        {
            FileName = "BADCELL.000",
            SignatureOutcome = S57Outcome.NotSigned,
            ChecksumOutcome = S57Outcome.ChecksumMismatch,
            ExpectedCrc = "AAAAAAAA",
            ActualCrc = "BBBBBBBB",
        };

        FileVerificationResult mapped = S57ExchangeSetVerification.MapFile(s57);

        Assert.Equal(VerificationOutcome.ChecksumMismatch, mapped.ChecksumOutcome);
        Assert.Contains("expected=AAAAAAAA", mapped.Detail);
        Assert.Contains("actual=BBBBBBBB", mapped.Detail);
    }

    [Fact]
    public void MapFile_NoCrc_LeavesDetailNull()
    {
        var s57 = new S57FileVerificationResult
        {
            FileName = "CATALOG.031",
            SignatureOutcome = S57Outcome.NotSigned,
            ChecksumOutcome = S57Outcome.NoChecksum,
        };

        FileVerificationResult mapped = S57ExchangeSetVerification.MapFile(s57);

        Assert.Equal(VerificationOutcome.NoChecksum, mapped.ChecksumOutcome);
        Assert.Null(mapped.Detail);
    }

    [Fact]
    public void Map_ProjectsAllFileResults()
    {
        var s57 = new S57ExchangeSetVerificationResult
        {
            FileResults = [
                new S57FileVerificationResult
                {
                    FileName = "CATALOG.031",
                    SignatureOutcome = S57Outcome.NotSigned,
                    ChecksumOutcome = S57Outcome.NoChecksum,
                },
                new S57FileVerificationResult
                {
                    FileName = "CELL.000",
                    SignatureOutcome = S57Outcome.NotSigned,
                    ChecksumOutcome = S57Outcome.Ok,
                    ExpectedCrc = "1234ABCD",
                    ActualCrc = "1234ABCD",
                }],
        };

        ExchangeSetVerificationResult mapped = S57ExchangeSetVerification.Map(s57);

        Assert.Equal(2, mapped.FileResults.Count);
        // An unsigned but intact S-57 set: not AllValid (unsigned) yet integrity
        // is verified (no mismatches / missing files) — matches the documented
        // S-100 semantics and S-57's own AllValid.
        Assert.False(mapped.AllValid);
        Assert.True(mapped.IntegrityVerified);
        Assert.False(mapped.HasChecksumMismatches);
        Assert.False(mapped.HasMissingFiles);
    }

    [Fact]
    public void Map_ChecksumMismatch_FailsIntegrity()
    {
        var s57 = new S57ExchangeSetVerificationResult
        {
            FileResults = [
                new S57FileVerificationResult
                {
                    FileName = "CELL.000",
                    SignatureOutcome = S57Outcome.NotSigned,
                    ChecksumOutcome = S57Outcome.ChecksumMismatch,
                    ExpectedCrc = "AAAA",
                    ActualCrc = "BBBB",
                }],
        };

        ExchangeSetVerificationResult mapped = S57ExchangeSetVerification.Map(s57);

        Assert.True(mapped.HasChecksumMismatches);
        Assert.False(mapped.IntegrityVerified);
    }

    [Fact]
    public async Task VerifyAsync_NullOrEmptyRoot_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => S57ExchangeSetVerification.VerifyAsync(string.Empty));
    }

    /// <summary>
    /// End-to-end verification against a real S-57 exchange set, when one is
    /// available via the <c>ENCDOTNET_S57_EXCHANGE_SET</c> environment variable
    /// (pointing at the folder containing <c>CATALOG.031</c>). Skipped otherwise
    /// so CI never depends on (or commits) real ENC data.
    /// </summary>
    [SkippableFact]
    public async Task VerifyAsync_RealExchangeSet_VerifiesIntegrity()
    {
        string? root = Environment.GetEnvironmentVariable("ENCDOTNET_S57_EXCHANGE_SET");
        Skip.If(string.IsNullOrEmpty(root), "ENCDOTNET_S57_EXCHANGE_SET not set.");
        Skip.IfNot(
            File.Exists(Path.Combine(root!, "CATALOG.031")),
            $"No CATALOG.031 in {root}.");

        ExchangeSetVerificationResult result =
            await S57ExchangeSetVerification.VerifyAsync(root!);

        Assert.NotEmpty(result.FileResults);
        // A pristine, freshly-downloaded set should have no corrupt files.
        Assert.True(result.IntegrityVerified);
    }
}
