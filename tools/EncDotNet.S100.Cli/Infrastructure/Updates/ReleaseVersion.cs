namespace EncDotNet.S100.Cli.Infrastructure.Updates;

/// <summary>
/// Parses and compares GitHub release tags using SemVer 2.0.0 precedence,
/// so a release is newer than its own pre-releases (for example
/// <c>0.25.2</c> is newer than <c>0.25.2-nightly.20261021</c>).
/// </summary>
internal static class ReleaseVersion
{
    public static bool TryParse(string? value, out Version version, out string[] prerelease)
    {
        version = new Version(0, 0, 0);
        prerelease = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.Trim();
        if (span.StartsWith('v') || span.StartsWith('V'))
        {
            span = span[1..];
        }

        var metadata = span.IndexOf('+');
        if (metadata >= 0)
        {
            span = span[..metadata];
        }

        var suffix = span.IndexOf('-');
        if (suffix >= 0)
        {
            var identifiers = span[(suffix + 1)..].Split('.');
            if (identifiers.Any(string.IsNullOrEmpty))
            {
                return false;
            }

            prerelease = identifiers;
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
        if (!TryParse(candidate, out var candidateVersion, out var candidatePrerelease)
            || !TryParse(current, out var currentVersion, out var currentPrerelease))
        {
            return false;
        }

        var core = candidateVersion.CompareTo(currentVersion);
        return core != 0
            ? core > 0
            : ComparePrerelease(candidatePrerelease, currentPrerelease) > 0;
    }

    /// <summary>
    /// Gets whether <paramref name="value"/> is a nightly build version: its
    /// first pre-release identifier starts with <c>nightly</c>
    /// (for example <c>0.25.2-nightly.20261021+abc1234</c>).
    /// </summary>
    public static bool IsNightly(string? value) =>
        TryParse(value, out _, out var prerelease)
        && prerelease.Length > 0
        && prerelease[0].StartsWith("nightly", StringComparison.OrdinalIgnoreCase);

    private static int ComparePrerelease(string[] left, string[] right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return right.Length.CompareTo(left.Length);
        }

        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var result = CompareIdentifier(left[i], right[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = left.All(char.IsAsciiDigit);
        var rightNumeric = right.All(char.IsAsciiDigit);

        if (leftNumeric && rightNumeric)
        {
            var trimmedLeft = left.TrimStart('0');
            var trimmedRight = right.TrimStart('0');
            var length = trimmedLeft.Length.CompareTo(trimmedRight.Length);
            return length != 0 ? length : Math.Sign(string.CompareOrdinal(trimmedLeft, trimmedRight));
        }

        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? -1 : 1;
        }

        return Math.Sign(string.CompareOrdinal(left, right));
    }
}
