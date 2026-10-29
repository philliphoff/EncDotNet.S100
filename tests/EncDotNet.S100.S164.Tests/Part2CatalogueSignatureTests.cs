using System.Text;
using EncDotNet.S100.Core;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// S-164 Part 2 catalogue signature tests (MissingCatalogueSignature,
/// InvalidCatalogueSignature) on the <c>CATALOG.SIGN</c> every v2.0.0
/// exchange set carries. The scheme administrator certificate behind the
/// v2.0.0 signer isn't published, so these allow untrusted certificates and
/// check the signature against the certificate the file carries.
/// </summary>
public sealed class Part2CatalogueSignatureTests
{
    private static readonly TrustAnchorOptions Untrusted = new() { AllowUntrustedCertificates = true };

    public static TheoryData<string> ExchangeSets() => S164TestData.ExchangeSetNames();

    [Theory]
    [MemberData(nameof(ExchangeSets))]
    public async Task CatalogueSignature_Verifies(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        using var source = ZipAssetSource.Create(await S164TestData.GetExchangeSetAsync(name, ct), "S100_ROOT/");

        var result = await CatalogueSignatureVerifier.VerifyAsync(source, Untrusted, ct);

        Assert.True(result.IsValid, $"{result.Outcome}: {result.Detail}");
    }

    [Fact]
    public async Task InvalidCatalogueSignature_IsReported()
    {
        var ct = TestContext.Current.CancellationToken;
        using IAssetSource inner = ZipAssetSource.Create(await S164TestData.GetExchangeSetAsync("PowerUp", ct), "S100_ROOT/");
        var catalogue = (await inner.ReadAllBytesAsync("CATALOG.XML", ct)).AsString();
        using var source = new ReplacingAssetSource(inner, "CATALOG.XML",
            Encoding.UTF8.GetBytes(catalogue.Replace("PowerUp", "PowerUp2", StringComparison.Ordinal)));

        var result = await CatalogueSignatureVerifier.VerifyAsync(source, Untrusted, ct);

        Assert.Equal(VerificationOutcome.SignatureInvalid, result.Outcome);
    }

    [Fact]
    public async Task MissingCatalogueSignature_IsReported()
    {
        var ct = TestContext.Current.CancellationToken;
        using var inner = ZipAssetSource.Create(await S164TestData.GetExchangeSetAsync("PowerUp", ct), "S100_ROOT/");
        using var source = new ReplacingAssetSource(inner, CatalogueSignatureVerifier.SignatureFileName, null);

        var result = await CatalogueSignatureVerifier.VerifyAsync(source, Untrusted, ct);

        Assert.Equal(VerificationOutcome.NotSigned, result.Outcome);
    }

    /// <summary>Serves one file with other content, or as missing, and the rest from another source.</summary>
    private sealed class ReplacingAssetSource(IAssetSource inner, string path, byte[]? content) : IAssetSource
    {
        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(relativePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return inner.OpenAsync(relativePath, cancellationToken);
            }

            return content is null
                ? throw new FileNotFoundException($"File not found: {relativePath}", relativePath)
                : Task.FromResult<Stream>(new MemoryStream(content, writable: false));
        }

        public void Dispose()
        {
        }
    }
}
