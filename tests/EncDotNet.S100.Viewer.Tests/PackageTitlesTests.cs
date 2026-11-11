using EncDotNet.S100.Viewer.Library;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class PackageTitlesTests
{
    [Theory]
    [InlineData("23.10.2025 15:17 - New IENCs and bIENCs (269)", "New IENCs and bIENCs (269)")]
    [InlineData("1.2.2024 - Donau", "Donau")]
    [InlineData("Dunărea 790 - 0 (Base1)", "Dunărea 790 - 0 (Base1)")]
    [InlineData("23.10.2025 15:17 - ", "23.10.2025 15:17 - ")]
    public void A_leading_publication_time_is_dropped(string title, string expected)
    {
        Assert.Equal(expected, PackageTitles.Clean(title));
    }
}
