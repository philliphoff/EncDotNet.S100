using System.Text;
using System.Text.Json;
using EncDotNet.S100.Collections.Manifests;

namespace EncDotNet.S100.Collections.Tests;

public class CollectionManifestTests
{
    private static CollectionManifestDocument Read(string json) =>
        CollectionManifest.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    private static string ErrorOf(string json) =>
        Assert.Throws<CollectionManifestException>(() => Read(json)).Message;

    [Fact]
    public void Reads_a_manifest()
    {
        var manifest = Read("""
            {
              "$schema": "https://example.org/s100collection.schema.json",
              "format": "encdotnet-s100-collection",
              "version": 1,
              "title": "IC-ENC",
              "description": "Test data",
              "groups": [
                { "id": "AU", "name": "Australia", "paths": ["AU"] },
                { "id": "ID", "paths": ["ID/a", "ID/b"], "recursive": false, "description": "Two sets" }
              ]
            }
            """);

        Assert.Equal("IC-ENC", manifest.Title);
        Assert.Equal("Test data", manifest.Description);
        Assert.Equal(["AU", "ID"], manifest.Groups.Select(g => g.Id));
        Assert.Equal("Australia", manifest.Groups[0].DisplayName);
        Assert.True(manifest.Groups[0].Recursive);
        Assert.Equal("ID", manifest.Groups[1].DisplayName);
        Assert.False(manifest.Groups[1].Recursive);
        Assert.Equal(["ID/a", "ID/b"], manifest.Groups[1].Paths);
    }

    [Fact]
    public void Accepts_comments_trailing_commas_and_a_byte_order_mark()
    {
        var json = """
            {
              // hand-written
              "format": "encdotnet-s100-collection",
              "version": 1,
              "groups": [ { "id": "BE", "paths": ["BE",], }, ],
            }
            """;
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).ToArray();

        var manifest = CollectionManifest.Read(new MemoryStream(bytes));

        Assert.Equal("BE", Assert.Single(manifest.Groups).Id);
        Assert.Null(manifest.Title);
    }

    [Fact]
    public void Rejects_another_format()
    {
        Assert.Contains("format", ErrorOf("""{ "format": "encdotnet-s100-feed", "version": 1, "groups": [] }"""));
    }

    [Fact]
    public void Rejects_a_newer_version()
    {
        Assert.Throws<NotSupportedException>(() => Read(
            """{ "format": "encdotnet-s100-collection", "version": 2, "groups": [ { "id": "A", "paths": ["A"] } ] }"""));
    }

    [Theory]
    [InlineData("""{ "format": "encdotnet-s100-collection", "groups": [ { "id": "A", "paths": ["A"] } ] }""", "version")]
    [InlineData("""{ "format": "encdotnet-s100-collection", "version": 1, "groups": [] }""", "groups")]
    [InlineData("""{ "format": "encdotnet-s100-collection", "version": 1, "groups": [ { "paths": ["A"] } ] }""", "groups[0]: 'id' is required")]
    [InlineData("""{ "format": "encdotnet-s100-collection", "version": 1, "groups": [ { "id": "A B", "paths": ["A"] } ] }""", "groups[0].id: 'A B'")]
    [InlineData("""{ "format": "encdotnet-s100-collection", "version": 1, "groups": [ { "id": "A" } ] }""", "groups[0]: 'paths' is required")]
    [InlineData("""{ "format": "encdotnet-s100-collection", "version": 1, "groups": [ { "id": "A", "paths": [" "] } ] }""", "groups[0].paths: 'paths' is required")]
    [InlineData("""{ "format": "encdotnet-s100-collection", "version": 1, "groups": [ { "id": "au", "paths": ["A"] }, { "id": "AU", "paths": ["B"] } ] }""", "groups[1].id: duplicate 'AU' (also groups[0])")]
    public void Errors_say_where(string json, string expected)
    {
        Assert.Contains(expected, ErrorOf(json));
    }

    [Fact]
    public void Problems_carry_lines_and_are_all_reported()
    {
        var ex = Assert.Throws<CollectionManifestException>(() => Read("""
            {
              "format": "encdotnet-s100-collection",
              "version": 1,
              "groups": [
                { "id": "AU", "paths": ["AU"] },
                { "id": "BE",
                  "paths": [] },
                {
                  "id": "au", "paths": ["X"] }
              ]
            }
            """));

        Assert.Equal(
            [
                new CollectionManifestProblem(7, "groups[1].paths", "'paths' is required and must list at least one path."),
                new CollectionManifestProblem(9, "groups[2].id", "duplicate 'au' (also groups[0])."),
            ],
            ex.Problems);
    }

    [Fact]
    public void Syntax_errors_carry_their_line()
    {
        var ex = Assert.Throws<CollectionManifestException>(() => Read("""
            {
              "format": "encdotnet-s100-collection",
              "version": 1
              "groups": []
            }
            """));

        var problem = Assert.Single(ex.Problems);
        Assert.Equal(4, problem.Line);
        Assert.IsAssignableFrom<JsonException>(ex);
    }

    [Fact]
    public void Resolves_relative_absolute_and_backslash_paths()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "manifest-root"));
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "elsewhere"));
        var group = new CollectionManifestGroup("X", null, null, ["AU", @"ID\a", "ID/a/", absolute]);

        var paths = group.ResolvePaths(root);

        Assert.Equal(
            [Path.Combine(root, "AU"), Path.Combine(root, "ID", "a"), absolute],
            paths);
    }

    [Fact]
    public void Recognises_manifests_by_suffix_or_content()
    {
        using var temp = new TempDirectory();
        var bySuffix = Path.Combine(temp.Path, "charts.s100collection.json");
        var byContent = Path.Combine(temp.Path, "charts.json");
        var other = Path.Combine(temp.Path, "feed.json");
        File.WriteAllText(bySuffix, "{}");
        File.WriteAllText(byContent, "﻿{ /* x */ \"title\": \"T\", \"format\": \"encdotnet-s100-collection\" }");
        File.WriteAllText(other, "{ \"format\": \"encdotnet-s100-feed\" }");

        Assert.True(CollectionManifest.IsManifestPath(bySuffix));
        Assert.True(CollectionManifest.IsManifestPath(byContent));
        Assert.False(CollectionManifest.IsManifestPath(other));
        Assert.False(CollectionManifest.IsManifestPath(Path.Combine(temp.Path, "missing.s100collection.json")));
        Assert.False(CollectionManifest.IsManifestPath(temp.Path));
    }

    [Fact]
    public void Summarizes_groups_and_missing_paths()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "AU"));
        var manifest = Read("""
            { "format": "encdotnet-s100-collection", "version": 1, "groups": [
              { "id": "AU", "name": "Australia", "paths": ["AU", "AU/"] },
              { "id": "PE", "paths": ["PE", "AU"] } ] }
            """);

        var summary = CollectionManifest.Summarize(manifest, temp.Path);

        Assert.Equal(new CollectionManifestGroupSummary("AU", "Australia", null, 1, 0), summary[0]);
        Assert.Equal(new CollectionManifestGroupSummary("PE", "PE", null, 2, 1), summary[1]);
    }

    [Fact]
    public void Filter_canonical_form_ignores_order_and_case()
    {
        var a = new LocalManifestFilter { Groups = ["be", "AU"] };
        var b = new LocalManifestFilter { Groups = ["AU", "BE"] };

        Assert.Equal("g=AU,BE", a.ToCanonicalString());
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a.Matches("Au"));
        Assert.False(a.Matches("NL"));
        Assert.True(LocalManifestFilter.All.Matches("NL"));
    }
}
