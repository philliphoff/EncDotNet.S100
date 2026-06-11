using EncDotNet.S100.Cli.Infrastructure;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Unit tests for the S-57 exchange-set detection helpers on
/// <see cref="ExchangeSetInput"/>, which let <c>s100 validate</c> recognise an
/// S-57 / S-63 <c>CATALOG.031</c> input and branch into S-57 verification.
/// Synthetic temp directories only — no real ENC data.
/// </summary>
public sealed class S57ExchangeSetInputTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("s57-input-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Directory_with_catalog031_is_detected()
    {
        File.WriteAllBytes(Path.Combine(_root, "CATALOG.031"), []);

        Assert.True(ExchangeSetInput.LooksLikeS57ExchangeSet(_root));
        Assert.Equal(Path.GetFullPath(_root), ExchangeSetInput.ResolveS57Root(_root));
    }

    [Fact]
    public void Catalog031_file_path_is_detected_and_resolves_to_parent()
    {
        string catalogue = Path.Combine(_root, "CATALOG.031");
        File.WriteAllBytes(catalogue, []);

        Assert.True(ExchangeSetInput.LooksLikeS57ExchangeSet(catalogue));
        Assert.Equal(Path.GetFullPath(_root), ExchangeSetInput.ResolveS57Root(catalogue));
    }

    [Fact]
    public void Detection_is_case_insensitive()
    {
        File.WriteAllBytes(Path.Combine(_root, "catalog.031"), []);

        Assert.True(ExchangeSetInput.LooksLikeS57ExchangeSet(_root));
    }

    [Fact]
    public void S100_exchange_set_is_not_an_s57_exchange_set()
    {
        File.WriteAllText(Path.Combine(_root, "CATALOG.XML"), "<x/>");

        Assert.False(ExchangeSetInput.LooksLikeS57ExchangeSet(_root));
        Assert.True(ExchangeSetInput.LooksLikeExchangeSet(_root));
    }

    [Fact]
    public void Empty_directory_is_not_detected()
    {
        Assert.False(ExchangeSetInput.LooksLikeS57ExchangeSet(_root));
    }

    [Fact]
    public void ResolveS57Root_without_catalogue_throws()
    {
        Assert.Throws<FileNotFoundException>(() => ExchangeSetInput.ResolveS57Root(_root));
    }
}
