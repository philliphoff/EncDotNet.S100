using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// End-to-end coverage that every committed GML sample dataset is routed to the
/// correct product specification by the per-registration
/// <see cref="ProductRegistration.MatchGml"/> recognizers that back
/// <see cref="DatasetPipelineFactory"/>'s GML detection. This exercises the real
/// namespaces / <c>productIdentifier</c> shapes of all ten GML products (rather
/// than a single hand-written sample), so a regression in any one product's
/// recognizer is caught. The <c>tests/datasets/&lt;folder&gt;</c> layout names
/// each fixture's product, giving the expected spec.
/// </summary>
public class DatasetPipelineFactoryGmlDetectionTests
{
    // Folder under tests/datasets → canonical spec the fixtures declare.
    public static TheoryData<string, string> GmlFixtures()
    {
        var map = new (string Folder, string Spec)[]
        {
            ("S122", "S-122"), ("S124", "S-124"), ("S125", "S-125"),
            ("S127", "S-127"), ("S128", "S-128"), ("S129", "S-129"),
            ("S131", "S-131"), ("S201", "S-201"), ("S411", "S-411"),
            ("S421", "S-421"),
        };

        var data = new TheoryData<string, string>();
        foreach (var (folder, spec) in map)
        {
            // These fixtures are committed to the repo, so a missing or empty
            // folder is a real problem — fail loudly rather than silently
            // dropping a product's detection coverage.
            var dir = ResolveDatasetsDirectory(folder)
                ?? throw new InvalidOperationException(
                    $"Committed GML fixture folder 'tests/datasets/{folder}' was not found; " +
                    "GML detection coverage requires it.");

            var gmls = Directory.EnumerateFiles(dir, "*.gml").ToList();
            if (gmls.Count == 0)
                throw new InvalidOperationException(
                    $"Committed GML fixture folder 'tests/datasets/{folder}' contains no .gml files.");

            foreach (var gml in gmls)
                data.Add(gml, spec);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(GmlFixtures))]
    public async Task DetectProductSpecFromSource_RoutesSampleToDeclaredProduct(
        string gmlPath, string expectedSpec)
    {
        var directory = Path.GetDirectoryName(gmlPath)!;
        using var source = FileSystemAssetSource.Create(directory);

        var spec = await DatasetPipelineFactory.DetectProductSpecFromSourceAsync(
            source, Path.GetFileName(gmlPath), TestContext.Current.CancellationToken);

        Assert.Equal(expectedSpec, spec);
    }

    [Theory]
    [MemberData(nameof(GmlFixtures))]
    public void DetectProductSpec_RoutesSampleToDeclaredProduct(string gmlPath, string expectedSpec)
    {
        Assert.Equal(expectedSpec, DatasetPipelineFactory.DetectProductSpec(gmlPath));
    }

    // Folder under tests/datasets → canonical spec of every committed HDF5
    // coverage fixture (S-100 Part 10c productSpecification).
    public static TheoryData<string, string> Hdf5Fixtures()
    {
        var data = new TheoryData<string, string>();
        foreach (var (folder, spec) in new[] { ("S102", "S-102"), ("S104", "S-104"), ("S111", "S-111") })
        {
            var dir = ResolveDatasetsDirectory(folder)
                ?? throw new InvalidOperationException(
                    $"Committed HDF5 fixture folder 'tests/datasets/{folder}' was not found.");
            foreach (var h5 in Directory.EnumerateFiles(dir, "*.h5"))
                data.Add(h5, spec);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Hdf5Fixtures))]
    public async Task Hdf5Fixtures_AreClaimedByTheirProduct(string h5Path, string expectedSpec)
    {
        // Issue #1061: the HDF5 products claim their own files (MatchHdf5); there
        // is no S-102 fallback, so each committed fixture must still be claimed.
        Assert.Equal(expectedSpec, DatasetPipelineFactory.DetectProductSpec(h5Path));

        using var source = FileSystemAssetSource.Create(Path.GetDirectoryName(h5Path)!);
        Assert.Equal(
            expectedSpec,
            await DatasetPipelineFactory.DetectProductSpecFromSourceAsync(
                source, Path.GetFileName(h5Path), TestContext.Current.CancellationToken));
    }

    /// <summary>Walks up from the test assembly to the committed fixture folder.</summary>
    private static string? ResolveDatasetsDirectory(string folder)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", folder);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
