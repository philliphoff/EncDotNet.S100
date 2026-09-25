using EncDotNet.S100.Core;

namespace EncDotNet.S100.Tests;

/// <summary>
/// Exercises <see cref="S100DatasetValidationExtensions.Validate"/>, which runs a
/// product's bundled rule pack against a facade dataset.
/// </summary>
public sealed class S100DatasetValidationTests
{
    private static readonly string TestData = Path.Combine(AppContext.BaseDirectory, "TestData");

    [Fact]
    public void Validate_GmlDataset_ReturnsReport()
    {
        using var dataset = S100Dataset.Open(Path.Combine(TestData, "S124", "navwarn_surface.gml"));

        var report = dataset.Validate();

        Assert.NotNull(report);
    }

    [Fact]
    public async Task Validate_DatasetOpenedFromSource_ReturnsReport()
    {
        using var zip = ZipAssetSource.Create(Path.Combine(TestData, "S101.zip"));
        using var dataset = await S100Dataset.OpenAsync(zip, "S-101/DATASET_FILES/101AA00DS0019.000");

        var report = dataset.Validate();

        Assert.NotNull(report);
        Assert.All(report!.Findings, finding => Assert.False(string.IsNullOrEmpty(finding.RuleId)));
    }

    [Fact]
    public void Validate_IsCached()
    {
        using var dataset = S100Dataset.Open(Path.Combine(TestData, "S124", "navwarn_surface.gml"));

        Assert.Same(dataset.Validate(), dataset.Validate());
    }

    [Fact]
    public void Validate_NullDataset_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((S100Dataset)null!).Validate());
    }

    [Fact]
    public void Validate_DisposedDataset_Throws()
    {
        var dataset = S100Dataset.Open(Path.Combine(TestData, "S124", "navwarn_surface.gml"));
        dataset.Dispose();

        Assert.Throws<ObjectDisposedException>(() => dataset.Validate());
    }
}
