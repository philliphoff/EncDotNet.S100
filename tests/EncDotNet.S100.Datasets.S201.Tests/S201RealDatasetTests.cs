using System.Linq;

namespace EncDotNet.S100.Datasets.S201.Tests;

/// <summary>
/// Opt-in tests against developer-supplied real-world S-201 datasets.
/// Set the environment variable <c>S201_REAL_DATASET_PATH</c> to a
/// directory (or single .gml file) before running the test suite to
/// enable; otherwise the tests are skipped. Real-world S-201 datasets
/// in the <c>cs0/2.0</c> shape use a unified <c>&lt;members&gt;</c>
/// container and default-namespace feature elements; this fixture
/// guards against silent parse regressions.
/// </summary>
public class S201RealDatasetTests
{
    private const string EnvVar = "S201_REAL_DATASET_PATH";

    [SkippableFact]
    public void Open_RealDataset_ParsesFeatures()
    {
        var path = Environment.GetEnvironmentVariable(EnvVar);
        Skip.If(string.IsNullOrEmpty(path), $"Set {EnvVar} to enable real-dataset tests.");

        var files = Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*.gml", SearchOption.AllDirectories).ToArray()
            : new[] { path };
        Skip.If(files.Length == 0, $"No .gml files found at {path}");

        int totalFeatures = 0;
        int parsedFiles = 0;
        foreach (var file in files)
        {
            // Skip dotfiles / macOS resource forks.
            var name = Path.GetFileName(file);
            if (name.StartsWith("._", StringComparison.Ordinal)) continue;

            var dataset = S201Dataset.Open(file);
            totalFeatures += dataset.Features.Length;
            parsedFiles++;
        }

        Skip.If(parsedFiles == 0, "No parseable files.");
        // At least one of the supplied datasets must contain content.
        Assert.True(totalFeatures > 0,
            $"Parsed {parsedFiles} file(s) but found zero features — reader likely misclassified the dataset shape.");
    }
}
