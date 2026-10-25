using EncDotNet.S100.Viewer.Diagnostics;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Exported reports and logs show the user's home directory as <c>~</c> (#1080).
/// </summary>
public sealed class HomePathRedactorTests
{
    [Theory]
    [InlineData("Could not open /Users/alice/Charts/US5MA1BO.000", "Could not open ~/Charts/US5MA1BO.000")]
    [InlineData("/Users/alice", "~")]
    [InlineData("cwd=\"/Users/alice\" next", "cwd=\"~\" next")]
    [InlineData("a /Users/alice/x and /Users/alice/y", "a ~/x and ~/y")]
    // A longer account name that starts with the same letters is not the home.
    [InlineData("/Users/alicebob/Charts", "/Users/alicebob/Charts")]
    [InlineData("nothing to do", "nothing to do")]
    public void Replaces_the_home_directory_on_unix(string text, string expected) =>
        Assert.Equal(expected, HomePathRedactor.Redact(text, "/Users/alice", ignoreCase: false));

    [Fact]
    public void Matches_windows_paths_with_either_separator_any_case_and_json_escaping()
    {
        const string home = @"C:\Users\Alice";

        Assert.Equal(@"at ~\AppData\Local\Temp\x.zip", HomePathRedactor.Redact(@"at C:\Users\Alice\AppData\Local\Temp\x.zip", home, ignoreCase: true));
        Assert.Equal("at ~/Charts", HomePathRedactor.Redact("at c:/users/alice/Charts", home, ignoreCase: true));
        Assert.Equal(@"{""Path"": ""~\\Charts""}", HomePathRedactor.Redact(@"{""Path"": ""C:\\Users\\Alice\\Charts""}", home, ignoreCase: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    public void Leaves_text_alone_without_a_usable_home(string? home) =>
        Assert.Equal("/x/y", HomePathRedactor.Redact("/x/y", home, ignoreCase: false));

    [Fact]
    public void Redacts_the_current_users_home()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.SkipWhen(string.IsNullOrEmpty(home) || home.TrimEnd('/', '\\').Length < 3, "no home directory");

        Assert.Equal("open ~" + Path.DirectorySeparatorChar + "f", HomePathRedactor.Redact("open " + Path.Combine(home, "f")));
        Assert.Null(HomePathRedactor.RedactOrNull(null));
    }
}
