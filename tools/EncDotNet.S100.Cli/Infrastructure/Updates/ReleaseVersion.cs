namespace EncDotNet.S100.Cli.Infrastructure.Updates;

/// <summary>
/// Parses and compares the numeric core of GitHub release tags.
/// </summary>
internal static class ReleaseVersion
{
    public static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.Trim();
        if (span.StartsWith('v') || span.StartsWith('V'))
        {
            span = span[1..];
        }

        var suffix = span.IndexOfAny(['-', '+']);
        if (suffix >= 0)
        {
            span = span[..suffix];
        }

        if (span.Length == 0)
        {
            return false;
        }

        if (!span.Contains('.'))
        {
            span += ".0";
        }

        if (!Version.TryParse(span, out var parsed))
        {
            return false;
        }

        version = new Version(
            Math.Max(parsed.Major, 0),
            Math.Max(parsed.Minor, 0),
            Math.Max(parsed.Build, 0));
        return true;
    }

    public static bool IsNewer(string? candidate, string? current)
    {
        return TryParse(candidate, out var candidateVersion)
            && TryParse(current, out var currentVersion)
            && candidateVersion > currentVersion;
    }
}
