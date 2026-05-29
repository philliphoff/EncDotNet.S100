using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S101.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of normative rules
/// for an S-101 dataset, evaluated through the spec-vocabulary
/// <see cref="S101DatasetView"/> façade. Rule identifiers follow
/// <c>S101-R-{clause}</c> for normative rules and
/// <c>S101-PROJ-{kind}</c> for projection-diagnostic surrogates.
/// </summary>
/// <remarks>
/// <para>
/// This is the V-4 rule pack as defined in
/// <c>docs/design/non-gml-validation.md</c> §6.4. The pack reads from
/// the thin façade introduced in the same PR (input model option (b),
/// design §3.1); rules cite the relevant spec clause and the
/// <c>s101-enc</c> skill review checklist item they implement.
/// </para>
/// <para>
/// Per-finding payload conventions follow design §4.3:
/// <list type="bullet">
/// <item><description>S-101 feature findings — <c>RelatedFeatureId = "{agency}:{FIDN}.{FIDS}"</c></description></item>
/// <item><description>Spatial record findings — <c>RelatedFeatureId = "surf:{RCID}"</c> / <c>"curve:{RCID}"</c> / <c>"point:{RCID}"</c></description></item>
/// </list>
/// </para>
/// </remarks>
public static class S101DatasetRules
{
    /// <summary>
    /// <c>S101-R-1.1</c> — Every feature record's
    /// <see cref="S101FeatureRecord.FeatureTypeCode"/> must resolve to
    /// a Feature Catalogue acronym through the dataset's embedded
    /// <see cref="S101Document.FeatureTypeCatalogue"/>.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-101 Edition 1.2.0 §6 (Encoding) and S-100
    /// Part 10a §4.3 — the FRID record's feature type code is a
    /// numeric reference into the embedded feature-type catalogue.
    /// Implements the <c>s101-enc</c> skill review checklist item
    /// "feature type code resolves to FC acronym".
    /// </remarks>
    public static IValidationRule<S101DatasetView> FeatureTypeResolves { get; } =
        ValidationRuleBuilder.RuleFor<S101DatasetView>("S101-R-1.1")
            .WithDescription("Every feature record's feature type code must resolve to an FC acronym.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((view, _) =>
            {
                var datasetId = view.Raw.Identification.DatasetName;
                var findings = new List<ValidationFinding>();
                foreach (var f in view.UnresolvedFeatures)
                {
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S101-R-1.1",
                        Severity = ValidationSeverity.Error,
                        Message = $"Feature (RCID {f.Raw.RecordId}) has feature type code " +
                            $"{f.Raw.FeatureTypeCode} which is not declared in the dataset's " +
                            "feature type catalogue.",
                        DatasetId = datasetId,
                        RelatedFeatureId = f.FoidKey,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S101-R-1.2</c> — Every attribute on every feature must
    /// resolve through <see cref="S101Document.AttributeTypeCatalogue"/>
    /// AND its acronym must be a permitted attribute of the host
    /// feature class per the Feature Catalogue (including inherited
    /// bindings from any super-type chain).
    /// </summary>
    /// <remarks>
    /// Spec reference: S-101 Edition 1.2.0 §6, S-100 Part 5 (ISO
    /// 19110) — feature-type attribute bindings define the set of
    /// attributes a feature instance may carry. Implements the
    /// <c>s101-enc</c> skill review checklist items "attribute codes
    /// resolve" and "attribute bindings match FC for feature class".
    /// Degrades to a no-op when no <see cref="FeatureCatalogueDecoder"/>
    /// is available (the decoder lookup returned <c>null</c>).
    /// </remarks>
    public static IValidationRule<S101DatasetView> AttributeBindingsValid { get; } =
        ValidationRuleBuilder.RuleFor<S101DatasetView>("S101-R-1.2")
            .WithDescription("Every attribute's numeric code must resolve and its acronym must be a permitted attribute of the host feature class.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((view, _) =>
            {
                var findings = new List<ValidationFinding>();
                var datasetId = view.Raw.Identification.DatasetName;

                foreach (var f in view.Features)
                {
                    foreach (var a in f.Attributes)
                    {
                        if (a.Acronym is null)
                        {
                            findings.Add(new ValidationFinding
                            {
                                RuleId = "S101-R-1.2",
                                Severity = ValidationSeverity.Error,
                                Message = $"Feature '{f.FeatureTypeAcronym ?? f.Raw.FeatureTypeCode.ToString(CultureInfo.InvariantCulture)}' " +
                                    $"(RCID {f.Raw.RecordId}) carries attribute code {a.NumericCode} " +
                                    "which is not declared in the dataset's attribute type catalogue.",
                                DatasetId = datasetId,
                                RelatedFeatureId = f.FoidKey,
                            });
                        }
                    }

                    if (view.Decoder is null || f.FeatureTypeAcronym is null)
                        continue;

                    var permitted = CollectPermittedAttributes(view.Decoder, f.FeatureTypeAcronym);
                    if (permitted is null)
                        continue; // Unknown feature class; R-1.1 already fires.

                    foreach (var a in f.Attributes)
                    {
                        if (a.Acronym is null) continue;
                        if (permitted.Contains(a.Acronym)) continue;

                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S101-R-1.2",
                            Severity = ValidationSeverity.Error,
                            Message = $"Feature '{f.FeatureTypeAcronym}' (RCID {f.Raw.RecordId}) carries " +
                                $"attribute '{a.Acronym}' which is not bound to that feature class in the " +
                                "S-101 Feature Catalogue.",
                            DatasetId = datasetId,
                            RelatedFeatureId = f.FoidKey,
                        });
                    }
                }

                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S101-R-2.1</c> — Feature Object Identifier (FOID) triples
    /// (<c>producingAgency</c>, <c>FIDN</c>, <c>FIDS</c>) must be
    /// unique across <see cref="S101DatasetView.Features"/>. Each
    /// duplicate occurrence emits one finding; the first occurrence
    /// is the anchor and is not flagged.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-101 Edition 1.2.0 §6 / S-100 Part 10a §4.3.2
    /// — the FOID composite key uniquely identifies a feature
    /// instance within an exchange set. Implements the design
    /// note §8.4 ("FOID uniqueness — one finding per duplicate;
    /// first occurrence is the anchor") and the <c>s101-enc</c>
    /// skill review checklist item "FOID uniqueness".
    /// </remarks>
    public static IValidationRule<S101DatasetView> FoidUniqueness { get; } =
        ValidationRuleBuilder.RuleFor<S101DatasetView>("S101-R-2.1")
            .WithDescription("Feature Object Identifier triples must be unique within the dataset.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((view, _) =>
            {
                var datasetId = view.Raw.Identification.DatasetName;
                var anchors = new Dictionary<string, S101FeatureView>(StringComparer.Ordinal);
                var findings = new List<ValidationFinding>();
                foreach (var f in view.Features)
                {
                    var key = f.FoidKey;
                    if (anchors.TryGetValue(key, out var anchor))
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S101-R-2.1",
                            Severity = ValidationSeverity.Error,
                            Message = $"Duplicate FOID '{key}': feature RCID {f.Raw.RecordId} repeats the " +
                                $"identifier first used by feature RCID {anchor.Raw.RecordId}.",
                            DatasetId = datasetId,
                            RelatedFeatureId = key,
                        });
                    }
                    else
                    {
                        anchors[key] = f;
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S101-R-3.1</c> — Every spatial association on every feature
    /// must reference a record present in the matching
    /// <c>(RecordName, RecordId)</c> dictionary of the document.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10a §4.3.3 — the SPAS field carries
    /// (RCNM, RCID, orientation) tuples that must resolve to a point
    /// (110), multipoint (115), curve (120), composite curve (125),
    /// or surface (130) record present in the same dataset.
    /// Implements the <c>s101-enc</c> skill review checklist item
    /// "spatial association referential integrity".
    /// </remarks>
    public static IValidationRule<S101DatasetView> SpatialAssociationsResolve { get; } =
        ValidationRuleBuilder.RuleFor<S101DatasetView>("S101-R-3.1")
            .WithDescription("Every spatial association must reference an existing spatial record.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((view, _) =>
            {
                var datasetId = view.Raw.Identification.DatasetName;
                var findings = new List<ValidationFinding>();
                foreach (var f in view.Features)
                {
                    foreach (var sa in f.SpatialAssociations)
                    {
                        if (view.TryGetSpatial(sa, out var _record)) continue;
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S101-R-3.1",
                            Severity = ValidationSeverity.Error,
                            Message = $"Feature '{f.FeatureTypeAcronym ?? f.Raw.FeatureTypeCode.ToString(CultureInfo.InvariantCulture)}' " +
                                $"(RCID {f.Raw.RecordId}) references spatial record " +
                                $"(RCNM={sa.RecordName}, RCID={sa.RecordId}) which is not present in the dataset.",
                            DatasetId = datasetId,
                            RelatedFeatureId = f.FoidKey,
                        });
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S101-R-3.2</c> — Every surface record's rings (exterior and
    /// interior) must be closed and contain at least three distinct
    /// vertices. A ring is closed when its constituent curves form a
    /// cycle (end of the last curve equals start of the first); a
    /// degenerate ring (fewer than three distinct points) is reported
    /// regardless of closure per the design note §8.2.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10a §4.3.3 (Surface records) and
    /// ISO 19107 — a polygon boundary is a closed simple ring.
    /// Implements the <c>s101-enc</c> skill review checklist item
    /// "surface ring closure".
    /// </remarks>
    public static IValidationRule<S101DatasetView> SurfaceRingsClosed { get; } =
        ValidationRuleBuilder.RuleFor<S101DatasetView>("S101-R-3.2")
            .WithDescription("Surface rings must be closed and contain at least three distinct vertices.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((view, _) =>
            {
                var datasetId = view.Raw.Identification.DatasetName;
                var findings = new List<ValidationFinding>();
                foreach (var surface in view.Raw.Surfaces.Values)
                {
                    EvaluateSurface(view, surface, datasetId, findings);
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S101-R-3.3</c> — Composite curve continuity: when iterating
    /// a composite curve's component curves in their declared order
    /// (and honouring per-component orientation), the end point of
    /// each curve segment must equal the start point of the next.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10a §4.3.3 (Composite curve
    /// records) and ISO 19107 — a composite curve is a connected
    /// sequence of curve segments.
    /// </remarks>
    public static IValidationRule<S101DatasetView> CompositeCurveContinuity { get; } =
        ValidationRuleBuilder.RuleFor<S101DatasetView>("S101-R-3.3")
            .WithDescription("Composite curve component endpoints must be continuous.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((view, _) =>
            {
                var datasetId = view.Raw.Identification.DatasetName;
                var findings = new List<ValidationFinding>();
                foreach (var composite in view.Raw.CompositeCurves.Values)
                {
                    EvaluateComposite(view, composite, datasetId, findings);
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S101-R-4.1</c> — Enumerated simple-attribute values must be
    /// one of the listed values declared for that attribute by the
    /// Feature Catalogue. Issued as a warning because authoring tools
    /// sometimes carry out-of-domain codes that are nonetheless
    /// renderable.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 5 (ISO 19110) — listed-value
    /// attributes constrain values to a closed code list.
    /// Implements the <c>s101-enc</c> skill review checklist item
    /// "enumerated attribute domain conformance". Degrades to a
    /// no-op when no decoder is available.
    /// </remarks>
    public static IValidationRule<S101DatasetView> EnumeratedAttributeDomain { get; } =
        ValidationRuleBuilder.RuleFor<S101DatasetView>("S101-R-4.1")
            .WithDescription("Enumerated attribute values must be one of the listed values declared by the Feature Catalogue.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((view, _) =>
            {
                if (view.Decoder is null) return Array.Empty<ValidationFinding>();
                var datasetId = view.Raw.Identification.DatasetName;
                var findings = new List<ValidationFinding>();
                foreach (var f in view.Features)
                {
                    foreach (var a in f.Attributes)
                    {
                        if (a.Acronym is null) continue;
                        if (string.IsNullOrEmpty(a.Value)) continue;
                        if (!view.Decoder.IsEnumeratedAttribute(a.Acronym)) continue;
                        if (view.Decoder.ResolveListedValue(a.Acronym, a.Value) is not null) continue;

                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S101-R-4.1",
                            Severity = ValidationSeverity.Warning,
                            Message = $"Feature '{f.FeatureTypeAcronym ?? "?"}' (RCID {f.Raw.RecordId}) " +
                                $"attribute '{a.Acronym}' value '{a.Value}' is not a listed value of the " +
                                "S-101 Feature Catalogue.",
                            DatasetId = datasetId,
                            RelatedFeatureId = f.FoidKey,
                        });
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S101-R-5.1</c> — Every resolved coordinate (latitude /
    /// longitude after applying the dataset's coordinate
    /// multiplication factors) on every point, multi-point,
    /// curve-segment intermediate coordinate, and composite-curve
    /// vertex must lie within the WGS-84 bounds: latitude in
    /// <c>[-90, 90]</c>, longitude in <c>[-180, 180]</c>.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.2 (geographic coordinates
    /// for <c>EPSG:4326</c>) — the same bounds the GML packs apply.
    /// Issued as a warning because some chart compilers permit
    /// dateline-crossing geometry whose normalised form falls
    /// slightly outside the canonical range.
    /// </remarks>
    public static IValidationRule<S101DatasetView> CoordinatesInWgs84Range { get; } =
        ValidationRuleBuilder.RuleFor<S101DatasetView>("S101-R-5.1")
            .WithDescription("Resolved coordinates must lie within the WGS-84 latitude/longitude ranges.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((view, _) =>
            {
                var datasetId = view.Raw.Identification.DatasetName;
                var (cmfX, cmfY) = EffectiveCmf(view.Raw.StructureInfo);
                var findings = new List<ValidationFinding>();

                foreach (var p in view.Raw.Points.Values)
                {
                    var (lat, lon) = ToLatLon(p.Y, p.X, cmfY, cmfX);
                    EmitIfOutOfRange(findings, datasetId, $"point:{p.RecordId}", "Point", lat, lon);
                }
                foreach (var mp in view.Raw.MultiPoints.Values)
                {
                    foreach (var pt in mp.Points)
                    {
                        var (lat, lon) = ToLatLon(pt.Y, pt.X, cmfY, cmfX);
                        EmitIfOutOfRange(findings, datasetId, $"multipoint:{mp.RecordId}", "MultiPoint", lat, lon);
                    }
                }
                foreach (var cs in view.Raw.CurveSegments.Values)
                {
                    foreach (var ic in cs.IntermediateCoordinates)
                    {
                        var (lat, lon) = ToLatLon(ic.Y, ic.X, cmfY, cmfX);
                        EmitIfOutOfRange(findings, datasetId, $"curve:{cs.RecordId}", "CurveSegment", lat, lon);
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S101-R-5.2</c> — Every information association on every
    /// feature must reference an information-type record present in
    /// <see cref="S101Document.InformationTypes"/>. Issued as a
    /// warning because unresolved information associations degrade
    /// gracefully (the feature still renders) but lose the linked
    /// information content.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10a §4.3.3 (INAS field on feature
    /// records) — the association references an information record
    /// by RCID. Implements the <c>s101-enc</c> skill review checklist
    /// item "information association referential integrity".
    /// </remarks>
    public static IValidationRule<S101DatasetView> InformationAssociationsResolve { get; } =
        ValidationRuleBuilder.RuleFor<S101DatasetView>("S101-R-5.2")
            .WithDescription("Information associations must reference an existing information-type record.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((view, _) =>
            {
                var datasetId = view.Raw.Identification.DatasetName;
                var findings = new List<ValidationFinding>();
                foreach (var f in view.Features)
                {
                    foreach (var ia in f.InformationAssociations)
                    {
                        if (view.Raw.InformationTypes.ContainsKey(ia.RecordId)) continue;
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S101-R-5.2",
                            Severity = ValidationSeverity.Warning,
                            Message = $"Feature '{f.FeatureTypeAcronym ?? "?"}' (RCID {f.Raw.RecordId}) " +
                                $"references information record RCID {ia.RecordId} which is not present in " +
                                "the dataset.",
                            DatasetId = datasetId,
                            RelatedFeatureId = f.FoidKey,
                        });
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S101-PROJ-PARSE</c> — Placeholder for parser warnings
    /// emitted by <see cref="S101DocumentReader"/>. The rule body is
    /// intentionally empty in v1; this entry commits the rule-id
    /// namespace so a future change that surfaces non-fatal reader
    /// diagnostics can populate findings without breaking
    /// downstream consumers.
    /// </summary>
    /// <remarks>
    /// Spec reference: design note §5.2 (Stance A) — the reader does
    /// not yet surface non-fatal warnings; promoting Stance B is a
    /// separate PR. Until then the rule reads
    /// <see cref="S101DatasetView.Diagnostics"/> (always empty) and
    /// emits one warning per diagnostic.
    /// </remarks>
    public static IValidationRule<S101DatasetView> ParserDiagnosticPlaceholder { get; } =
        ValidationRuleBuilder.RuleFor<S101DatasetView>("S101-PROJ-PARSE")
            .WithDescription("S-101 parser warnings (placeholder for future reader diagnostics surface).")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((view, _) =>
            {
                if (view.Diagnostics.Count == 0) return Array.Empty<ValidationFinding>();
                var datasetId = view.Raw.Identification.DatasetName;
                var findings = new List<ValidationFinding>(view.Diagnostics.Count);
                foreach (var d in view.Diagnostics)
                {
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S101-PROJ-PARSE",
                        Severity = ValidationSeverity.Warning,
                        Message = $"[{d.Code}] {d.Message}",
                        DatasetId = datasetId,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>The canonical default rule set for S-101 datasets.</summary>
    public static ValidationRuleSet<S101DatasetView> Default { get; } = new(
        FeatureTypeResolves,
        AttributeBindingsValid,
        FoidUniqueness,
        SpatialAssociationsResolve,
        SurfaceRingsClosed,
        CompositeCurveContinuity,
        EnumeratedAttributeDomain,
        CoordinatesInWgs84Range,
        InformationAssociationsResolve,
        ParserDiagnosticPlaceholder);

    /// <summary>
    /// Convenience wrapper around <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/>
    /// using the <see cref="Default"/> rule set.
    /// </summary>
    public static ValidationReport Validate(S101DatasetView view, ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        return Default.Run(view, context);
    }

    // ----- helpers -----

    /// <summary>
    /// Collects the set of attribute acronyms permitted on a feature
    /// class, walking the super-type chain to gather inherited
    /// bindings. Returns <c>null</c> when the feature class itself
    /// is not present in the catalogue (so R-1.1 owns the report).
    /// </summary>
    private static HashSet<string>? CollectPermittedAttributes(FeatureCatalogueDecoder decoder, string acronym)
    {
        var fc = decoder.Catalogue;
        var byCode = new Dictionary<string, FeatureType>(StringComparer.OrdinalIgnoreCase);
        foreach (var ft in fc.FeatureTypes) byCode[ft.Code] = ft;
        if (!byCode.TryGetValue(acronym, out var current))
            return null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var permitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (current is not null && seen.Add(current.Code))
        {
            foreach (var binding in current.AttributeBindings)
            {
                if (!string.IsNullOrEmpty(binding.AttributeRef))
                    permitted.Add(binding.AttributeRef);
            }
            if (string.IsNullOrEmpty(current.SuperType)) break;
            byCode.TryGetValue(current.SuperType, out current);
        }
        return permitted;
    }

    private static (uint cmfX, uint cmfY) EffectiveCmf(S101DatasetStructureInfo si)
    {
        // S-100 Part 10a §4.3.1 — CMF defaults to 10^7 when the DSSI
        // value is zero (i.e. unset).
        var cmfX = si.CoordinateMultiplicationFactorX == 0 ? 10_000_000u : si.CoordinateMultiplicationFactorX;
        var cmfY = si.CoordinateMultiplicationFactorY == 0 ? 10_000_000u : si.CoordinateMultiplicationFactorY;
        return (cmfX, cmfY);
    }

    private static (double lat, double lon) ToLatLon(int y, int x, uint cmfY, uint cmfX)
        => ((double)y / cmfY, (double)x / cmfX);

    private static void EmitIfOutOfRange(
        List<ValidationFinding> sink, string? datasetId, string relatedId, string kind, double lat, double lon)
    {
        bool latOk = lat is >= -90 and <= 90;
        bool lonOk = lon is >= -180 and <= 180;
        if (latOk && lonOk) return;
        sink.Add(new ValidationFinding
        {
            RuleId = "S101-R-5.1",
            Severity = ValidationSeverity.Warning,
            Message = $"{kind} (RCID from {relatedId}) coordinate (lat={lat}, lon={lon}) is outside the WGS-84 range.",
            Point = new GeoPosition(Latitude: lat, Longitude: lon),
            DatasetId = datasetId,
            RelatedFeatureId = relatedId,
        });
    }

    private static void EvaluateSurface(
        S101DatasetView view,
        S101SurfaceRecord surface,
        string? datasetId,
        List<ValidationFinding> findings)
    {
        var relatedId = $"surf:{surface.RecordId}";
        var (cmfX, cmfY) = EffectiveCmf(view.Raw.StructureInfo);

        var rings = GroupRings(surface);
        foreach (var ring in rings)
        {
            var pts = ResolveRingPoints(view, surface, ring, cmfX, cmfY, datasetId, relatedId, findings);
            if (pts is null) continue;

            var distinct = CountDistinct(pts);
            if (distinct < 3)
            {
                findings.Add(new ValidationFinding
                {
                    RuleId = "S101-R-3.2",
                    Severity = ValidationSeverity.Error,
                    Message = $"Surface (RCID {surface.RecordId}) ring has only {distinct} distinct vertex/vertices; " +
                        "a ring requires at least three.",
                    DatasetId = datasetId,
                    RelatedFeatureId = relatedId,
                });
                continue;
            }

            var first = pts[0];
            var last = pts[^1];
            if (Math.Abs(first.X - last.X) > 0 || Math.Abs(first.Y - last.Y) > 0)
            {
                findings.Add(new ValidationFinding
                {
                    RuleId = "S101-R-3.2",
                    Severity = ValidationSeverity.Error,
                    Message = $"Surface (RCID {surface.RecordId}) ring is not closed: first vertex " +
                        $"({first.Y}, {first.X}) ≠ last vertex ({last.Y}, {last.X}).",
                    DatasetId = datasetId,
                    RelatedFeatureId = relatedId,
                });
            }
        }
    }

    private static List<List<S101RingAssociation>> GroupRings(S101SurfaceRecord surface)
    {
        // RIAS lists ring associations consecutively, exterior (USAG=1)
        // first followed by interior (USAG=2) rings; each ring is a
        // contiguous run sharing the same Usage value. We don't have
        // an explicit per-ring delimiter so the simplest grouping is
        // "split whenever Usage transitions from 1 → 2"; multiple
        // interior rings (USAG=2) belonging to the same surface would
        // be encoded by repeated curves in the same run. For ring
        // closure analysis we evaluate the whole run as one boundary
        // path — sufficient for v1.
        var groups = new List<List<S101RingAssociation>>();
        List<S101RingAssociation>? current = null;
        byte previousUsage = 0;
        foreach (var ra in surface.RingAssociations)
        {
            if (current is null || ra.Usage != previousUsage)
            {
                current = new List<S101RingAssociation>();
                groups.Add(current);
                previousUsage = ra.Usage;
            }
            current.Add(ra);
        }
        return groups;
    }

    private static List<(int Y, int X)>? ResolveRingPoints(
        S101DatasetView view,
        S101SurfaceRecord surface,
        List<S101RingAssociation> ring,
        uint cmfX,
        uint cmfY,
        string? datasetId,
        string relatedId,
        List<ValidationFinding> findings)
    {
        var pts = new List<(int Y, int X)>();
        foreach (var ra in ring)
        {
            var curve = ResolveCurveSegments(view, ra.RecordName, ra.RecordId);
            if (curve is null)
            {
                // Dangling spatial reference — R-3.1 owns this finding;
                // skip the ring entirely to avoid a duplicate report.
                return null;
            }
            AppendCurvePoints(view, curve, ra.Orientation == 2, pts);
        }
        _ = (cmfX, cmfY, datasetId, relatedId, findings);
        return pts;
    }

    private static IReadOnlyList<S101CurveSegmentRecord>? ResolveCurveSegments(
        S101DatasetView view, byte recordName, uint recordId)
    {
        // A ring component is either a curve (RCNM=120) or a composite
        // curve (RCNM=125). For composite curves, recursively gather
        // the underlying curve segments in declared order.
        if (recordName == 120)
        {
            return view.Raw.CurveSegments.TryGetValue(recordId, out var c)
                ? new[] { c }
                : null;
        }
        if (recordName == 125)
        {
            if (!view.Raw.CompositeCurves.TryGetValue(recordId, out var composite)) return null;
            var list = new List<S101CurveSegmentRecord>();
            foreach (var cu in composite.CurveComponents)
            {
                if (!view.Raw.CurveSegments.TryGetValue(cu.RecordId, out var c)) return null;
                list.Add(c);
            }
            return list;
        }
        return null;
    }

    private static void AppendCurvePoints(
        S101DatasetView view,
        IReadOnlyList<S101CurveSegmentRecord> segments,
        bool reverseRing,
        List<(int Y, int X)> sink)
    {
        // We walk each curve segment in begin→end order and append
        // begin point, intermediate coordinates, and end point. Per-
        // segment orientation (forward/reverse) is encoded at the
        // ring level; for v1 we honour the ring orientation by
        // reversing the final point sequence if requested. The
        // segment-level orientation flag is not surfaced through
        // S101RingAssociation, so segment-level reversal is left to
        // a v-next refinement.
        foreach (var seg in segments)
        {
            (int Y, int X)? begin = null, end = null;
            foreach (var pa in seg.PointAssociations)
            {
                if (!view.Raw.Points.TryGetValue(pa.RecordId, out var p)) continue;
                if (pa.Topology == 1) begin = (p.Y, p.X);
                else if (pa.Topology == 2) end = (p.Y, p.X);
            }
            if (begin is { } b) sink.Add(b);
            foreach (var ic in seg.IntermediateCoordinates) sink.Add((ic.Y, ic.X));
            if (end is { } e) sink.Add(e);
        }
        if (reverseRing) sink.Reverse();
    }

    private static int CountDistinct(List<(int Y, int X)> pts)
    {
        var set = new HashSet<(int, int)>();
        foreach (var p in pts) set.Add(p);
        return set.Count;
    }

    private static void EvaluateComposite(
        S101DatasetView view,
        S101CompositeCurveRecord composite,
        string? datasetId,
        List<ValidationFinding> findings)
    {
        var relatedId = $"composite:{composite.RecordId}";
        (int Y, int X)? previousEnd = null;
        int index = 0;
        foreach (var cu in composite.CurveComponents)
        {
            if (!view.Raw.CurveSegments.TryGetValue(cu.RecordId, out var seg))
            {
                // R-3.1 handles dangling references; skip continuity
                // analysis for this composite.
                return;
            }
            (int Y, int X)? begin = null, end = null;
            foreach (var pa in seg.PointAssociations)
            {
                if (!view.Raw.Points.TryGetValue(pa.RecordId, out var p)) continue;
                if (pa.Topology == 1) begin = (p.Y, p.X);
                else if (pa.Topology == 2) end = (p.Y, p.X);
            }

            // Honour the per-component orientation flag.
            if (cu.Orientation == 2) (begin, end) = (end, begin);

            if (previousEnd is { } pe && begin is { } b && (pe.Y != b.Y || pe.X != b.X))
            {
                findings.Add(new ValidationFinding
                {
                    RuleId = "S101-R-3.3",
                    Severity = ValidationSeverity.Error,
                    Message = $"Composite curve (RCID {composite.RecordId}) is not continuous at " +
                        $"component index {index}: previous endpoint ({pe.Y}, {pe.X}) ≠ next start " +
                        $"({b.Y}, {b.X}).",
                    DatasetId = datasetId,
                    RelatedFeatureId = relatedId,
                });
            }
            previousEnd = end;
            index++;
        }
    }
}
