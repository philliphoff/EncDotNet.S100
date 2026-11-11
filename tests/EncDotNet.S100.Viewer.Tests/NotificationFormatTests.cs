using EncDotNet.S100.Viewer.Services.Notifications;

namespace EncDotNet.S100.Viewer.Tests;

public class NotificationFormatTests
{
    [Fact]
    public void ShortenPath_File_ReturnsFileName()
    {
        var path = Path.Combine("a", "b", "set.zip");
        Assert.Equal("set.zip", NotificationFormat.ShortenPath(path));
    }

    [Fact]
    public void ShortenPath_Directory_ReturnsDirectoryName()
    {
        var path = Path.Combine("a", "b", "MyExchangeSet");
        Assert.Equal("MyExchangeSet", NotificationFormat.ShortenPath(path));
    }

    [Fact]
    public void ShortenPath_TrailingSeparator_ReturnsDirectoryName()
    {
        var path = Path.Combine("a", "b", "MyExchangeSet") + Path.DirectorySeparatorChar;
        Assert.Equal("MyExchangeSet", NotificationFormat.ShortenPath(path));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void ShortenPath_NullOrWhitespace_ReturnsInputUnchanged(string? input, string expected)
    {
        Assert.Equal(expected, NotificationFormat.ShortenPath(input));
    }

    [Fact]
    public void ShortenPath_BareName_ReturnsItself()
    {
        Assert.Equal("set.zip", NotificationFormat.ShortenPath("set.zip"));
    }
}
