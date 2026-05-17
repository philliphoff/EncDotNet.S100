using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S125.DataModel;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S125.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of normative rules
/// for an S-125 <see cref="S125AtonDataset"/>. Rule identifiers follow
/// the convention <c>S125-R-{clause}</c>, where <c>{clause}</c> traces
/// to the relevant section of the S-125 specification
/// (Edition 1.0.0 — Marine Aids to Navigation).
/// </summary>
/// <remarks>
/// <para>
/// The pilot rule set focuses on Tier-1 (schema-shape) and Tier-2
/// (spec-semantic) rules that can be evaluated against a single
/// <see cref="S125AtonDataset"/> in isolation. Tier-3 cross-dataset
/// rules (e.g. checking an AtoN's charted position against an S-101
/// chart) will be added in a follow-up once the MCP
/// <c>validate_all</c> surface is wired up — they need access to
/// sibling datasets via <see cref="ValidationContext.Services"/>.
/// </para>
/// <para>
/// <b>Deviation from the S-421 pilot.</b> Projection-time issues
/// surfaced by <see cref="S125AtonDataset.From"/> (unresolved xlinks,
/// duplicate ids, etc.) are reported through the
/// <c>ProjectionDiagnostic</c> out-parameter rather than carried on
/// the typed model, so there is no rule that mirrors S-421's
/// "information binding sanity" check. Callers that care about those
/// diagnostics should capture them at projection time.
/// </para>
/// </remarks>
public static class S125AtonRules
{
    /// <summary>
    /// <c>S125-R-1.1</c> — Every aid to navigation's position must fall
    /// within the WGS-84 ranges: latitude in [-90, +90] and longitude
    /// in [-180, +180].
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.2 — geographic coordinates for
    /// <c>EPSG:4326</c> are bounded. Aids without a position are
    /// ignored by this rule; geometry presence is covered separately
    /// where the spec requires it.
    /// </remarks>
    public static IValidationRule<S125AtonDataset> AidLatLonInRange { get; } =
        ValidationRuleBuilder.RuleFor<S125AtonDataset>("S125-R-1.1")
            .WithDescription("Aid to navigation positions must lie within the WGS-84 lat/lon ranges.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var aid in dataset.Aids)
                {
                    if (aid.Position is not { } pos) continue;

                    bool latOk = pos.Latitude is >= -90 and <= 90;
                    bool lonOk = pos.Longitude is >= -180 and <= 180;
                    if (latOk && lonOk) continue;

                    var details = (latOk, lonOk) switch
                    {
                        (false, true) => $"latitude {pos.Latitude} is outside [-90, +90]",
                        (true, false) => $"longitude {pos.Longitude} is outside [-180, +180]",
                        _ => $"latitude {pos.Latitude} and longitude {pos.Longitude} are both out of range",
                    };
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S125-R-1.1",
                        Severity = ValidationSeverity.Error,
                        Message = $"{aid.FeatureType} '{aid.Id}': {details}.",
                        Point = pos,
                        RelatedFeatureId = aid.Id,
                        DatasetId = dataset.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S125-R-1.2</c> — Every aid to navigation must have a
    /// dataset-unique <c>gml:id</c>.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.4 — every GML object exposed
    /// at the dataset level must carry a unique <c>gml:id</c> so that
    /// xlink references can resolve. Duplicate aid identifiers also
    /// break the typed model's <c>parent</c> / aggregation lookups.
    /// </remarks>
    public static IValidationRule<S125AtonDataset> AidIdsUnique { get; } =
        ValidationRuleBuilder.RuleFor<S125AtonDataset>("S125-R-1.2")
            .WithDescription("Aid to navigation gml:id values must be unique within the dataset.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var dupes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var aid in dataset.Aids)
                {
                    if (string.IsNullOrEmpty(aid.Id)) continue;
                    if (!seen.Add(aid.Id)) dupes.Add(aid.Id);
                }
                foreach (var dup in dupes)
                {
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S125-R-1.2",
                        Severity = ValidationSeverity.Error,
                        Message = $"Duplicate aid gml:id '{dup}'.",
                        RelatedFeatureId = dup,
                        DatasetId = dataset.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S125-R-2.1</c> — Every AIS aid to navigation
    /// (Physical / Synthetic / Virtual) must carry an <c>mMSICode</c>
    /// of exactly nine decimal digits.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-125 Edition 1.0.0 §PhysicalAISAidToNavigation,
    /// §SyntheticAISAidToNavigation, §VirtualAISAidToNavigation —
    /// <c>mMSICode</c> (Maritime Mobile Service Identity) is the
    /// 9-digit MMSI broadcast in the AIS message. The check is most
    /// load-bearing on <see cref="S125AisKind.Virtual"/> aids, which
    /// have no physical presence and so cannot be cross-validated
    /// against a real-world fix. The attribute is read from
    /// <see cref="IS125Aid.ExtraAttributes"/> because the typed model
    /// does not break it out as a strongly-typed property.
    /// </remarks>
    public static IValidationRule<S125AtonDataset> AisAidHasMmsi { get; } =
        ValidationRuleBuilder.RuleFor<S125AtonDataset>("S125-R-2.1")
            .WithDescription("Every AIS aid to navigation must carry a 9-digit mMSICode.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var aid in dataset.Aids.OfType<S125AisAton>())
                {
                    aid.ExtraAttributes.TryGetValue("mMSICode", out var mmsi);
                    if (string.IsNullOrEmpty(mmsi))
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S125-R-2.1",
                            Severity = ValidationSeverity.Error,
                            Message = $"AIS aid '{aid.Id}' ({aid.FeatureType}) is missing the required mMSICode attribute.",
                            Point = aid.Position,
                            RelatedFeatureId = aid.Id,
                            DatasetId = dataset.DatasetIdentifier,
                        });
                        continue;
                    }

                    if (mmsi.Length != 9 || !mmsi.All(char.IsAsciiDigit))
                    {
                        findings.Add(new ValidationFinding
                        {
                            RuleId = "S125-R-2.1",
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"AIS aid '{aid.Id}' ({aid.FeatureType}) has malformed mMSICode '{mmsi}'; " +
                                "must be exactly 9 decimal digits.",
                            Point = aid.Position,
                            RelatedFeatureId = aid.Id,
                            DatasetId = dataset.DatasetIdentifier,
                        });
                    }
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S125-R-3.1</c> — When an <c>AtonStatusInformation</c>
    /// carries a numeric <c>changeTypes</c> code it must fall within
    /// the closed enumeration declared by the Feature Catalogue.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-125 Edition 1.0.0 §changeTypes (Feature
    /// Catalogue listed values 1–5: Advance Notice of Change,
    /// Discrepancy, Proposed Change, Temporary Change, Permanent
    /// Change). The typed projection maps an out-of-range numeric
    /// value to <see cref="S125ChangeType.Unknown"/>, so this rule
    /// fires precisely when <see cref="S125AtonStatusInformation.ChangeTypeCode"/>
    /// is present but the projection refused to classify it.
    /// </remarks>
    public static IValidationRule<S125AtonDataset> ChangeTypeCodeInEnumeration { get; } =
        ValidationRuleBuilder.RuleFor<S125AtonDataset>("S125-R-3.1")
            .WithDescription("AtonStatusInformation changeTypes must be one of the FC listed values 1–5.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var info in dataset.StatusInformation)
                {
                    if (info.ChangeTypeCode is not { } code) continue;
                    if (info.ChangeType != S125ChangeType.Unknown) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S125-R-3.1",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"AtonStatusInformation '{info.Id}' has changeTypes code {code}; " +
                            "expected one of 1 (Advance Notice), 2 (Discrepancy), 3 (Proposed), " +
                            "4 (Temporary), 5 (Permanent).",
                        RelatedFeatureId = info.Id,
                        DatasetId = dataset.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S125-R-3.2</c> — When a status date range
    /// (<c>fixedDateRange</c> or <c>periodicDateRange</c>) carries
    /// both <c>dateStart</c> and <c>dateEnd</c>, the start must not
    /// be after the end.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-125 Edition 1.0.0 §fixedDateRange and
    /// §periodicDateRange — the complex attribute is composed of
    /// <c>dateStart</c> / <c>dateEnd</c>; the spec is silent on
    /// degenerate ranges but a status whose validity begins after it
    /// ends is non-actionable.
    /// </remarks>
    public static IValidationRule<S125AtonDataset> StatusDateRangeOrdered { get; } =
        ValidationRuleBuilder.RuleFor<S125AtonDataset>("S125-R-3.2")
            .WithDescription("Status date ranges must have dateStart ≤ dateEnd when both are present.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();

                void CheckRange(string ownerId, string label, S125DateRange? range)
                {
                    if (range is null) return;
                    if (range.Start is not { } s || range.End is not { } e) return;
                    if (s <= e) return;
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S125-R-3.2",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"AtonStatusInformation '{ownerId}' {label} has dateStart " +
                            $"({s:O}) after dateEnd ({e:O}).",
                        RelatedFeatureId = ownerId,
                        DatasetId = dataset.DatasetIdentifier,
                    });
                }

                foreach (var info in dataset.StatusInformation)
                {
                    CheckRange(info.Id, "fixedDateRange", info.FixedDateRange);
                    foreach (var p in info.PeriodicDateRanges)
                        CheckRange(info.Id, "periodicDateRange", p);
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S125-R-4.1</c> — Every <c>AtonAggregation</c> /
    /// <c>AtonAssociation</c> must bind at least one aid to
    /// navigation.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-125 Edition 1.0.0 §AtonAggregation,
    /// §AtonAssociation — these features are geometry-less containers
    /// whose entire purpose is to group AtoN by xlink. An aggregation
    /// with zero resolved members has no semantic content (either the
    /// aggregation is empty in the source or every xlink failed to
    /// resolve, which is itself a defect).
    /// </remarks>
    public static IValidationRule<S125AtonDataset> AggregationHasMembers { get; } =
        ValidationRuleBuilder.RuleFor<S125AtonDataset>("S125-R-4.1")
            .WithDescription("AtonAggregation / AtonAssociation must reference at least one aid to navigation.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var agg in dataset.Aggregations)
                {
                    if (!agg.Members.IsDefaultOrEmpty && agg.Members.Length > 0) continue;
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S125-R-4.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"{agg.Kind} '{agg.Id}' has no resolved member aids to navigation.",
                        RelatedFeatureId = agg.Id,
                        DatasetId = dataset.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>
    /// <c>S125-R-5.1</c> — Every <c>AtonStatusIndication</c> feature
    /// must have a position.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-125 Edition 1.0.0 §AtonStatusIndication —
    /// <c>permittedPrimitives = point</c>. The feature is portrayed
    /// at its position; an indication with no geometry cannot be
    /// drawn and adds nothing the bound <c>AtonStatusInformation</c>
    /// does not already convey through its xlink.
    /// </remarks>
    public static IValidationRule<S125AtonDataset> StatusIndicationHasPosition { get; } =
        ValidationRuleBuilder.RuleFor<S125AtonDataset>("S125-R-5.1")
            .WithDescription("AtonStatusIndication features must have a point geometry.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((dataset, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var ind in dataset.StatusIndications)
                {
                    if (ind.Position is not null) continue;
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S125-R-5.1",
                        Severity = ValidationSeverity.Warning,
                        Message = $"AtonStatusIndication '{ind.Id}' has no point geometry.",
                        RelatedFeatureId = ind.Id,
                        DatasetId = dataset.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    /// <summary>The canonical default rule set for S-125 AtoN datasets.</summary>
    public static ValidationRuleSet<S125AtonDataset> Default { get; } = new(
        AidLatLonInRange,
        AidIdsUnique,
        AisAidHasMmsi,
        ChangeTypeCodeInEnumeration,
        StatusDateRangeOrdered,
        AggregationHasMembers,
        StatusIndicationHasPosition);

    /// <summary>
    /// Convenience wrapper around <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/>
    /// using the <see cref="Default"/> rule set.
    /// </summary>
    public static ValidationReport Validate(S125AtonDataset dataset, ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return Default.Run(dataset, context);
    }
}
