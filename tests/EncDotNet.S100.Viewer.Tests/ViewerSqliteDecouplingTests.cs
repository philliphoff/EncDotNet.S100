using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Guards that SQLite stays a dependency of the <c>s100</c> CLI alone (issue
/// #858): the MBTiles writer lives in the CLI, and SoundCharts must not
/// reference SQLite or its native bundle, even though it ships a copy of the
/// CLI in its <c>cli/</c> folder.
/// </summary>
public class ViewerSqliteDecouplingTests
{
    [Fact]
    public void Viewer_assembly_has_no_SQLite_in_its_reference_closure()
    {
        var viewerPath = Path.Combine(AppContext.BaseDirectory, "SoundCharts.dll");
        Assert.True(File.Exists(viewerPath), $"Expected viewer assembly at {viewerPath}");

        var offenders = MapsuiDependencyClosure.FindReferences(viewerPath, "Microsoft.Data.Sqlite", "SQLitePCL");

        Assert.True(
            offenders.Count == 0,
            $"SoundCharts must not reference SQLite, but its closure reaches: {string.Join(", ", offenders)}");
    }
}
