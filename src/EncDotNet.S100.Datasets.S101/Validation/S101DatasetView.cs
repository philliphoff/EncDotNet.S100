using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.S101.Validation;

/// <summary>
/// Spec-aligned façade over an <see cref="S101Document"/>, exposing
/// feature-type acronyms, attribute acronyms, and spatial-record
/// lookup helpers in the vocabulary used by the S-101 Feature
/// Catalogue rather than the numeric codes of the ISO 8211 encoding.
/// </summary>
/// <remarks>
/// <para>
/// Implements the input-model decision recorded in
/// <c>docs/design/non-gml-validation.md</c> §3.1 — option (b),
/// "thin spec-aligned façade". The façade is constructed once per
/// dataset by <see cref="From(S101Document, FeatureCatalogueDecoder?)"/>;
/// rule packs (e.g. <see cref="S101DatasetRules"/>) read from
/// <see cref="Features"/>, <see cref="OfType(string)"/>, and
/// <see cref="TryGetSpatial(S101SpatialAssociation, out object?)"/>.
/// </para>
/// <para>
/// The underlying <see cref="S101Document"/> is reachable through
/// <see cref="Raw"/> for the rare rule that needs to dive below the
/// façade. By design (§3.1 closing paragraph) the façade does NOT
/// expose <see cref="S101FeatureRecord"/> as part of its surface,
/// keeping the door open for a future fully typed projection
/// (option (d)) without breaking existing rule code.
/// </para>
/// </remarks>
public sealed class S101DatasetView
{
    private readonly Dictionary<string, List<S101FeatureView>> _byType;

