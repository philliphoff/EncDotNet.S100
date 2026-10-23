namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// The finding codes table in docs/how-a-route-check-works.md lists every
/// code in <see cref="RouteFindingCodes.All"/>, in order, with its category
/// and default group.
/// </summary>
public class RouteFindingCodesDocsTests
{
    [Fact]
    public void The_article_lists_every_code_with_its_category_and_group()
    {
        var article = Path.Combine(RepositoryRoot(), "docs", "how-a-route-check-works.md");
        var rows = File.ReadLines(article)
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .Where(cells => cells.Length == 7 && RouteFindingCodes.TryGet(cells[1].Trim('`'), out _))
            .Select(cells => (Code: cells[1].Trim('`'), Category: cells[3], Group: cells[4]))
            .ToList();

        var expected = RouteFindingCodes.All
            .Select(c => (Code: c.Value, Category: c.Category.ToString(), Group: c.DefaultGroup.ToString()))
            .ToList();

        Assert.Equal(expected, rows);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EncDotNet.S100.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("No EncDotNet.S100.slnx above the test output.");
    }
}
