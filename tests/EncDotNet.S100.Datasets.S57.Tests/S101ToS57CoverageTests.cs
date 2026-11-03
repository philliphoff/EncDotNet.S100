using System.Text;

namespace EncDotNet.S100.Datasets.S57.Tests;

/// <summary>
/// Every S-65 Annex C clause has a rule or is a documented drop, and the
/// coverage table in the <c>EncDotNet.S100.Datasets.S57</c> README matches
/// <see cref="S101ToS57Coverage"/>.
/// </summary>
public class S101ToS57CoverageTests
{
    private const string Start = "<!-- annex-c-coverage:start -->";
    private const string End = "<!-- annex-c-coverage:end -->";

    [Fact]
    public void EveryAnnexCClause_IsCovered()
    {
        var uncovered = S101ToS57Coverage.Clauses.Where(c => c.Treatment == "uncovered").Select(c => $"{c.Clause} {c.S101Code}");
        Assert.Empty(uncovered);
    }

    /// <summary>
    /// Set <c>ENCDOTNET_UPDATE_README=1</c> to rewrite the table instead of
    /// checking it.
    /// </summary>
    [Fact]
    public void Readme_CoverageTable_IsCurrent()
    {
        var path = ReadmePath();
        var readme = File.ReadAllText(path).ReplaceLineEndings("\n");
        int start = readme.IndexOf(Start, StringComparison.Ordinal);
        int end = readme.IndexOf(End, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"The README has no {Start} … {End} section.");

        var expected = Table();
        var actual = readme[(start + Start.Length)..end];
        if (Environment.GetEnvironmentVariable("ENCDOTNET_UPDATE_README") == "1")
        {
            File.WriteAllText(path, readme[..(start + Start.Length)] + expected + readme[end..]);
            return;
        }
        Assert.Equal(expected, actual);
    }

    private static string Table()
    {
        var sections = new (string Title, string[] Kinds)[]
        {
            ("Feature types and general clauses (§3–§23)", ["feature", "general"]),
            ("Information types and associations (§24–§25)", ["information", "association"]),
            ("Simple attributes (§27, §28, §30)", ["attribute"]),
            ("Complex attributes (§29)", ["complex"]),
        };
        var text = new StringBuilder("\n");
        foreach (var (title, kinds) in sections)
        {
            var rows = S101ToS57Coverage.Clauses.Where(c => kinds.Contains(c.Kind)).ToList();
            text.Append("<details>\n<summary>").Append(title).Append(": ").Append(rows.Count).Append(" clauses</summary>\n\n");
            text.Append("| Clause | S-101 | Treatment | S-57 |\n|---|---|---|---|\n");
            foreach (var row in rows)
                text.Append("| ").Append(row.Clause).Append(" | ").Append(row.S101Code).Append(" | ").Append(row.Treatment)
                    .Append(" | ").Append(row.Target.Replace("|", "\\|", StringComparison.Ordinal)).Append(" |\n");
            text.Append("\n</details>\n\n");
        }
        return text.ToString();
    }

    private static string ReadmePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "EncDotNet.S100.Datasets.S57", "README.md");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("EncDotNet.S100.Datasets.S57/README.md not found above the test output folder.");
    }
}
