namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// The finding code tables in docs/route-checks.md list every code in
/// <see cref="RouteFindingCodes.All"/> once, each with its default group.
/// </summary>
public class RouteFindingCodesDocsTests
{
    [Fact]
    public void The_route_checks_page_lists_every_code_once_with_its_group()
    {
        var page = Path.Combine(RepositoryRoot(), "docs", "route-checks.md");
        var rows = File.ReadLines(page)
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .Where(cells => cells.Length == 5 && RouteFindingCodes.TryGet(cells[1].Trim('`'), out _))
            .Select(cells => (Code: cells[1].Trim('`'), Group: cells[2]))
            .ToList();

        Assert.Equal(
            RouteFindingCodes.All.Select(c => c.Value).Order(StringComparer.Ordinal),
            rows.Select(r => r.Code).Order(StringComparer.Ordinal));
        Assert.All(rows, row =>
        {
            RouteFindingCodes.TryGet(row.Code, out var code);
            Assert.StartsWith(GroupText(code!.DefaultGroup), row.Group, StringComparison.Ordinal);
        });
    }

    private static string GroupText(RouteFindingGroup group) => group switch
    {
        RouteFindingGroup.MustReview => "Must review",
        _ => group.ToString(),
    };

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
