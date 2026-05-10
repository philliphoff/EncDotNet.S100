namespace EncDotNet.S100.Core;

/// <summary>
/// Identity of a single Feature Catalogue or Portrayal Catalogue instance —
/// the pair <c>(name, version)</c> declared inside the catalogue's metadata
/// (<c>&lt;S100FC:productId&gt;</c> + <c>&lt;S100FC:versionNumber&gt;</c> for
/// FCs; equivalent fields for PCs).
/// </summary>
/// <remarks>
/// <para>
/// <c>CatalogueRef</c> is intentionally distinct from <see cref="SpecRef"/>
/// even though both carry a name and a version: a catalogue's version
/// advances on a different release cadence from its owning product
/// specification edition. For example, S-101 product spec edition 1.2.0
/// currently ships with a Feature Catalogue at version 2.0.0.
/// </para>
/// <para>
/// Used as the cache key inside FC and PC managers so that two distinct
/// catalogue versions can coexist in a single process — something a bare
/// product-name string cannot express.
/// </para>
/// </remarks>
public readonly record struct CatalogueRef
{
    /// <summary>
    /// The catalogue's owning product specification short name in canonical
    /// <c>"S-NNN"</c> form (e.g. <c>"S-101"</c>).
    /// </summary>
    public string Name { get; }

    /// <summary>The catalogue version (independent of any product spec edition).</summary>
    public SpecVersion Version { get; }

    /// <summary>
    /// Creates a new <see cref="CatalogueRef"/>. <paramref name="name"/> is
    /// normalised to <c>"S-NNN"</c> form; <see cref="FormatException"/> is
    /// thrown if it is not a recognised S-100 product spec name.
    /// </summary>
    public CatalogueRef(string name, SpecVersion version)
    {
        Name = SpecName.Normalize(name);
        Version = version;
    }

    /// <summary>
    /// Parses a <see cref="CatalogueRef"/> from <c>"S-101@2.0.0"</c> or
    /// <c>"S-101/2.0.0"</c> form.
    /// </summary>
    /// <exception cref="FormatException">The input does not match any recognised form.</exception>
    public static CatalogueRef Parse(string s)
    {
        if (!TryParse(s, out var v))
        {
            throw new FormatException($"'{s}' is not a valid CatalogueRef.");
        }

        return v;
    }

    /// <summary>Tolerant variant of <see cref="Parse"/> returning <c>false</c> on failure.</summary>
    public static bool TryParse(string? s, out CatalogueRef value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        var trimmed = s.Trim();
        var sep = trimmed.IndexOfAny(['@', '/']);
        if (sep <= 0 || sep == trimmed.Length - 1) return false;

        var namePart = trimmed[..sep];
        var versionPart = trimmed[(sep + 1)..];
        if (!SpecName.TryNormalize(namePart, out var name)) return false;
        if (!SpecVersion.TryParse(versionPart, out var version)) return false;

        value = new CatalogueRef(name, version);
        return true;
    }

    /// <summary>Returns the canonical <c>"S-NNN@M.m.c"</c> form.</summary>
    public override string ToString() => $"{Name}@{Version}";
}