    private S101DatasetView(
        S101Document raw,
        ImmutableArray<S101FeatureView> features,
        ImmutableArray<S101FeatureView> unresolvedFeatures,
        ImmutableArray<S101DiagnosticView> diagnostics,
        FeatureCatalogueDecoder? decoder)
    {
        Raw = raw;
        Features = features;
        UnresolvedFeatures = unresolvedFeatures;
        Diagnostics = diagnostics;
        Decoder = decoder;
        _byType = new Dictionary<string, List<S101FeatureView>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in features)
        {
            if (f.FeatureTypeAcronym is null) continue;
            if (!_byType.TryGetValue(f.FeatureTypeAcronym, out var list))
            {
                list = new List<S101FeatureView>();
                _byType[f.FeatureTypeAcronym] = list;
            }
            list.Add(f);
        }
    }

    /// <summary>The underlying parsed S-101 document.</summary>
    public S101Document Raw { get; }

    /// <summary>
    /// The bundled <see cref="FeatureCatalogueDecoder"/> the façade
    /// was constructed with, or <c>null</c> when no catalogue was
    /// available. Rule packs use the decoder to enumerate attribute
    /// bindings and listed values for FC-conformance rules
    /// (<c>S101-R-1.2</c>, <c>S101-R-4.1</c>).
    /// </summary>
    public FeatureCatalogueDecoder? Decoder { get; }

    /// <summary>
    /// Every feature in the document, in dataset order, including
    /// features whose <see cref="S101FeatureView.FeatureTypeAcronym"/>
    /// did not resolve. Use <see cref="UnresolvedFeatures"/> to
    /// enumerate only the unresolved subset.
    /// </summary>
    public ImmutableArray<S101FeatureView> Features { get; }

    /// <summary>
    /// Sidecar view of every feature in the document whose feature
    /// type code did not resolve through
    /// <see cref="S101Document.FeatureTypeCatalogue"/>. Used by
    /// <c>S101-R-1.1</c>.
    /// </summary>
    public ImmutableArray<S101FeatureView> UnresolvedFeatures { get; }

    /// <summary>
    /// Parse-time diagnostics surfaced by <see cref="S101DocumentReader"/>.
    /// Always empty for v1 per the design note §5.2 Stance A; the
    /// surface is present so a future reader change can populate it
    /// without breaking the façade contract.
    /// </summary>
    public IReadOnlyList<S101DiagnosticView> Diagnostics { get; }

    /// <summary>
    /// Returns every resolved feature whose
    /// <see cref="S101FeatureView.FeatureTypeAcronym"/> matches
    /// <paramref name="acronym"/> (case-insensitive). Returns an
    /// empty sequence when no features of that type exist.
    /// </summary>
    public IEnumerable<S101FeatureView> OfType(string acronym)
    {
        if (string.IsNullOrEmpty(acronym))
            return Array.Empty<S101FeatureView>();
        return _byType.TryGetValue(acronym, out var list)
            ? list
            : Array.Empty<S101FeatureView>();
    }

    /// <summary>
    /// Resolves a <see cref="S101SpatialAssociation"/> into the
    /// matching spatial record. Returns <c>true</c> with the record
    /// boxed as <see cref="object"/> when the
    /// <c>(RecordName, RecordId)</c> pair maps into one of the
    /// document's spatial dictionaries (RCNM 110 = Point, 115 =
    /// MultiPoint, 120 = Curve, 125 = CompositeCurve, 130 = Surface);
    /// returns <c>false</c> when the reference is dangling.
    /// </summary>
    /// <remarks>
    /// Surfaces are returned as <see cref="S101SurfaceRecord"/>,
    /// composite curves as <see cref="S101CompositeCurveRecord"/>,
    /// curves as <see cref="S101CurveSegmentRecord"/>, points as
    /// <see cref="S101PointRecord"/>, and multi-points as
    /// <see cref="S101MultiPointRecord"/>. Rule code that needs a
    /// specific shape pattern-matches the returned <c>object</c>.
    /// </remarks>
    public bool TryGetSpatial(S101SpatialAssociation association, out object? record)
    {
        record = null;
        switch (association.RecordName)
        {
            case 110:
                if (Raw.Points.TryGetValue(association.RecordId, out var p))
                {
                    record = p; return true;
                }
                break;
            case 115:
                if (Raw.MultiPoints.TryGetValue(association.RecordId, out var mp))
                {
                    record = mp; return true;
                }
                break;
            case 120:
                if (Raw.CurveSegments.TryGetValue(association.RecordId, out var cs))
                {
                    record = cs; return true;
                }
                break;
            case 125:
                if (Raw.CompositeCurves.TryGetValue(association.RecordId, out var cc))
                {
                    record = cc; return true;
                }
                break;
            case 130:
                if (Raw.Surfaces.TryGetValue(association.RecordId, out var s))
                {
                    record = s; return true;
                }
                break;
        }
        return false;
    }

    /// <summary>
    /// Returns the canonical <c>RelatedFeatureId</c> string for a
    /// spatial association per design §4.3 — e.g. <c>"surf:42"</c>,
    /// <c>"curve:17"</c>, <c>"point:9"</c>. Includes the tagged
    /// prefix so RCIDs (which are NOT unique across record-name
    /// buckets) remain disambiguated.
    /// </summary>
    public static string SpatialRelatedId(S101SpatialAssociation association)
    {
        var prefix = association.RecordName switch
        {
            110 => "point",
            115 => "multipoint",
            120 => "curve",
            125 => "composite",
            130 => "surf",
            _ => $"rcnm{association.RecordName}",
        };
        return $"{prefix}:{association.RecordId}";
    }

    /// <summary>
    /// Builds a façade for the supplied document, resolving every
    /// feature's type code and attribute codes once against the
    /// embedded catalogues. The <paramref name="decoder"/> is
    /// retained for FC-conformance rules; pass <c>null</c> when no
    /// Feature Catalogue is available (in which case
    /// <c>S101-R-1.2</c> and <c>S101-R-4.1</c> degrade to no-ops).
    /// </summary>
    /// <param name="document">The parsed S-101 document.</param>
    /// <param name="decoder">
    /// The bundled <see cref="FeatureCatalogueDecoder"/> for S-101,
    /// or <c>null</c> when validation runs without a catalogue.
    /// </param>
    public static S101DatasetView From(S101Document document, FeatureCatalogueDecoder? decoder)
    {
        ArgumentNullException.ThrowIfNull(document);

        var featuresBuilder = ImmutableArray.CreateBuilder<S101FeatureView>(document.Features.Length);
        var unresolvedBuilder = ImmutableArray.CreateBuilder<S101FeatureView>();
        var attrCatalogue = document.AttributeTypeCatalogue;

        foreach (var record in document.Features)
        {
            document.FeatureTypeCatalogue.TryGetValue(record.FeatureTypeCode, out var typeAcronym);

            var attributeBuilder = ImmutableArray.CreateBuilder<S101AttributeView>(record.Attributes.Length);
            foreach (var attr in record.Attributes)
            {
                attrCatalogue.TryGetValue(attr.NumericCode, out var acronym);
                attributeBuilder.Add(new S101AttributeView
                {
                    Acronym = acronym,
                    NumericCode = attr.NumericCode,
                    Index = attr.Index,
                    Value = attr.Value,
                });
            }

            var view = new S101FeatureView(record, typeAcronym, attributeBuilder.ToImmutable(), attrCatalogue);
            featuresBuilder.Add(view);
            if (typeAcronym is null)
                unresolvedBuilder.Add(view);
        }

        return new S101DatasetView(
            document,
            featuresBuilder.MoveToImmutable(),
            unresolvedBuilder.ToImmutable(),
            ImmutableArray<S101DiagnosticView>.Empty,
            decoder);
    }
}

/// <summary>
/// A parse-time diagnostic surfaced by the S-101 reader. Present as
/// part of the façade contract so the <c>S101-PROJ-PARSE</c> rule
/// (design §5.2) has a stable shape to iterate; always empty in v1
/// per the Stance A decision documented in the design note.
/// </summary>
/// <remarks>
/// Promote to a non-empty surface only once
/// <see cref="S101DocumentReader"/> is changed to emit warnings
/// alongside the parsed document (Stance B).
/// </remarks>
public sealed class S101DiagnosticView
{
    /// <summary>A stable, machine-readable code identifying the diagnostic.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable description of the diagnostic.</summary>
    public required string Message { get; init; }
}
