using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Unit tests for <see cref="S100ExaminerLinkBuilder"/> — the deep-link
/// builder for the S-100 Feature Catalogue eXaminer (issue #442).
/// </summary>
public class S100ExaminerLinkBuilderTests
{
    private static ViewerSettings NewSettings(
        bool enabled = true,
        string? baseUrl = ViewerSettings.DefaultS100ExaminerBaseUrl)
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        return new ViewerSettings
        {
            SettingsFilePath = path,
            S100ExaminerLinksEnabled = enabled,
            S100ExaminerBaseUrl = baseUrl!,
        };
    }

    [Fact]
    public void Catalogue_url_uses_catalog_query()
    {
        var builder = new S100ExaminerLinkBuilder(NewSettings());
        var url = builder.BuildCatalogueUrl("S-101");
        Assert.Equal("https://s100examiner.com/?catalog=S-101", url);
    }

    [Fact]
    public void Feature_url_adds_feature_query()
    {
        var builder = new S100ExaminerLinkBuilder(NewSettings());
        var url = builder.BuildFeatureUrl("S-101", "Canal");
        Assert.Equal("https://s100examiner.com/?catalog=S-101&feature=Canal", url);
    }

    [Fact]
    public void Attribute_url_adds_feature_and_attribute_queries()
    {
        var builder = new S100ExaminerLinkBuilder(NewSettings());
        var url = builder.BuildAttributeUrl("S-101", "Building", "colour");
        Assert.Equal("https://s100examiner.com/?catalog=S-101&feature=Building&attribute=colour", url);
    }

    [Fact]
    public void Attribute_url_without_feature_still_links()
    {
        var builder = new S100ExaminerLinkBuilder(NewSettings());
        var url = builder.BuildAttributeUrl("S-101", featureCode: null, "colour");
        Assert.Equal("https://s100examiner.com/?catalog=S-101&attribute=colour", url);
    }

    [Fact]
    public void Values_are_url_encoded()
    {
        var builder = new S100ExaminerLinkBuilder(NewSettings());
        var url = builder.BuildFeatureUrl("S-101", "Depth Area");
        Assert.Equal("https://s100examiner.com/?catalog=S-101&feature=Depth%20Area", url);
    }

    [Fact]
    public void Unsupported_spec_returns_null()
    {
        var builder = new S100ExaminerLinkBuilder(NewSettings());
        Assert.Null(builder.BuildCatalogueUrl("S-421"));
        Assert.False(builder.SupportsSpec("S-421"));
    }

    [Fact]
    public void Disabled_returns_null()
    {
        var builder = new S100ExaminerLinkBuilder(NewSettings(enabled: false));
        Assert.Null(builder.BuildCatalogueUrl("S-101"));
        Assert.False(builder.IsEnabled);
        Assert.False(builder.SupportsSpec("S-101"));
    }

    [Fact]
    public void Empty_feature_code_returns_null()
    {
        var builder = new S100ExaminerLinkBuilder(NewSettings());
        Assert.Null(builder.BuildFeatureUrl("S-101", "  "));
    }

    [Fact]
    public void Empty_attribute_code_returns_null()
    {
        var builder = new S100ExaminerLinkBuilder(NewSettings());
        Assert.Null(builder.BuildAttributeUrl("S-101", "Building", ""));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/")]
    [InlineData("")]
    public void Malformed_base_url_returns_null(string baseUrl)
    {
        var builder = new S100ExaminerLinkBuilder(NewSettings(baseUrl: baseUrl));
        Assert.Null(builder.BuildCatalogueUrl("S-101"));
        Assert.False(builder.IsEnabled);
    }

    [Fact]
    public void Spec_match_is_case_insensitive()
    {
        var builder = new S100ExaminerLinkBuilder(NewSettings());
        Assert.True(builder.SupportsSpec("s-101"));
    }

    [Fact]
    public void Custom_base_url_is_honoured()
    {
        var builder = new S100ExaminerLinkBuilder(NewSettings(baseUrl: "https://mirror.example.org/examiner/"));
        var url = builder.BuildFeatureUrl("S-101", "Canal");
        Assert.Equal("https://mirror.example.org/examiner/?catalog=S-101&feature=Canal", url);
    }
}
