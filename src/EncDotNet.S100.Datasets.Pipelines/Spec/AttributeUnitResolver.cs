using System.Collections.Concurrent;
using EncDotNet.S100.Features;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Datasets.Pipelines.Spec;

/// <summary>
/// Resolves the unit of measure declared for a simple attribute in a
/// product specification's Feature Catalogue, so describers can annotate
/// numeric attribute values (e.g. S-101 depth-valued attributes such as
/// <c>depthRangeMinimumValue</c>) with their authoritative unit rather
/// than returning a bare, unitless string (issue #334).
/// </summary>
/// <remarks>
/// <para>
/// The unit is read from the Feature Catalogue's
/// <c>&lt;S100FC:uom&gt;</c> element (S-100 Part 5), which is the
/// source-of-truth for the unit — no hard-coded attribute allow-list is
/// involved. The resolver is spec-agnostic: any describer can ask for the
/// unit of an attribute code under its own spec, so the convention is
/// shared across S-101 and the GML/coverage products.
/// </para>
/// <para>
/// Resolution is best-effort. When the Feature Catalogue for a spec is
/// unavailable (e.g. not bundled, or fails to parse) the resolver returns
/// <see langword="false"/> and the caller simply omits the unit, mirroring
/// how attribute acronyms degrade to numeric codes elsewhere in the
/// describer pipeline. Per-spec attribute lookups are cached so each
/// Feature Catalogue is parsed and indexed at most once per resolver.
/// </para>
/// </remarks>
internal sealed class AttributeUnitResolver
{
    private readonly FeatureCatalogueManager _catalogues;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, UnitOfMeasure>?> _byCodeBySpec;

    /// <summary>
    /// Creates a resolver backed by the bundled specifications via
    /// <see cref="Specification.TryOpenFeatureCatalogue"/> (the same
    /// catalogue source <c>describe_feature_type</c> uses).
    /// </summary>
    public AttributeUnitResolver()
        : this(Specification.TryOpenFeatureCatalogue)
    {
    }

    /// <summary>
    /// Creates a resolver backed by a custom Feature Catalogue stream
    /// resolver, primarily for testing.
    /// </summary>
    /// <param name="catalogueResolver">
    /// A function that, given a product specification name (e.g.
    /// <c>"S-101"</c>), returns a readable Feature Catalogue XML stream, or
    /// <c>null</c> when no catalogue is available.
    /// </param>
    public AttributeUnitResolver(Func<string, Stream?> catalogueResolver)
    {
        ArgumentNullException.ThrowIfNull(catalogueResolver);
        _catalogues = new FeatureCatalogueManager(catalogueResolver);
        _byCodeBySpec = new ConcurrentDictionary<string, IReadOnlyDictionary<string, UnitOfMeasure>?>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Attempts to resolve the unit of measure for the simple attribute
    /// identified by <paramref name="attributeCode"/> (the Feature
    /// Catalogue <c>code</c>, e.g. <c>"depthRangeMinimumValue"</c>) within
    /// the specification named <paramref name="specName"/> (canonical
    /// <c>"S-NNN"</c>).
    /// </summary>
    /// <param name="specName">Canonical product specification name (e.g. <c>"S-101"</c>).</param>
    /// <param name="attributeCode">Feature Catalogue attribute code; <c>null</c> yields <see langword="false"/>.</param>
    /// <param name="uom">The resolved unit of measure when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a unit was found; otherwise <see langword="false"/>.</returns>
    public bool TryGetUnit(string specName, string? attributeCode, out UnitOfMeasure uom)
    {
        uom = null!;
        if (string.IsNullOrEmpty(specName) || string.IsNullOrEmpty(attributeCode))
        {
            return false;
        }

        var byCode = _byCodeBySpec.GetOrAdd(specName, BuildIndex);
        if (byCode is null)
        {
            return false;
        }

        if (byCode.TryGetValue(attributeCode, out var found))
        {
            uom = found;
            return true;
        }

        return false;
    }

    private IReadOnlyDictionary<string, UnitOfMeasure>? BuildIndex(string specName)
    {
        FeatureCatalogue? catalogue;
        try
        {
            catalogue = _catalogues.GetCatalogue(specName);
        }
        catch (Exception)
        {
            catalogue = null;
        }

        if (catalogue is null)
        {
            return null;
        }

        var byCode = new Dictionary<string, UnitOfMeasure>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in catalogue.SimpleAttributes)
        {
            if (attr.Uom is { } uom && !string.IsNullOrEmpty(attr.Code))
            {
                byCode[attr.Code] = uom;
            }
        }

        return byCode;
    }
}
