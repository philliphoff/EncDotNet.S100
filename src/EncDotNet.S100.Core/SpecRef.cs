namespace EncDotNet.S100.Core;

/// <summary>
/// Identity of a single S-100 product specification edition — the pair
/// <c>(name, edition)</c> that a dataset declares conformance to (e.g.
/// <c>S-101 / 1.2.0</c>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Name"/> is normalised to canonical <c>"S-NNN"</c> form by the
/// constructor. Use <see cref="Parse"/> for tolerant parsing of the various
/// representations seen in real-world datasets, exchange-set CATALOG.XML
/// entries, HDF5 <c>productSpecification</c> attributes, and GML application
/// namespaces.
/// </para>
/// <para>
/// <c>SpecRef</c> identifies the <em>product specification</em> a dataset
/// targets. The version of the Feature or Portrayal Catalogue used to
/// process that dataset is a separate identity tracked by
/// <see cref="CatalogueRef"/>; the two float independently because S-100
/// product specs and their catalogues advance on different release cadences
/// (e.g. S-101 product spec at edition 1.2.0 ships with a Feature
/// Catalogue at version 2.0.0).
/// </para>
/// </remarks>
public readonly record struct SpecRef
{
    /// <summary>
    /// The product specification short name in canonical <c>"S-NNN"</c> form
    /// (e.g. <c>"S-101"</c>, <c>"S-102"</c>).
    /// </summary>
    public string Name { get; }

    /// <summary>The product specification edition.</summary>
    public SpecVersion Edition { get; }

    /// <summary>
    /// Creates a new <see cref="SpecRef"/>. <paramref name="name"/> is
    /// normalised to <c>"S-NNN"</c> form; <see cref="FormatException"/> is
    /// thrown if it is not a recognised S-100 product spec name.
    /// </summary>
    public SpecRef(string name, SpecVersion edition)
    {
        Name = SpecName.Normalize(name);
        Edition = edition;
    }

    /// <summary>
    /// Parses a <see cref="SpecRef"/> from one of the following forms:
    /// <list type="bullet">
    ///   <item><description><c>"S-101/1.2.0"</c> — canonical form (this type's <see cref="ToString"/>).</description></item>
    ///   <item><description><c>"S-101@1.2.0"</c> — alternate separator.</description></item>
    ///   <item><description><c>"INT.IHO.S-101.1.2.0"</c> — exchange-set product identifier form.</description></item>
    /// </list>
    /// Casing of the leading <c>S</c> and presence of the dash are tolerated.
    /// </summary>
    /// <exception cref="FormatException">The input does not match any recognised form.</exception>
    public static SpecRef Parse(string s)
    {
        if (!TryParse(s, out var v))
        {
            throw new FormatException($"'{s}' is not a valid SpecRef.");
        }

        return v;
    }

    /// <summary>Tolerant variant of <see cref="Parse"/> returning <c>false</c> on failure.</summary>
    public static bool TryParse(string? s, out SpecRef value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        var trimmed = s.Trim();

        // INT.IHO.S-NNN.x.y.z — long-form product identifier.
        if (trimmed.StartsWith("INT.IHO.", StringComparison.OrdinalIgnoreCase))
        {
            var afterPrefix = trimmed["INT.IHO.".Length..];
            // "S-101.1.2.0" or "S101.1.2.0"
            var firstDot = afterPrefix.IndexOf('.');
            if (firstDot <= 0) return false;
            var rawName = afterPrefix[..firstDot];
            var rawVersion = afterPrefix[(firstDot + 1)..];
            if (!SpecName.TryNormalize(rawName, out var canonicalName)) return false;
            if (!SpecVersion.TryParse(rawVersion, out var version)) return false;
            value = new SpecRef(canonicalName, version);
            return true;
        }

        // <name><sep><version> with sep ∈ { '/', '@' }.
        var sep = trimmed.IndexOfAny(['/', '@']);
        if (sep <= 0 || sep == trimmed.Length - 1) return false;

        var namePart = trimmed[..sep];
        var versionPart = trimmed[(sep + 1)..];
        if (!SpecName.TryNormalize(namePart, out var name)) return false;
        if (!SpecVersion.TryParse(versionPart, out var ed)) return false;

        value = new SpecRef(name, ed);
        return true;
    }

    /// <summary>Returns the canonical <c>"S-NNN/M.m.c"</c> form.</summary>
    public override string ToString() => $"{Name}/{Edition}";
}
