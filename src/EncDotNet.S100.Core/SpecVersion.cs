using System.ComponentModel;
using System.Globalization;

namespace EncDotNet.S100.Core;

/// <summary>
/// A three-component S-100 version of the form
/// <c>&lt;major&gt;.&lt;minor&gt;.&lt;clarification&gt;</c>, with the
/// compatibility semantics defined by S-100 Edition 5.2.1 Part 2 §6
/// (Maintenance):
/// <list type="bullet">
///   <item><description><b>Major</b> — backward-incompatible change.</description></item>
///   <item><description><b>Minor</b> — backward-compatible addition (new feature
///   classes, attributes, code-list entries, etc.).</description></item>
///   <item><description><b>Clarification</b> — editorial change with no
///   semantic impact on the data model.</description></item>
/// </list>
/// This is <em>not</em> Semantic Versioning despite its similar shape — there are
/// no pre-release tags, no build metadata, and clarification-level differences
/// are interchangeable for compatibility purposes.
/// </summary>
public readonly record struct SpecVersion : IComparable<SpecVersion>
{
    /// <summary>Major component (backward-incompatible change boundary).</summary>
    [Description("Major component (backward-incompatible change boundary).")]
    public int Major { get; }

    /// <summary>Minor component (backward-compatible additions).</summary>
    [Description("Minor component (backward-compatible additions).")]
    public int Minor { get; }

    /// <summary>Clarification component (editorial-only changes).</summary>
    [Description("Clarification component (editorial-only changes).")]
    public int Clarification { get; }

    /// <summary>
    /// Creates a new <see cref="SpecVersion"/>. All components must be
    /// non-negative.
    /// </summary>
    public SpecVersion(int major, int minor, int clarification)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(clarification);

        Major = major;
        Minor = minor;
        Clarification = clarification;
    }

    /// <summary>
    /// Parses a version string of the form <c>"M"</c>, <c>"M.m"</c>, or
    /// <c>"M.m.c"</c>. Missing trailing components default to <c>0</c>.
    /// </summary>
    /// <exception cref="FormatException">The input is not a valid version string.</exception>
    public static SpecVersion Parse(string s)
    {
        if (!TryParse(s, out var v))
        {
            throw new FormatException($"'{s}' is not a valid S-100 version (expected M, M.m, or M.m.c).");
        }

        return v;
    }

    /// <summary>
    /// Attempts to parse a version string of the form <c>"M"</c>, <c>"M.m"</c>,
    /// or <c>"M.m.c"</c>. Missing trailing components default to <c>0</c>.
    /// </summary>
    public static bool TryParse(string? s, out SpecVersion value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        var trimmed = s.Trim();
        var parts = trimmed.Split('.');
        if (parts.Length is 0 or > 3) return false;

        Span<int> components = stackalloc int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                || n < 0)
            {
                return false;
            }

            components[i] = n;
        }

        value = new SpecVersion(components[0], components[1], components[2]);
        return true;
    }

    /// <summary>
    /// Returns <c>true</c> when a consumer conforming to this version can read
    /// content produced for <paramref name="older"/> — i.e. the two share the
    /// same major component and this version's minor is at least as high as
    /// the older one's. Clarification-level differences are ignored
    /// per S-100 Part 2 §6.
    /// </summary>
    public bool IsBackwardCompatibleWith(SpecVersion older)
        => Major == older.Major && Minor >= older.Minor;

    /// <inheritdoc />
    public int CompareTo(SpecVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        return Clarification.CompareTo(other.Clarification);
    }

    public static bool operator <(SpecVersion left, SpecVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SpecVersion left, SpecVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SpecVersion left, SpecVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SpecVersion left, SpecVersion right) => left.CompareTo(right) >= 0;

    /// <summary>Returns the canonical <c>"M.m.c"</c> representation.</summary>
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{Major}.{Minor}.{Clarification}");
}
