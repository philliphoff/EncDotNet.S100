using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// One card of a symbol's Used by (design note §4.4, C2): the feature types
/// that draw it in the same cases, with those cases said once. Obstruction,
/// Underwater/Awash Rock and Wreck all draw ISODGR01 the same way, so they
/// share a card rather than repeat the cases three times.
/// </summary>
internal sealed class UsedByGroupViewModel
{
    /// <summary>Creates a card.</summary>
    /// <param name="featureTypes">The feature types, in order; each has the same cases.</param>
    public UsedByGroupViewModel(IReadOnlyList<ChartReferenceLinkViewModel> featureTypes)
    {
        ArgumentNullException.ThrowIfNull(featureTypes);
        if (featureTypes.Count == 0)
        {
            throw new ArgumentException("A card has at least one feature type.", nameof(featureTypes));
        }

        FeatureTypes = featureTypes;
        CaseTerms = featureTypes[0].CaseTerms;
    }

    /// <summary>The feature types, each a link to its entry.</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> FeatureTypes { get; }

    /// <summary>The cases they share, one line per case, with their names as terms.</summary>
    public IReadOnlyList<TermText> CaseTerms { get; }

    /// <summary>Whether there are cases to show.</summary>
    public bool HasCases => CaseTerms.Count > 0;

    /// <summary>
    /// Groups links by their cases, keeping the links' order: a card per set of
    /// cases, in the order its first feature type comes.
    /// </summary>
    /// <param name="links">The feature types, each with its cases.</param>
    /// <returns>The cards.</returns>
    public static List<UsedByGroupViewModel> Group(IEnumerable<ChartReferenceLinkViewModel> links) =>
    [
        .. links
            .GroupBy(l => l.Cases, CasesComparer.Instance)
            .Select(g => new UsedByGroupViewModel([.. g])),
    ];

    private sealed class CasesComparer : IEqualityComparer<IReadOnlyList<string>>
    {
        public static CasesComparer Instance { get; } = new();

        public bool Equals(IReadOnlyList<string>? x, IReadOnlyList<string>? y) =>
            ReferenceEquals(x, y) || x is not null && y is not null && x.SequenceEqual(y, StringComparer.Ordinal);

        public int GetHashCode(IReadOnlyList<string> obj)
        {
            var hash = default(HashCode);
            foreach (var c in obj)
            {
                hash.Add(c, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }
}
