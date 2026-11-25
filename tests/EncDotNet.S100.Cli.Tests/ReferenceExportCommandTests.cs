using System.Text.RegularExpressions;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Reference;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// The S-101 chart reference, exported three times: twice with the committed
/// probe results, to check the output is deterministic, and once without.
/// </summary>
public sealed class ReferenceExportFixture : IDisposable
{
    public ReferenceExportFixture()
    {
        Root = Directory.CreateTempSubdirectory("reference-export-").FullName;
        WithProbes = Export("probes", []);
        Again = Export("again", []);
        WithoutProbes = Export("no-probes", ["--no-probes"]);
    }

    public string Root { get; }

    public string WithProbes { get; }

    public string Again { get; }

    public string WithoutProbes { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Export(string name, string[] extra)
    {
        var output = Path.Combine(Root, name);
        var exit = CliApp.Build().Run(
            ["reference", "export", "--spec", "S-101", "--palette", "day", "--out", output, .. extra],
            TestContext.Current.CancellationToken);
        Assert.Equal(0, exit);
        return output;
    }
}

/// <summary>
/// <c>s100 reference export</c> (issue #1723): the chart reference as a
/// static, offline HTML site.
/// </summary>
public sealed partial class ReferenceExportCommandTests(ReferenceExportFixture fixture) : IClassFixture<ReferenceExportFixture>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Export_writes_the_index_and_the_entry_pages(bool probes)
    {
        var root = probes ? fixture.WithProbes : fixture.WithoutProbes;

        Assert.True(File.Exists(Path.Combine(root, "index.html")));
        Assert.True(File.Exists(Path.Combine(root, "feature-types", "LateralBeacon.html")));
        Assert.True(File.Exists(Path.Combine(root, "symbols", "BCNLAT21.html")));
        Assert.True(File.Exists(Path.Combine(root, "attributes", "colour.html")));
        Assert.True(File.Exists(Path.Combine(root, "display-settings", "SimplifiedSymbols.html")));
        Assert.True(File.Exists(Path.Combine(root, "glyphs", "day", "symbols", "BCNLAT21.png")));
        Assert.True(File.Exists(Path.Combine(root, "glyphs", "night", "symbols", "BCNLAT21.png")));

        var index = File.ReadAllText(Path.Combine(root, "index.html"));
        Assert.Contains("id=\"search\"", index, StringComparison.Ordinal);
        Assert.Contains("href=\"feature-types/LateralBeacon.html\"", index, StringComparison.Ordinal);
        Assert.Contains("BCNLAT", index, StringComparison.Ordinal);

        // Drawn with, in words, either way.
        var beacon = File.ReadAllText(Path.Combine(root, "feature-types", "LateralBeacon.html"));
        Assert.Contains("Drawn with", beacon, StringComparison.Ordinal);
        Assert.Contains("href=\"../symbols/BCNLAT21.html\"", beacon, StringComparison.Ordinal);
        Assert.Contains(">simplified symbols</a> is on", beacon, StringComparison.Ordinal);

