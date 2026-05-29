using System.Collections.Generic;
using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S101.Validation;

/// <summary>
/// A spec-vocabulary view over a single <see cref="S101FeatureRecord"/>.
/// </summary>
/// <remarks>
/// <para>
/// Implements the façade contract described in
/// <c>docs/design/non-gml-validation.md</c> §3.1 (input model option (b)).
/// Rules read feature-type acronyms and attribute acronyms; the
/// raw record (with numeric codes, complex-attribute marker rows,
/// spatial-record references, etc.) is reachable through the
/// underlying <see cref="S101DatasetView.Raw"/> when needed.
/// </para>
/// <para>
/// Per the design note's closing paragraph of §3.1, this façade is
/// deliberately a strict superset of what a future fully typed
/// projection (option (d)) would expose. Rules MUST NOT introduce
/// dependencies on <see cref="S101FeatureRecord"/> through this view
/// type — anything that requires raw-record access goes via
/// <see cref="S101DatasetView.Raw"/>.
/// </para>
/// </remarks>
public sealed class S101FeatureView
{
    private readonly Dictionary<string, List<S101AttributeView>> _byAcronym;
    private readonly Dictionary<string, List<IReadOnlyList<S101AttributeView>>> _complexByAcronym;

    internal S101FeatureView(
        S101FeatureRecord raw,
        string? featureTypeAcronym,
        ImmutableArray<S101AttributeView> attributes,
        IReadOnlyDictionary<ushort, string> attributeTypeCatalogue)
    {
        Raw = raw;
        FeatureTypeAcronym = featureTypeAcronym;
        Attributes = attributes;
        _byAcronym = new Dictionary<string, List<S101AttributeView>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var a in attributes)
        {
            if (a.Acronym is null) continue;
            if (!_byAcronym.TryGetValue(a.Acronym, out var list))
            {
                list = new List<S101AttributeView>();
                _byAcronym[a.Acronym] = list;
            }
            list.Add(a);
        }

        _complexByAcronym = new Dictionary<string, List<IReadOnlyList<S101AttributeView>>>(
            System.StringComparer.OrdinalIgnoreCase);
        BuildComplexIndex(raw, attributeTypeCatalogue);
    }

    private void BuildComplexIndex(
        S101FeatureRecord raw,
        IReadOnlyDictionary<ushort, string> attributeTypeCatalogue)
    {
        // S-101 complex attributes (S-100 Part 10a §4.4) encode each
        // instance as a marker row (Index == 1, empty Value) followed
        // by sub-attribute rows carrying Index > 1. We slice each
        // marker-plus-children run and index it by the parent acronym
        // so rules can iterate instances without re-walking the
        // record.
        var rows = raw.Attributes;
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (row.Index != 1 || !string.IsNullOrEmpty(row.Value))
                continue;
            if (!attributeTypeCatalogue.TryGetValue(row.NumericCode, out var acronym))
                continue;

            var slice = new List<S101AttributeView>();
            int j = i + 1;
            while (j < rows.Length && rows[j].Index > 1)
            {
                attributeTypeCatalogue.TryGetValue(rows[j].NumericCode, out var subAcronym);
                slice.Add(new S101AttributeView
                {
                    Acronym = subAcronym,
                    NumericCode = rows[j].NumericCode,
                    Index = rows[j].Index,
                    Value = rows[j].Value,
                });
                j++;
            }

            if (!_complexByAcronym.TryGetValue(acronym, out var instances))
            {
                instances = new List<IReadOnlyList<S101AttributeView>>();
                _complexByAcronym[acronym] = instances;
            }
            instances.Add(slice);
        }
    }

    /// <summary>The underlying ISO 8211 feature record this view wraps.</summary>
    public S101FeatureRecord Raw { get; }

    /// <summary>
    /// Feature-type acronym resolved through
    /// <see cref="S101Document.FeatureTypeCatalogue"/> — for example
    /// <c>"DepthArea"</c>. <c>null</c> when
    /// <see cref="S101FeatureRecord.FeatureTypeCode"/> does not appear
    /// in the catalogue (the failure mode reported by <c>S101-R-1.1</c>).
    /// </summary>
    public string? FeatureTypeAcronym { get; }

    /// <summary>
    /// FOID triple (producing agency, FIDN, FIDS) projected into the
    /// canonical string form used by the design note §4.3:
    /// <c>"{producingAgency}:{FIDN}.{FIDS}"</c>. This is the value
    /// rule authors attach to <c>RelatedFeatureId</c> on findings.
    /// </summary>
    public string FoidKey =>
        $"{Raw.ProducingAgency}:{Raw.FeatureIdentificationNumber}.{Raw.FeatureIdentificationSubdivision}";

    /// <summary>
    /// Flat list of attribute rows for the feature, one view per ISO
    /// 8211 ATTR row. Complex-attribute marker rows are preserved so
    /// rule authors who need the raw ordering can iterate them.
    /// </summary>
    public ImmutableArray<S101AttributeView> Attributes { get; }

    /// <summary>
    /// Returns every complex-attribute instance whose parent acronym
    /// matches <paramref name="acronym"/>. Each instance is the
    /// (possibly empty) slice of sub-attribute rows that followed its
    /// marker row in the original record.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<S101AttributeView>> ComplexAttributes(string acronym)
    {
        if (string.IsNullOrEmpty(acronym))
            return System.Array.Empty<IReadOnlyList<S101AttributeView>>();
        return _complexByAcronym.TryGetValue(acronym, out var list)
            ? list
            : System.Array.Empty<IReadOnlyList<S101AttributeView>>();
    }

    /// <summary>
    /// Convenience accessor for a simple attribute identified by
    /// <paramref name="acronym"/>. Returns the value of the first
    /// matching row, or <c>null</c> when the attribute is absent.
    /// </summary>
    /// <remarks>
    /// Multi-valued simple attributes (the rare case where the same
    /// acronym appears more than once on a feature) require rule
    /// authors to iterate <see cref="Attributes"/> directly; this
    /// method is shaped for the dominant single-valued case.
    /// </remarks>
    public string? GetSimple(string acronym)
    {
        if (string.IsNullOrEmpty(acronym)) return null;
        return _byAcronym.TryGetValue(acronym, out var list) && list.Count > 0
            ? list[0].Value
            : null;
    }

    /// <summary>
    /// Returns every simple-attribute row whose acronym matches
    /// <paramref name="acronym"/>. Useful for rules that need to
    /// inspect every value of a repeating simple attribute.
    /// </summary>
    public IEnumerable<S101AttributeView> GetAllSimple(string acronym)
    {
        if (string.IsNullOrEmpty(acronym))
            return System.Array.Empty<S101AttributeView>();
        return _byAcronym.TryGetValue(acronym, out var list)
            ? list
            : System.Array.Empty<S101AttributeView>();
    }

    /// <summary>Spatial associations attached to the feature, in record order.</summary>
    public ImmutableArray<S101SpatialAssociation> SpatialAssociations => Raw.SpatialAssociations;

    /// <summary>Feature-to-feature associations attached to the feature, in record order.</summary>
    public ImmutableArray<S101FeatureAssociation> FeatureAssociations => Raw.FeatureAssociations;

    /// <summary>Feature-to-information-type associations attached to the feature, in record order.</summary>
    public ImmutableArray<S101InformationAssociation> InformationAssociations => Raw.InformationAssociations;
}
