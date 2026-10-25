using EncDotNet.S100.Viewer.Diagnostics;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers replacing the user's home directory with <c>~</c> in log text
/// attached to an exported feedback report.
/// </summary>
public class HomePathRedactorTests
{
    [Fact]
    public void ReplacesHomeDirectory()
    {
        var redacted = HomePathRedactor.Redact(
            "Opened /home/alice/charts/US5MA1SK.000 and /home/alice/.config", "/home/alice");

        Assert.Equal("Opened ~/charts/US5MA1SK.000 and ~/.config", redacted);
    }

    [Fact]
    public void ReplacesForwardSlashSpellingOfWindowsHome()
    {
        var redacted = HomePathRedactor.Redact(
            @"C:\Users\alice\charts and C:/Users/alice/logs", @"C:\Users\alice\");

        Assert.Equal(@"~\charts and ~/logs", redacted);
    }

    [Fact]
    public void DefaultsToCurrentUserProfile()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.SkipWhen(home.Length <= 3, "No usable home directory.");

        var redacted = HomePathRedactor.Redact(Path.Combine(home, "file.txt"));

        Assert.Equal(Path.Combine("~", "file.txt"), redacted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LeavesEmptyTextUnchanged(string? text) =>
        Assert.Equal(text, HomePathRedactor.Redact(text, "/home/alice"));

    [Fact]
    public void IgnoresRootHome() =>
        Assert.Equal("/var/log/x", HomePathRedactor.Redact("/var/log/x", "/"));
}
