namespace EncDotNet.S100.Viewer.Services.Updates;

/// <summary>
/// Minimal lenient SemVer comparison sufficient for the viewer's
/// <c>MAJOR.MINOR.PATCH</c> release tags. A leading <c>v</c> and any
/// pre-release (<c>-…</c>) or build-metadata (<c>+…</c>) suffix are
/// ignored; the numeric core is compared via <see cref="System.Version"/>.
/// </summary>
internal static class ReleaseVersion
{
    /// <summary>
    /// Attempts to parse a release version string (e.g. <c>"v2.5.0"</c>,
    /// <c>"2.5.0"</c>, <c>"2.5.0-rc.1"</c>) into a normalised
    /// <see cref="System.Version"/>. Returns <see langword="false"/> for
    /// null/empty or non-numeric input.
    /// </summary>
    public static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var span = value.Trim();
        if (span.StartsWith('v') || span.StartsWith('V'))
            span = span[1..];

        // Drop pre-release / build metadata; keep only the numeric core.
        var cut = span.IndexOfAny(['-', '+']);
        if (cut >= 0)
            span = span[..cut];

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
        if (!TryParse(candidate, out var candidateVersion))
            return false;
        if (!TryParse(current, out var currentVersion))
            return false;

        return candidateVersion > currentVersion;
    }
}
