using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Covers <see cref="PortrayalAssetNotFoundException"/> and the contract that
/// portrayal catalogues throw it (rather than a bare
/// <see cref="KeyNotFoundException"/>) for a missing named asset — the
/// missing-value policy in <c>docs/design/api-conventions.md</c>.
/// </summary>
public sealed class PortrayalAssetNotFoundExceptionTests
{
    [Theory]
    [InlineData(PortrayalAssetKind.Symbol)]
    [InlineData(PortrayalAssetKind.LineStyle)]
    [InlineData(PortrayalAssetKind.AreaFill)]
    [InlineData(PortrayalAssetKind.Rule)]
    public void Constructor_ExposesKindNameAndMessage(PortrayalAssetKind kind)
    {
        var ex = new PortrayalAssetNotFoundException(kind, "ACHARE51");

        Assert.Equal(kind, ex.AssetKind);
        Assert.Equal("ACHARE51", ex.AssetName);
        Assert.Contains("ACHARE51", ex.Message);
    }

    [Fact]
    public void Constructor_PreservesInnerException()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new PortrayalAssetNotFoundException(PortrayalAssetKind.Symbol, "X", inner);

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void Constructor_NullName_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new PortrayalAssetNotFoundException(PortrayalAssetKind.Symbol, null!));
    }

    [Fact]
    public void IsNotKeyNotFoundException()
    {
        // Deriving from Exception (not KeyNotFoundException) keeps the domain
        // failure out of handlers that guard ordinary dictionary lookups.
        var ex = new PortrayalAssetNotFoundException(PortrayalAssetKind.Symbol, "X");
        Assert.IsNotType<KeyNotFoundException>(ex);
        Assert.IsNotAssignableFrom<KeyNotFoundException>(ex);
    }

    [Fact]
    public async Task Catalogue_MissingSymbol_ThrowsPortrayalAssetNotFound()
    {
        using var source = Specification.CreatePortrayalCatalogueSource("S-101");
        var provider = await PortrayalCatalogueProvider.OpenAsync(source);
        var catalogue = new S101PortrayalCatalogue(provider);

        var ex = await Assert.ThrowsAsync<PortrayalAssetNotFoundException>(
            async () => await catalogue.GetSymbolAsync("NOT_A_REAL_SYMBOL_ZZZ"));

        Assert.Equal(PortrayalAssetKind.Symbol, ex.AssetKind);
        Assert.Equal("NOT_A_REAL_SYMBOL_ZZZ", ex.AssetName);
    }
}
