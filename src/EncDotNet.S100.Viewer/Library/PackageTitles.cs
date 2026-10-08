using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>Cleans community-list package titles; the rule lives in the Library core (#792).</summary>
internal static class PackageTitles
{
    /// <summary>The package's description without its leading timestamp.</summary>
    public static string Clean(string title) => LibraryTextFormat.PackageTitle(title);
}
