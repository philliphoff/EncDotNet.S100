using System.Text.RegularExpressions;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Display helpers for community-list package titles, which some lists prefix
/// with their publication time ("23.10.2025 15:17 - New IENCs and bIENCs
/// (269)"). The time is shown separately ("published …"), so the name drops it.
/// </summary>
internal static partial class PackageTitles
{
    /// <summary>The title without a leading "dd.MM.yyyy[ HH:mm] - " timestamp.</summary>
    public static string Clean(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var cleaned = LeadingTimestamp().Replace(title, string.Empty).Trim();
        return cleaned.Length == 0 ? title : cleaned;
    }

    [GeneratedRegex(@"^\s*\d{1,2}\.\d{1,2}\.\d{4}(\s+\d{1,2}:\d{2}(:\d{2})?)?\s*[-–—]\s*")]
    private static partial Regex LeadingTimestamp();
}
