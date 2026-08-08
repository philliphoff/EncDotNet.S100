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
