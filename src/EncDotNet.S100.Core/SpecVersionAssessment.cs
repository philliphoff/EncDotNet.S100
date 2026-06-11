namespace EncDotNet.S100.Core;

/// <summary>
/// A user-facing assessment of how the product specification edition a
/// dataset declares relates to the edition(s) of that product this application
/// actually implements.
/// </summary>
/// <remarks>
/// <para>
/// The warning deliberately compares the dataset's declared edition against
/// the <em>edition this application supports</em> — not against the Feature or
/// Portrayal Catalogue version number. Catalogue versions advance on their
/// own release cadence and routinely differ from the product-spec edition
/// (e.g. the S-101 portrayal catalogue is at version 2.0.0 while the product
/// specification is at edition 1.x), so comparing a declared edition against
/// a catalogue version would raise false alarms. The catalogue version is
/// surfaced separately as informational context via <see cref="Catalogue"/>.
/// </para>
/// <para>
/// A product may implement more than one edition (e.g. S-102 reads both
/// edition 2.1 and 3.0). The declared edition is matched against the
/// <see cref="Supported"/> member sharing its major component; when none
/// does, the relationship is <see cref="SpecMatchKind.MajorDivergence"/>.
/// Classification follows S-100 Edition 5.2.1 Part 2 §6 (Maintenance) via
/// <see cref="SpecCompatibility.Classify(SpecVersion, SpecVersion)"/>.
/// </para>
/// <para>
/// When the dataset does not declare an edition, <see cref="Kind"/> is
/// <see cref="SpecMatchKind.Unknown"/> and both <see cref="IsDivergent"/>
/// and <see cref="IsWarning"/> are <c>false</c> — hosts stay silent rather
/// than warn on missing information.
/// </para>
/// </remarks>
public sealed record SpecVersionAssessment
{
    private SpecVersionAssessment(
        SpecRef declared,
        SpecVersion implemented,
        IReadOnlyList<SpecVersion> supported,
        CatalogueRef? catalogue,
        SpecMatchKind kind)
    {
        Declared = declared;
        Implemented = implemented;
        Supported = supported;
        Catalogue = catalogue;
        Kind = kind;
    }

    /// <summary>The product specification (name + edition) the dataset declares.</summary>
    public SpecRef Declared { get; }

    /// <summary>
    /// The build-implemented edition the declared edition was compared against
    /// — the <see cref="Supported"/> member sharing the declared major, or
    /// (when none does) the highest supported edition.
    /// </summary>
    public SpecVersion Implemented { get; }

    /// <summary>Every edition of this product the application supports.</summary>
    public IReadOnlyList<SpecVersion> Supported { get; }

    /// <summary>
    /// The version of the Feature/Portrayal Catalogue resolved for the
    /// dataset, surfaced for display only (it does not drive the warning).
    /// <c>null</c> when the catalogue does not self-describe.
    /// </summary>
    public CatalogueRef? Catalogue { get; }

    /// <summary>The classified relationship between declared and implemented editions.</summary>
    public SpecMatchKind Kind { get; }

    /// <summary>
    /// <c>true</c> when the declared edition and the implemented edition are
    /// not identical and the declared edition is known — i.e. there is
    /// something worth telling the user about, however benign.
    /// </summary>
    public bool IsDivergent => Kind is not SpecMatchKind.Exact and not SpecMatchKind.Unknown;

    /// <summary>
    /// <c>true</c> when the divergence may produce incomplete or incorrect
    /// output — the application supports an older edition on the same major
    /// (<see cref="SpecMatchKind.CatalogueOlder"/>) or no edition on the
    /// declared major (<see cref="SpecMatchKind.MajorDivergence"/>). A build
    /// that implements a newer backward-compatible edition is divergent but
    /// not a warning.
    /// </summary>
    public bool IsWarning => Kind is SpecMatchKind.CatalogueOlder or SpecMatchKind.MajorDivergence;

    /// <summary>
    /// Builds the assessment for a declared spec and the set of editions the
    /// application supports for that product.
    /// </summary>
    /// <param name="declared">The dataset's declared product spec.</param>
    /// <param name="supported">
    /// Every edition of the product the application supports. Must be non-empty;
    /// returns <c>null</c> when empty (no support known).
    /// </param>
    /// <param name="catalogue">
    /// The resolved catalogue identity for informational display, or
    /// <c>null</c> when unknown.
    /// </param>
    public static SpecVersionAssessment? Create(
        SpecRef declared,
        IReadOnlyList<SpecVersion> supported,
        CatalogueRef? catalogue = null)
    {
        ArgumentNullException.ThrowIfNull(supported);
        if (supported.Count == 0) return null;

        var highest = supported[0];
        foreach (var v in supported)
        {
            if (v > highest) highest = v;
        }

        // No declared edition → nothing to compare; report Unknown so hosts
        // stay silent but can still display the implemented edition.
        if (declared.Edition == default)
        {
            return new SpecVersionAssessment(declared, highest, supported, catalogue, SpecMatchKind.Unknown);
        }

        // Prefer the supported edition sharing the declared major so a
        // multi-edition build (e.g. S-102 {2.1, 3.0}) does not flag a
        // supported-but-not-latest dataset.
        SpecVersion? sameMajor = null;
        foreach (var v in supported)
        {
            if (v.Major == declared.Edition.Major && (sameMajor is null || v > sameMajor))
            {
                sameMajor = v;
            }
        }

        var implemented = sameMajor ?? highest;
        var kind = SpecCompatibility.Classify(declared.Edition, implemented);
        return new SpecVersionAssessment(declared, implemented, supported, catalogue, kind);
    }

    /// <summary>
    /// Returns a single-line, human-readable description of the divergence,
    /// e.g. <c>"Dataset targets S-104 edition 0.8.0, but this application
    /// supports S-104 edition 2.0.0; rendering may be incomplete or
    /// incorrect."</c> Returns <c>null</c> when <see cref="IsDivergent"/> is
    /// <c>false</c>.
    /// </summary>
    /// <remarks>
    /// The supported edition is attributed to "this application" because it is
    /// a property of the reader/portrayal implementation in this codebase: an
    /// S-100 Feature or Portrayal Catalogue does not declare which
    /// product-specification edition it targets (only its own catalogue
    /// version), so the supported edition cannot be derived from the loaded
    /// catalogue and is instead asserted by the build (see
    /// <c>SupportedSpecEditions</c>).
    /// </remarks>
    public string? BuildMessage()
    {
        if (!IsDivergent) return null;

        if (IsWarning)
        {
            return $"Dataset targets {Declared.Name} edition {Declared.Edition}, but this "
                + $"application supports {Declared.Name} edition {Implemented}; rendering "
                + "may be incomplete or incorrect.";
        }

        // Divergent but benign: the application supports a newer edition on the
        // same major, which reads the declared edition's data correctly.
        return $"Dataset targets {Declared.Name} edition {Declared.Edition}; this application "
            + $"supports the newer, backward-compatible edition {Implemented}.";
    }
}
