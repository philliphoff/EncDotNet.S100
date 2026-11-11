using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for <see cref="ApplicationContentScope"/>, the tile disk-cache scope
/// of a portrayal defined in code (#1066, #1014).
/// </summary>
public sealed class ApplicationContentScopeTests
{
    [Fact]
    public void Scope_names_the_style_its_version_and_the_toolkit_version()
    {
        Assert.Equal(
            $"app:Weather@3|toolkit:{ApplicationContentScope.ToolkitVersion}",
            ApplicationContentScope.For("Weather", 3));
        Assert.False(string.IsNullOrWhiteSpace(ApplicationContentScope.ToolkitVersion));
    }

    [Fact]
    public void A_new_style_version_or_name_is_a_new_scope()
    {
        Assert.Equal(ApplicationContentScope.For("Weather", 1), ApplicationContentScope.For("Weather", 1));
        Assert.NotEqual(ApplicationContentScope.For("Weather", 1), ApplicationContentScope.For("Weather", 2));
        Assert.NotEqual(ApplicationContentScope.For("Weather", 1), ApplicationContentScope.For("CAP", 1));
    }

    [Fact]
    public void Versions_start_at_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ApplicationContentScope.For("Weather", 0));
        Assert.Throws<ArgumentException>(() => ApplicationContentScope.For(" ", 1));
    }
}
