namespace EncDotNet.S100.Collections.Tests;

/// <summary>Locates the repository's committed sample datasets and this project's fixtures.</summary>
internal static class TestPaths
{
    /// <summary>The repository's <c>tests/datasets</c> folder.</summary>
    public static string Datasets { get; } = FindDatasets();

    /// <summary>Resolves a path under <c>tests/datasets</c>.</summary>
    public static string Dataset(params string[] parts) =>
        Path.Combine([Datasets, .. parts]);

    /// <summary>Resolves a file copied from this project's <c>Fixtures</c> folder.</summary>
    public static string Fixture(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static string FindDatasets()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets");
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate tests/datasets.");
    }
}

/// <summary>A temporary directory deleted on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "collections-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Copies a directory tree into this directory under <paramref name="name"/>.</summary>
    public string CopyTree(string source, string name)
    {
        var target = System.IO.Path.Combine(Path, name);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(System.IO.Path.Combine(target, System.IO.Path.GetRelativePath(source, dir)));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, System.IO.Path.Combine(target, System.IO.Path.GetRelativePath(source, file)));
        return target;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
