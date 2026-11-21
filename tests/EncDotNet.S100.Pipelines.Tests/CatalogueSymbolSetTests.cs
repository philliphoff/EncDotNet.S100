using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Covers <see cref="CatalogueSymbolSet"/> and
/// <see cref="VectorPortrayalResult.CatalogueSymbols"/>: every catalogue
/// symbol reachable from a portrayal result, not only those the dataset uses
/// (#1514).
/// </summary>
public sealed class CatalogueSymbolSetTests
{
    private const string S101Cell = "tests/datasets/S101/S-101/DATASET_FILES/101AA00DS0019.000";

    [Fact]
    public async Task Names_LoadNothing_AndSymbolsLoadOnlyWhenAskedFor()
    {
        var source = new CountingSource("A", "B", "MISSING");
        var set = new CatalogueSymbolSet(source);

        Assert.Equal(["A", "B", "MISSING"], set.Names);
        Assert.Equal(0, source.Loads);

        var geometry = await set.GetGeometryAsync("B", TestContext.Current.CancellationToken);
        Assert.NotNull(geometry);
        Assert.Equal(1, source.Loads);

        // A symbol the catalogue cannot supply is skipped, not a failure.
        Assert.Null(await set.GetSymbolAsync("MISSING", TestContext.Current.CancellationToken));
        var all = new List<string>();
        await foreach (var symbol in set.GetSymbolsAsync(TestContext.Current.CancellationToken))
            all.Add(symbol.Name);
        Assert.Equal(["A", "B"], all);
    }

    [Fact]
    public async Task S101Result_CoversEveryCatalogueSymbol_WithTheSameGeometryAsTheDrawnOnes()
    {
        var ct = TestContext.Current.CancellationToken;
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var processor = new S101DatasetProcessor(
            RequireFixture(S101Cell), catalogues, TestLuaEngine.Create(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
        var result = await processor.BuildVectorPortrayalAsync(new RenderContext(), ct);

        var expected = await CatalogueSymbolIdsAsync("S-101");
        Assert.True(expected.Count > 500);
        await AssertCompleteAsync(result, expected, ct);
    }

    [Fact]
    public async Task S124Result_CoversEveryCatalogueSymbol()
    {
        var ct = TestContext.Current.CancellationToken;
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-124", Specification.CreatePortrayalCatalogueSource("S-124"));
        var processor = new S124DatasetProcessor(
            Path.Combine(AppContext.BaseDirectory, "TestData", "S124", "navwarn_point.gml"),
            catalogues,
            new DisplayPlaneAuthorityProvider(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
        var result = await processor.BuildVectorPortrayalAsync(new RenderContext(), ct);

        var expected = await CatalogueSymbolIdsAsync("S-124");
        Assert.NotEmpty(expected);
        await AssertCompleteAsync(result, expected, ct);
    }

    /// <summary>
    /// The set names every catalogue symbol, more than the dataset draws; each
    /// loads as geometry matching a fresh parse of its SVG; and a symbol the
    /// dataset does draw is the very record the result's per-symbol geometry
    /// provider hands out.
    /// </summary>
    private static async Task AssertCompleteAsync(
        VectorPortrayalResult result, IReadOnlyList<string> expected, CancellationToken ct)
    {
        var set = result.CatalogueSymbols;
        Assert.NotNull(set);
        Assert.Equal(expected, set.Names);

        var drawn = 0;
        var loaded = 0;
        await foreach (var symbol in set.GetSymbolsAsync(ct))
        {
            loaded++;
            var geometry = symbol.Geometry;
            Assert.NotNull(geometry);
            var fresh = SymbolGeometryParser.TryParse(symbol.SvgContent, symbol.Name);
            Assert.NotNull(fresh);
            Assert.Equal(fresh.ViewBox, geometry.ViewBox);
            Assert.Equal(fresh.Shapes.Count, geometry.Shapes.Count);
            for (var i = 0; i < fresh.Shapes.Count; i++)
            {
                Assert.Equal(fresh.Shapes[i].Paint, geometry.Shapes[i].Paint);
                if (fresh.Shapes[i] is SymbolPath path)
                    Assert.Equal(path.Segments, ((SymbolPath)geometry.Shapes[i]).Segments);
            }

            if (result.SymbolGeometryProvider?.Invoke(symbol.Name) is { } used)
            {
                drawn++;
                Assert.Same(used, geometry);
            }
        }

        Assert.Equal(expected.Count, loaded);
        // The point of the set: symbols the dataset never draws are there too.
        Assert.True(drawn < loaded, $"drawn {drawn} of {loaded}");
    }

    private static async Task<IReadOnlyList<string>> CatalogueSymbolIdsAsync(string product)
    {
        var provider = await PortrayalCatalogueProvider.OpenAsync(
            Specification.CreatePortrayalCatalogueSource(product),
            cancellationToken: TestContext.Current.CancellationToken);
        return provider.Catalogue.Symbols.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string RequireFixture(string repoRelativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, repoRelativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        Assert.SkipWhen(true, $"Fixture not found: {repoRelativePath}");
        throw new InvalidOperationException("Unreachable.");
    }

    private sealed class CountingSource(params string[] names) : IPortrayalAssetSource
    {
        public int Loads { get; private set; }

        public IReadOnlyCollection<string> SymbolNames => names;

        public ValueTask<SvgSymbol> GetSymbolAsync(string symbolName, CancellationToken cancellationToken = default)
        {
            Loads++;
            if (symbolName == "MISSING")
                throw new PortrayalAssetNotFoundException(PortrayalAssetKind.Symbol, symbolName);
            return new(new SvgSymbol
            {
                Name = symbolName,
                SvgContent = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><circle cx=\"0\" cy=\"0\" r=\"1\" class=\"fCHBLK\"/></svg>",
            });
        }

        public ValueTask<LineStyle> GetLineStyleAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<AreaFill> GetAreaFillAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
