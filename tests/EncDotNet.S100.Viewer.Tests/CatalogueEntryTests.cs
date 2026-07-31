using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Verifies the inline metadata projected by <see cref="CatalogueEntry"/> and
/// the curated spec-title lookup in <see cref="Strings.SpecDisplayName"/> that
/// the Feature- and Portrayal-catalogue panels display.
/// </summary>
public class CatalogueEntryTests
{
    [Theory]
    [InlineData("S-101", "Electronic Navigational Chart")]
    [InlineData("S-104", "Water Level Information for Surface Navigation")]
    [InlineData("S-421", "Route Plan")]
    public void SpecDisplayName_returns_curated_title_for_known_spec(string spec, string expected)
    {
        Assert.Equal(expected, Strings.SpecDisplayName(spec));
    }

    [Theory]
    [InlineData("S-999")]
    [InlineData("")]
    public void SpecDisplayName_returns_null_for_unknown_or_empty_spec(string spec)
    {
        Assert.Null(Strings.SpecDisplayName(spec));
    }

    [Fact]
    public void Subtitle_contains_only_version_and_date()
    {
        var entry = new CatalogueEntry(
            "S-101",
            "S-101 Electronic Navigational Chart",
            "/tmp/fc.xml",
            version: "1.0.0",
            versionDate: "2023-01-01");

        Assert.Equal("v1.0.0 · 2023-01-01", entry.Subtitle);
        Assert.True(entry.HasSubtitle);
        Assert.True(entry.ShowPath);
    }

    [Fact]
    public void Subtitle_appends_built_in_marker_and_path_is_hidden_for_built_ins()
    {
        var entry = new CatalogueEntry(
            "S-101",
            "S-101 Electronic Navigational Chart",
            Strings.Catalogue_BuiltInLabel,
            isBuiltIn: true,
            version: "1.0.0",
            versionDate: "2023-01-01");

        Assert.Equal($"v1.0.0 · 2023-01-01 · {Strings.Catalogue_BuiltInLabel}", entry.Subtitle);
        Assert.False(entry.ShowPath);
    }

    [Fact]
    public void ComposeTitle_prefixes_spec_code_before_name()
    {
        Assert.Equal(
            "S-101 Electronic Navigational Chart",
            FeatureCataloguesViewModel.ComposeTitle("S-101", "Electronic Navigational Chart"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ComposeTitle_falls_back_to_spec_code_when_name_missing(string? name)
    {
        Assert.Equal("S-999", FeatureCataloguesViewModel.ComposeTitle("S-999", name));
    }

    [Fact]
    public void Subtitle_is_empty_when_no_secondary_metadata_is_available()
    {
        var entry = new CatalogueEntry("S-999", "S-999", "/tmp/fc.xml");

        Assert.Equal(string.Empty, entry.Subtitle);
        Assert.False(entry.HasSubtitle);
    }
}
