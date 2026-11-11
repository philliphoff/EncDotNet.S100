namespace EncDotNet.S100.Viewer.Tests;

public class ShortcutTextTests
{
    [Theory]
    [InlineData("E", false, true, "⌘E")]
    [InlineData("Z", true, true, "⇧⌘Z")]
    [InlineData("E", false, false, "Ctrl+E")]
    [InlineData("Z", true, false, "Ctrl+Shift+Z")]
    public void Command_writes_the_platform_shortcut(string key, bool shift, bool macOS, string expected)
        => Assert.Equal(expected, ShortcutText.Command(key, shift, macOS));
}
