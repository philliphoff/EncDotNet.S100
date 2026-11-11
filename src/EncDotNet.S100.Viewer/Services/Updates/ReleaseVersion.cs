namespace EncDotNet.S100.Viewer.Services.Updates;

/// <summary>
/// Minimal lenient SemVer comparison sufficient for the viewer's
/// <c>MAJOR.MINOR.PATCH[-PRERELEASE]</c> release tags. A leading <c>v</c>
/// and any build-metadata (<c>+…</c>) suffix are ignored; the numeric core
/// is compared via <see cref="System.Version"/> and pre-release identifiers
/// follow SemVer 2.0.0 precedence, so <c>0.25.2-nightly.20261021</c> sorts
/// after <c>0.25.1</c> and before <c>0.25.2</c>.
/// </summary>
internal static class ReleaseVersion
{
    /// <summary>
    /// Attempts to parse a release version string (e.g. <c>"v2.5.0"</c>,
    /// <c>"2.5.0"</c>, <c>"2.5.0-rc.1"</c>) into its normalised numeric core
    /// and pre-release identifiers (empty for a release). Returns
    /// <see langword="false"/> for null/empty or non-numeric input.
    /// </summary>
    public static bool TryParse(string? value, out Version version, out string[] prerelease)
    {
        version = new Version(0, 0, 0);
        prerelease = [];
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var span = value.Trim();
        if (span.StartsWith('v') || span.StartsWith('V'))
            span = span[1..];

        // Build metadata never affects precedence.
        var plus = span.IndexOf('+');
        if (plus >= 0)
            span = span[..plus];

        var dash = span.IndexOf('-');
        if (dash >= 0)
        {
            var identifiers = span[(dash + 1)..].Split('.');
            if (identifiers.Any(string.IsNullOrEmpty))
                return false;

            prerelease = identifiers;
            span = span[..dash];
        }

        if (span.Length == 0)
            return false;

        // System.Version requires at least MAJOR.MINOR; pad a bare MAJOR.
        if (!span.Contains('.'))
            span += ".0";

        return Version.TryParse(span, out var parsed) && Assign(parsed, out version);

        static bool Assign(Version parsed, out Version target)
        {
            // Normalise unspecified components (-1) to 0 so 2.5 == 2.5.0.
            target = new Version(
                Math.Max(parsed.Major, 0),
                Math.Max(parsed.Minor, 0),
                Math.Max(parsed.Build, 0));
            return true;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="candidate"/> is a
    /// strictly newer release than <paramref name="current"/>. Unparseable
    /// inputs are treated as "not newer" (fail safe — never nag on garbage).
    /// </summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        if (!TryParse(candidate, out var candidateVersion, out var candidatePrerelease))
            return false;
        if (!TryParse(current, out var currentVersion, out var currentPrerelease))
            return false;

        var core = candidateVersion.CompareTo(currentVersion);
        if (core != 0)
            return core > 0;

        return ComparePrerelease(candidatePrerelease, currentPrerelease) > 0;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> is a
    /// nightly build version, i.e. its first pre-release identifier starts
    /// with <c>nightly</c> (e.g. <c>"0.25.2-nightly.20261021+abc1234"</c>).
    /// Stable releases, other pre-releases (<c>-rc.1</c>) and the local
    /// <c>0.0.0-dev</c> default are not nightly builds.
    /// </summary>
    public static bool IsNightly(string? value) =>
        TryParse(value, out _, out var prerelease)
        && prerelease.Length > 0
        && prerelease[0].StartsWith("nightly", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Compares pre-release identifier lists per SemVer 2.0.0 §11: a release
    /// (no identifiers) outranks any of its pre-releases; numeric identifiers
    /// compare numerically and rank below alphanumeric ones, which compare
    /// ordinally; a shorter list that is a prefix of a longer one ranks lower.
    /// </summary>
    private static int ComparePrerelease(string[] left, string[] right)
    {
        if (left.Length == 0 || right.Length == 0)
            return right.Length.CompareTo(left.Length);

        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var result = CompareIdentifier(left[i], right[i]);
            if (result != 0)
                return result;
        }

        return left.Length.CompareTo(right.Length);
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = left.All(char.IsAsciiDigit);
        var rightNumeric = right.All(char.IsAsciiDigit);

        if (leftNumeric && rightNumeric)
        {
            // Compare by length first so arbitrarily long numbers never overflow.
            var trimmedLeft = left.TrimStart('0');
            var trimmedRight = right.TrimStart('0');
            var length = trimmedLeft.Length.CompareTo(trimmedRight.Length);
            return length != 0 ? length : Math.Sign(string.CompareOrdinal(trimmedLeft, trimmedRight));
        }

        if (leftNumeric != rightNumeric)
            return leftNumeric ? -1 : 1;

        return Math.Sign(string.CompareOrdinal(left, right));
    }
}