        var symbol = File.ReadAllText(Path.Combine(root, "symbols", "BCNLAT21.html"));
        Assert.Contains("Used by", symbol, StringComparison.Ordinal);
        Assert.Contains("href=\"../feature-types/LateralBeacon.html\"", symbol, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_results_add_variant_tables_value_columns_and_before_after_pairs()
    {
        var beacon = File.ReadAllText(Path.Combine(fixture.WithProbes, "feature-types", "LateralBeacon.html"));
        Assert.Contains("How it is drawn", beacon, StringComparison.Ordinal);
        Assert.Contains("class=\"variants\"", beacon, StringComparison.Ordinal);

        var colour = File.ReadAllText(Path.Combine(fixture.WithProbes, "attributes", "colour.html"));
        Assert.Contains("Symbols by value", colour, StringComparison.Ordinal);

        var simplified = File.ReadAllText(Path.Combine(fixture.WithProbes, "display-settings", "SimplifiedSymbols.html"));
        Assert.Contains("Before and after", simplified, StringComparison.Ordinal);

        foreach (var (file, section) in new[]
        {
            (Path.Combine("feature-types", "LateralBeacon.html"), "How it is drawn"),
            (Path.Combine("attributes", "colour.html"), "Symbols by value"),
            (Path.Combine("display-settings", "SimplifiedSymbols.html"), "Before and after"),
        })
        {
            Assert.DoesNotContain(section, File.ReadAllText(Path.Combine(fixture.WithoutProbes, file)), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Partial_feature_types_are_marked()
    {
        // Tests run inside the repository, next to the committed results.
        var set = EncDotNet.S100.Datasets.Pipelines.Reference.RuleProbeStore.ReadFile(ReferenceProbeCommand.DefaultOutput("S-101")!);
        var partial = set.FeatureTypes.FirstOrDefault(f => f.IsPartial);
        Assert.SkipWhen(partial is null, "The committed probe results have no partial feature type.");

        var page = File.ReadAllText(Path.Combine(fixture.WithProbes, "feature-types", ReferenceSite.FileName(partial!.Code) + ".html"));
        Assert.Contains("class=\"badge\"", page, StringComparison.Ordinal);
        Assert.Contains("Partial", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_internal_link_resolves(bool probes)
    {
        var root = probes ? fixture.WithProbes : fixture.WithoutProbes;
        var ids = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var broken = new List<string>();
        var links = 0;

        foreach (var page in Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories))
        {
            var html = File.ReadAllText(page);
            foreach (Match link in LinkPattern().Matches(html))
            {
                var target = System.Net.WebUtility.HtmlDecode(link.Groups["url"].Value);
                Assert.DoesNotMatch("^(https?:)?//", target);
                links++;

                var hash = target.IndexOf('#', StringComparison.Ordinal);
                var (path, fragment) = hash < 0 ? (target, null) : (target[..hash], target[(hash + 1)..]);
                var file = path.Length == 0 ? page : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(page)!, path));
                if (!File.Exists(file))
                {
                    broken.Add($"{Path.GetRelativePath(root, page)} → {target}");
                    continue;
                }

                if (fragment is { Length: > 0 } && !fragment.StartsWith("q=", StringComparison.Ordinal))
                {
                    if (!ids.TryGetValue(file, out var known))
                        ids[file] = known = [.. IdPattern().Matches(File.ReadAllText(file)).Select(m => m.Groups["id"].Value)];
                    if (!known.Contains(fragment))
                        broken.Add($"{Path.GetRelativePath(root, page)} → {target} (no #{fragment})");
                }
            }
        }

        Assert.True(links > 10_000, $"Only {links} links checked.");
        Assert.True(broken.Count == 0, $"{broken.Count} broken links, such as:\n{string.Join('\n', broken.Take(20))}");
    }

    [Fact]
    public void The_site_needs_no_network()
    {
        foreach (var file in Directory.EnumerateFiles(fixture.WithProbes, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".html", StringComparison.Ordinal) || f.EndsWith(".css", StringComparison.Ordinal) || f.EndsWith(".js", StringComparison.Ordinal)))
        {
            // Catalogue text may name a web address; nothing the page loads may.
            var text = File.ReadAllText(file);
            Assert.DoesNotMatch("(?:src|href)=\"(?:[a-z]+:)?//", text);
            Assert.DoesNotContain("url(", text, StringComparison.Ordinal);
            Assert.DoesNotContain("@import", text, StringComparison.Ordinal);
            Assert.DoesNotContain("@font-face", text, StringComparison.Ordinal);
            Assert.DoesNotContain("fetch(", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_second_run_writes_the_same_bytes()
    {
        var first = Files(fixture.WithProbes);
        var second = Files(fixture.Again);

        Assert.Equal(first, second);
        foreach (var file in first)
        {
            Assert.True(
                File.ReadAllBytes(Path.Combine(fixture.WithProbes, file)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(fixture.Again, file))),
                $"{file} differs between runs.");
        }
    }

    [Fact]
    public void An_earlier_export_is_replaced_but_another_folder_is_left_alone()
    {
        var root = Directory.CreateTempSubdirectory("reference-export-out-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "notes.txt"), "mine");
            Assert.NotNull(ReferenceExportCommand.PrepareOutput(root));
            Assert.True(File.Exists(Path.Combine(root, "notes.txt")));

            File.Delete(Path.Combine(root, "notes.txt"));
            File.WriteAllText(Path.Combine(root, ReferenceSite.Marker), "export");
            Directory.CreateDirectory(Path.Combine(root, "symbols"));
            File.WriteAllText(Path.Combine(root, "symbols", "OLD.html"), "stale");
            Assert.Null(ReferenceExportCommand.PrepareOutput(root));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("--palette", "sepia")]
    [InlineData("--catalogue", "/no/such/catalogue")]
    public void Export_refuses_bad_options(string option, string value)
    {
        var output = Path.Combine(fixture.Root, "refused-" + value.GetHashCode(StringComparison.Ordinal).ToString("x8", System.Globalization.CultureInfo.InvariantCulture));
        var exit = CliApp.Build().Run(
            ["reference", "export", "--out", output, option, value],
            TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exit);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("LateralBeacon", "LateralBeacon")]
    [InlineData("SOUNDG", "SOUNDG")]
    [InlineData("a/b", "a_b-")]
    [InlineData(".hidden", "_hidden-")]
    public void File_names_are_safe_and_stable(string code, string start)
    {
        var name = ReferenceSite.FileName(code);
        Assert.StartsWith(start, name, StringComparison.Ordinal);
        Assert.Equal(name, ReferenceSite.FileName(code));
        Assert.Matches("^[A-Za-z0-9._-]+$", name);
    }

    private static List<string> Files(string root) =>
    [
        .. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal),
    ];

    [GeneratedRegex("(?:href|src)=\"(?<url>[^\"]*)\"")]
    private static partial Regex LinkPattern();

    [GeneratedRegex("\\bid=\"(?<id>[^\"]*)\"")]
    private static partial Regex IdPattern();
}
