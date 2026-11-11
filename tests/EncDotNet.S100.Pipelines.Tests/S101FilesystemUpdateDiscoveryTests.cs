using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="S101FilesystemUpdateDiscovery"/>, which locates
/// sibling S-101 sequential update files (<c>….001</c>, <c>….002</c>, …)
/// alongside an <c>….000</c> base cell on the local file system (S-100 Part
/// 10a) so a command-line caller can render the cell at its up-to-date state.
/// </summary>
public sealed class S101FilesystemUpdateDiscoveryTests : IDisposable
{
    private readonly string _dir;

    public S101FilesystemUpdateDiscoveryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "s101fsupd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private string Touch(string fileName)
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    [Fact]
    public void FindSequentialUpdates_orders_updates_ascending()
    {
        var basePath = Touch("NL4NZ110.000");
        Touch("NL4NZ110.002");
        Touch("NL4NZ110.001");
        Touch("NL4NZ110.003");

        var updates = S101FilesystemUpdateDiscovery.FindSequentialUpdates(basePath);

        Assert.Equal(
            new[]
            {
                Path.Combine(_dir, "NL4NZ110.001"),
                Path.Combine(_dir, "NL4NZ110.002"),
                Path.Combine(_dir, "NL4NZ110.003"),
            },
            updates);
    }

    [Fact]
    public void FindSequentialUpdates_ignores_other_cells_and_non_numeric_extensions()
    {
        var basePath = Touch("NL4NZ110.000");
        Touch("NL4NZ110.001");
        Touch("OTHER.001");          // different cell
        Touch("NL4NZ110.h5");        // non-numeric extension
        Touch("NL4NZ110.txt");       // unrelated

        var updates = S101FilesystemUpdateDiscovery.FindSequentialUpdates(basePath);

        Assert.Equal(new[] { Path.Combine(_dir, "NL4NZ110.001") }, updates);
    }

    [Fact]
    public void FindSequentialUpdates_returns_empty_when_no_updates_present()
    {
        var basePath = Touch("NL4NZ110.000");

        Assert.Empty(S101FilesystemUpdateDiscovery.FindSequentialUpdates(basePath));
    }

    [Fact]
    public void FindSequentialUpdates_keeps_gaps_for_best_effort_application()
    {
        // A non-contiguous sequence is returned as-is; the applicator is
        // responsible for stopping the chain at the gap with a warning.
        var basePath = Touch("NL4NZ110.000");
        Touch("NL4NZ110.001");
        Touch("NL4NZ110.003");

        var updates = S101FilesystemUpdateDiscovery.FindSequentialUpdates(basePath);

        Assert.Equal(
            new[]
            {
                Path.Combine(_dir, "NL4NZ110.001"),
                Path.Combine(_dir, "NL4NZ110.003"),
            },
            updates);
    }

    [Fact]
    public void FindSequentialUpdates_returns_empty_when_path_is_not_a_base_cell()
    {
        // Pointed at an update file rather than the .000 base.
        var updatePath = Touch("NL4NZ110.001");
        Touch("NL4NZ110.002");

        Assert.Empty(S101FilesystemUpdateDiscovery.FindSequentialUpdates(updatePath));
    }
}
