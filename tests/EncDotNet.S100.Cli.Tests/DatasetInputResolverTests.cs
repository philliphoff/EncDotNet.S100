using EncDotNet.S100.Cli.Infrastructure;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <see cref="DatasetInputResolver"/>, focused on the <c>specHint</c>
/// path that forces the product spec for single-file loads (the
/// <c>open_dataset</c> <c>spec</c> argument, #560).
/// </summary>
public sealed class DatasetInputResolverTests
{
    [Theory]
    [InlineData("S-102")]
    [InlineData("S102")]
    [InlineData("s-102")]
    [InlineData("s102")]
    public void SpecHint_ForcesAndNormalisesSpec_ForSingleFile(string hint)
    {
        using var file = new TempFile();
        var warnings = new List<string>();

        var inputs = DatasetInputResolver.Resolve(
            file.Path, [], exchangeSet: null, only: null, warnings,
            out var resolution, specHint: hint);
        using var _ = resolution;

        var input = Assert.Single(inputs);
        Assert.Equal("S-102", input.Spec);
        Assert.Empty(warnings);
    }

    [Fact]
    public void SpecHint_LoadsAFileAutoDetectionWouldSkip()
    {
        // A dummy file has no detectable product spec, so without a hint it is
        // skipped; with a hint it is loaded as that spec.
        using var file = new TempFile();
        var warnings = new List<string>();

        var withoutHint = DatasetInputResolver.Resolve(
            file.Path, [], exchangeSet: null, only: null, warnings, out _);
        Assert.Empty(withoutHint);
        Assert.NotEmpty(warnings);

        warnings.Clear();
        var withHint = DatasetInputResolver.Resolve(
            file.Path, [], exchangeSet: null, only: null, warnings, out _, specHint: "S-101");
        Assert.Equal("S-101", Assert.Single(withHint).Spec);
        Assert.Empty(warnings);
    }

    private static string TestData(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, "TestData", .. parts]);

    [Fact]
    public void A_plain_folder_opens_its_loose_datasets_and_exchange_sets_recursively()
    {
        var root = Path.Combine(Path.GetTempPath(), $"encdotnet-folder-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "warnings"));
            File.Copy(TestData("S124", "navwarn_surface.gml"), Path.Combine(root, "warnings", "navwarn_surface.gml"));
            File.Copy(TestData("US5MA1BO.000"), Path.Combine(root, "US5MA1BO.000"));
            File.WriteAllText(Path.Combine(root, "readme.txt"), "not a dataset");
            File.Copy(TestData("S101.zip"), Path.Combine(root, "S101.zip"));
            CopyTree(TestData("ExchangeSet"), Path.Combine(root, "set"));
            var warnings = new List<string>();

            var inputs = DatasetInputResolver.Resolve(root, [], exchangeSet: null, only: null, warnings, out var resolution);
            using var _ = resolution;

            Assert.Contains(inputs, i => i.Id.Value == "US5MA1BO.000" && i.Spec == "S-57");
            Assert.Contains(inputs, i => i.Id.Value == "warnings/navwarn_surface.gml" && i.Spec == "S-124");
            Assert.Contains(inputs, i => i.Id.Value.StartsWith("set/", StringComparison.Ordinal));
            Assert.Contains(warnings, w => w.Contains("S101.zip", StringComparison.Ordinal));
            Assert.DoesNotContain(inputs, i => i.Path.EndsWith("readme.txt", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void An_exchange_set_and_layers_combine()
    {
        var warnings = new List<string>();

        var inputs = DatasetInputResolver.Resolve(
            null, [TestData("US5MA1BO.000")], exchangeSet: TestData("ExchangeSet"), only: null, warnings, out var resolution);
        using var _ = resolution;

        Assert.True(inputs.Count > 1);
        Assert.Equal("US5MA1BO.000", inputs[^1].Id.Value);
    }

    private static void CopyTree(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"encdotnet-resolve-{Guid.NewGuid():N}.dat");

        public TempFile() => File.WriteAllText(Path, "not-a-real-dataset");

        public void Dispose()
        {
            try { File.Delete(Path); } catch { /* best-effort */ }
        }
    }
}
