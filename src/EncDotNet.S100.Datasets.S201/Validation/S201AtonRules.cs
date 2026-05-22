using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S201.DataModel;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S201.Validation;

/// <summary>
/// The default <see cref="ValidationRuleSet{TModel}"/> of normative rules
/// for an S-201 <see cref="S201AtonInventory"/>. Rule identifiers follow
/// the convention <c>S201-R-{clause}</c>, where <c>{clause}</c> traces
/// to the relevant section of the S-201 specification
/// (Edition 2.0.0 — Aids to Navigation Information) or to the S-100
/// framework (Part 10b §6.2 / §6.4 for coordinate and identifier
/// encoding).
/// </summary>
/// <remarks>
/// <para>
/// The pilot rule set focuses on Tier-1 (schema-shape) and Tier-2
/// (spec-semantic) rules that can be evaluated against a single
/// <see cref="S201AtonInventory"/> in isolation. Tier-3 cross-dataset
/// rules (e.g. cross-referencing an AtoN's charted position against an
/// S-101 chart) are out of scope here — they would reach sibling
/// datasets via <see cref="ValidationContext.Services"/>.
/// </para>
/// <para>
/// <b>Audience-driven design.</b> S-201 is the IALA AtoN-authority
/// exchange product, not an ECDIS portrayal product. Rules emphasise
/// integrity of the inventory chain (xlink resolution, equipment ↔
/// host-structure subordination, aggregation membership, AIS MMSI
/// conformance) over mariner-display concerns.
/// </para>
/// <para>
/// <b>Deviation from S-125.</b> Projection-time issues surfaced by
/// <see cref="S201AtonInventory.From"/> (unresolved xlinks, attribute
/// parse failures, duplicate ids) are reported through a
/// <c>ProjectionDiagnostic</c> out-parameter rather than carried on
/// the typed model, so the rule pack performs its own checks where
/// useful (see <see cref="AggregationMembersResolved"/>).
/// </para>
/// </remarks>
public static class S201AtonRules
{
    private static readonly ImmutableHashSet<AisAtonKind> MmsiRequiredAisKinds =
        ImmutableHashSet.Create(AisAtonKind.Physical, AisAtonKind.Synthetic);

    private static readonly ImmutableHashSet<int> ValidChangeTypes =
        ImmutableHashSet.Create(1, 2, 3, 4);

    // ── S201-R-1.1 — coordinates in WGS-84 range ─────────────────

    /// <summary>
    /// <c>S201-R-1.1</c> — Every coordinate of every AtoN geometry must
    /// fall within the WGS-84 ranges: latitude in [-90, +90] and
    /// longitude in [-180, +180].
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.2 — geographic coordinates for
    /// <c>EPSG:4326</c> are bounded. AtoNs without geometry
    /// (geometry-less container features such as <c>AtonAggregation</c>)
    /// are ignored by this rule.
    /// </remarks>
    public static IValidationRule<S201AtonInventory> CoordinatesInWgs84Range { get; } =
        ValidationRuleBuilder.RuleFor<S201AtonInventory>("S201-R-1.1")
            .WithDescription("AtoN coordinates must lie within the WGS-84 lat/lon ranges.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((inventory, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var aton in inventory.AtoNs)
                {
                    if (aton.Coordinates.IsDefaultOrEmpty) continue;
                    foreach (var pos in aton.Coordinates)
                    {
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
                            RuleId = "S201-R-1.1",
                            Severity = ValidationSeverity.Error,
                            Message = $"{aton.FeatureClass} '{aton.Id}': {details}.",
                            Point = pos,
                            RelatedFeatureId = aton.Id,
                            DatasetId = inventory.DatasetIdentifier,
                        });
                    }
                }
                return findings;
            })
            .Build();

    // ── S201-R-1.2 — gml:id uniqueness ───────────────────────────

    /// <summary>
    /// <c>S201-R-1.2</c> — Every AtoN, aggregation, association, and
    /// information-type instance must carry a dataset-unique
    /// <c>gml:id</c>.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-100 Part 10b §6.4 — every GML object exposed
    /// at the dataset level must carry a unique <c>gml:id</c> so that
    /// <c>xlink:href</c> references resolve deterministically.
    /// Duplicate identifiers also break the typed projection's
    /// back-resolved <c>parent</c> / aggregation lookups.
    /// </remarks>
    public static IValidationRule<S201AtonInventory> GmlIdsUnique { get; } =
        ValidationRuleBuilder.RuleFor<S201AtonInventory>("S201-R-1.2")
            .WithDescription("gml:id values must be unique across AtoNs, aggregations, and information types.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((inventory, _) =>
            {
                var findings = new List<ValidationFinding>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var dupes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                void Track(string? id)
                {
                    if (string.IsNullOrEmpty(id)) return;
                    if (!seen.Add(id)) dupes.Add(id);
                }

                foreach (var a in inventory.AtoNs) Track(a.Id);
                foreach (var a in inventory.Aggregations) Track(a.Id);
                foreach (var a in inventory.Associations) Track(a.Id);
                foreach (var s in inventory.StatusInformation) Track(s.Id);
                foreach (var p in inventory.PositioningInformation) Track(p.Id);
                foreach (var f in inventory.FixingMethods) Track(f.Id);
                foreach (var q in inventory.SpatialQualities) Track(q.Id);

                foreach (var dup in dupes)
                {
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S201-R-1.2",
                        Severity = ValidationSeverity.Error,
                        Message = $"Duplicate gml:id '{dup}'.",
                        RelatedFeatureId = dup,
                        DatasetId = inventory.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    // ── S201-R-1.3 — navigable AtoN has geometry ─────────────────

    /// <summary>
    /// <c>S201-R-1.3</c> — Every navigable AtoN (concrete
    /// <c>StructureObject</c>, <c>Equipment</c>, or non-virtual
    /// <c>ElectronicAton</c>) must carry at least one coordinate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spec reference: S-201 Edition 2.0.0 Annex C — the FC requires a
    /// point geometry for concrete physical AtoNs. Aggregation
    /// containers (handled separately as
    /// <see cref="S201AtonAggregation"/> / <see cref="S201AtonAssociation"/>)
    /// and non-physical feature kinds (lines, areas, dataset metadata,
    /// represented in the typed model as <see cref="S201GenericAtonObject"/>)
    /// are exempt.
    /// </para>
    /// <para>
    /// Virtual AIS AtoNs (<see cref="AisAtonKind.Virtual"/>) describe a
    /// notional aid broadcast over AIS and may legitimately appear
    /// without a transmitter position when the encoder elides it; the
    /// rule fires at <see cref="ValidationSeverity.Warning"/> on
    /// non-virtual AtoNs only.
    /// </para>
    /// </remarks>
    public static IValidationRule<S201AtonInventory> NavigableAtoNHasGeometry { get; } =
        ValidationRuleBuilder.RuleFor<S201AtonInventory>("S201-R-1.3")
            .WithDescription("Concrete physical AtoNs must carry at least one coordinate.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((inventory, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var aton in inventory.AtoNs)
                {
                    if (!RequiresGeometry(aton)) continue;
                    if (!aton.Coordinates.IsDefaultOrEmpty && aton.Coordinates.Length > 0) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S201-R-1.3",
                        Severity = ValidationSeverity.Warning,
                        Message = $"{aton.FeatureClass} '{aton.Id}' has no geometry.",
                        RelatedFeatureId = aton.Id,
                        DatasetId = inventory.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    private static bool RequiresGeometry(S201AtonObject aton) => aton switch
    {
        S201ElectronicAtoN { Kind: AisAtonKind.Virtual } => false,
        S201StructureObject => true,
        S201Equipment => true,
        S201ElectronicAtoN => true,
        _ => false,
    };

    // ── S201-R-2.1 — physical / synthetic AIS MMSI ───────────────

    /// <summary>
    /// <c>S201-R-2.1</c> — Every Physical or Synthetic AIS aid to
    /// navigation must carry an <c>mMSICode</c> of exactly nine
    /// decimal digits.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-201 Edition 2.0.0 Annex C —
    /// §<c>PhysicalAISAidToNavigation</c> and
    /// §<c>SyntheticAISAidToNavigation</c> declare <c>mMSICode</c>
    /// (Maritime Mobile Service Identity) as a required attribute. The
    /// MMSI is the nine-digit identifier broadcast in the AIS message
    /// (ITU-R M.585).
    /// </remarks>
    public static IValidationRule<S201AtonInventory> PhysicalAisHasMmsi { get; } =
        ValidationRuleBuilder.RuleFor<S201AtonInventory>("S201-R-2.1")
            .WithDescription("Physical / Synthetic AIS aids to navigation must carry a 9-digit mMSICode.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((inventory, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var ais in inventory.ElectronicAtoNs)
                {
                    if (!MmsiRequiredAisKinds.Contains(ais.Kind)) continue;
                    AddMmsiFinding(findings, inventory, ais, "S201-R-2.1", ValidationSeverity.Error, requirePresent: true);
                }
                return findings;
            })
            .Build();

    // ── S201-R-2.2 — virtual AIS MMSI format ─────────────────────

    /// <summary>
    /// <c>S201-R-2.2</c> — When a Virtual AIS aid to navigation
    /// carries an <c>mMSICode</c>, the value must be exactly nine
    /// decimal digits.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-201 Edition 2.0.0 Annex C —
    /// §<c>VirtualAISAidToNavigation</c>. The FC permits a virtual AIS
    /// AtoN to be authored without an MMSI (a virtual mark may be
    /// dropped without provisioning a dedicated identity), but when one
    /// is supplied it must still conform to the ITU MMSI nine-digit
    /// shape. The rule fires at <see cref="ValidationSeverity.Warning"/>
    /// rather than <see cref="ValidationSeverity.Error"/> because the
    /// missing-value case is permitted.
    /// </remarks>
    public static IValidationRule<S201AtonInventory> VirtualAisMmsiFormat { get; } =
        ValidationRuleBuilder.RuleFor<S201AtonInventory>("S201-R-2.2")
            .WithDescription("Virtual AIS aids to navigation, when carrying an mMSICode, must use the 9-digit ITU shape.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((inventory, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var ais in inventory.ElectronicAtoNs)
                {
                    if (ais.Kind != AisAtonKind.Virtual) continue;
                    AddMmsiFinding(findings, inventory, ais, "S201-R-2.2", ValidationSeverity.Warning, requirePresent: false);
                }
                return findings;
            })
            .Build();

    private static void AddMmsiFinding(
        List<ValidationFinding> findings,
        S201AtonInventory inventory,
        S201ElectronicAtoN ais,
        string ruleId,
        ValidationSeverity severity,
        bool requirePresent)
    {
        var mmsi = ais.MmsiCode;
        var position = ais.Coordinates.IsDefaultOrEmpty ? (GeoPosition?)null : ais.Coordinates[0];

        if (string.IsNullOrEmpty(mmsi))
        {
            if (!requirePresent) return;
            findings.Add(new ValidationFinding
            {
                RuleId = ruleId,
                Severity = severity,
                Message = $"AIS AtoN '{ais.Id}' ({ais.FeatureClass}) is missing the required mMSICode attribute.",
                Point = position,
                RelatedFeatureId = ais.Id,
                DatasetId = inventory.DatasetIdentifier,
            });
            return;
        }

        if (mmsi.Length != 9 || !mmsi.All(char.IsAsciiDigit))
        {
            findings.Add(new ValidationFinding
            {
                RuleId = ruleId,
                Severity = severity,
                Message =
                    $"AIS AtoN '{ais.Id}' ({ais.FeatureClass}) has malformed mMSICode '{mmsi}'; " +
                    "must be exactly 9 decimal digits.",
                Point = position,
                RelatedFeatureId = ais.Id,
                DatasetId = inventory.DatasetIdentifier,
            });
        }
    }

    // ── S201-R-3.1 — ChangeTypes codelist ────────────────────────

    /// <summary>
    /// <c>S201-R-3.1</c> — When an <c>AtonStatusInformation</c>
    /// carries a <c>ChangeTypes</c> code it must fall within the closed
    /// enumeration declared by the Feature Catalogue.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-201 Edition 2.0.0 Annex C2 — <c>ChangeTypes</c>
    /// codelist (1 Advance Notice of Change, 2 Discrepancy,
    /// 3 Proposed Change, 4 Permanent Change). Note S-201 has only
    /// four codelist values where S-125 has five — the spec does not
    /// include "Temporary Change" as a distinct code under S-201.
    /// </remarks>
    public static IValidationRule<S201AtonInventory> ChangeTypesInEnumeration { get; } =
        ValidationRuleBuilder.RuleFor<S201AtonInventory>("S201-R-3.1")
            .WithDescription("AtonStatusInformation ChangeTypes must be one of the FC listed values 1–4.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((inventory, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var info in inventory.StatusInformation)
                {
                    if (info.ChangeTypes is not { } code) continue;
                    if (ValidChangeTypes.Contains(code)) continue;

                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S201-R-3.1",
                        Severity = ValidationSeverity.Error,
                        Message =
                            $"AtonStatusInformation '{info.Id}' has ChangeTypes code {code}; " +
                            "expected one of 1 (Advance Notice), 2 (Discrepancy), 3 (Proposed), 4 (Permanent).",
                        RelatedFeatureId = info.Id,
                        DatasetId = inventory.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    // ── S201-R-4.1 — date range ordering ─────────────────────────

    /// <summary>
    /// <c>S201-R-4.1</c> — When an AtoN's <c>fixedDateRange</c> or
    /// <c>periodicDateRange</c> carries both a start and an end, the
    /// start must not be after the end.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-201 Edition 2.0.0 Annex C — §<c>fixedDateRange</c>
    /// and §<c>periodicDateRange</c>. The complex attribute is composed
    /// of <c>dateStart</c> / <c>dateEnd</c>; the spec is silent on
    /// degenerate ranges but a deployment whose validity begins after
    /// it ends is non-actionable.
    /// </remarks>
    public static IValidationRule<S201AtonInventory> DateRangeOrdered { get; } =
        ValidationRuleBuilder.RuleFor<S201AtonInventory>("S201-R-4.1")
            .WithDescription("AtoN date ranges must have Start ≤ End when both bounds are present.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((inventory, _) =>
            {
                var findings = new List<ValidationFinding>();

                void CheckRange(string ownerId, string ownerClass, string label, S201DateRange? range)
                {
                    if (range is null) return;
                    if (range.Start is not { } s || range.End is not { } e) return;
                    if (s <= e) return;
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S201-R-4.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"{ownerClass} '{ownerId}' {label} has Start ({s:O}) after End ({e:O}).",
                        RelatedFeatureId = ownerId,
                        DatasetId = inventory.DatasetIdentifier,
                    });
                }

                foreach (var aton in inventory.AtoNs)
                {
                    CheckRange(aton.Id, aton.FeatureClass, "fixedDateRange", aton.FixedDateRange);
                    CheckRange(aton.Id, aton.FeatureClass, "periodicDateRange", aton.PeriodicDateRange);
                }
                return findings;
            })
            .Build();

    // ── S201-R-5.1 — equipment has host structure ────────────────

    /// <summary>
    /// <c>S201-R-5.1</c> — Every <c>Equipment</c> instance (including
    /// concrete <c>GenericLight</c> subclasses) should resolve to a
    /// host <c>StructureObject</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spec reference: S-201 Edition 2.0.0 Annex C —
    /// <c>StructureEquipment</c> association. The subordination chain
    /// is back-resolved by the typed projection via
    /// <c>theParentFeature</c> / <c>parent</c> xlinks; an equipment
    /// instance whose <see cref="S201Equipment.HostStructure"/> is
    /// <c>null</c> is either free-standing (rare but spec-permissible
    /// in the operational fleet) or carries an unresolved
    /// <c>xlink:href</c>.
    /// </para>
    /// <para>
    /// The rule fires at <see cref="ValidationSeverity.Warning"/>
    /// rather than <see cref="ValidationSeverity.Error"/> because the
    /// FC does not strictly require the association — the warning
    /// flags the case for downstream authority review.
    /// </para>
    /// </remarks>
    public static IValidationRule<S201AtonInventory> EquipmentHasHostStructure { get; } =
        ValidationRuleBuilder.RuleFor<S201AtonInventory>("S201-R-5.1")
            .WithDescription("Equipment instances should resolve to a host structure via StructureEquipment.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((inventory, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var eq in inventory.Equipment)
                {
                    if (eq.HostStructure is not null) continue;
                    var position = eq.Coordinates.IsDefaultOrEmpty ? (GeoPosition?)null : eq.Coordinates[0];
                    findings.Add(new ValidationFinding
                    {
                        RuleId = "S201-R-5.1",
                        Severity = ValidationSeverity.Warning,
                        Message =
                            $"{eq.FeatureClass} '{eq.Id}' has no resolved host structure " +
                            "(missing or unresolved StructureEquipment xlink).",
                        Point = position,
                        RelatedFeatureId = eq.Id,
                        DatasetId = inventory.DatasetIdentifier,
                    });
                }
                return findings;
            })
            .Build();

    // ── S201-R-6.1 — aggregation has members ─────────────────────

    /// <summary>
    /// <c>S201-R-6.1</c> — Every <c>AtonAggregation</c> /
    /// <c>AtonAssociation</c> must reference at least two AtoNs.
    /// </summary>
    /// <remarks>
    /// Spec reference: S-201 Edition 2.0.0 Annex C — §<c>AtonAggregation</c>
    /// and §<c>AtonAssociation</c>. These features are geometry-less
    /// containers whose entire purpose is to group two or more AtoNs
    /// by xlink (e.g. a leading line of two beacons). An aggregation
    /// with fewer than two resolved peers has no semantic content.
    /// </remarks>
    public static IValidationRule<S201AtonInventory> AggregationHasMembers { get; } =
        ValidationRuleBuilder.RuleFor<S201AtonInventory>("S201-R-6.1")
            .WithDescription("AtonAggregation / AtonAssociation must reference at least two AtoNs.")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((inventory, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var agg in inventory.Aggregations)
                    CheckMemberCount(findings, inventory, agg.Id, "AtonAggregation", agg.Peers);
                foreach (var asoc in inventory.Associations)
                    CheckMemberCount(findings, inventory, asoc.Id, "AtonAssociation", asoc.Peers);
                return findings;
            })
            .Build();

    private static void CheckMemberCount(
        List<ValidationFinding> findings,
        S201AtonInventory inventory,
        string id,
        string kind,
        ImmutableArray<S201AtonObject> peers)
    {
        var count = peers.IsDefaultOrEmpty ? 0 : peers.Length;
        if (count >= 2) return;
        findings.Add(new ValidationFinding
        {
            RuleId = "S201-R-6.1",
            Severity = ValidationSeverity.Warning,
            Message = $"{kind} '{id}' has {count} resolved peer(s); expected at least 2.",
            RelatedFeatureId = id,
            DatasetId = inventory.DatasetIdentifier,
        });
    }

    // ── S201-R-6.2 — aggregation members resolved ────────────────

    /// <summary>
    /// <c>S201-R-6.2</c> — Every peer <c>xlink:href</c> on an
    /// <c>AtonAggregation</c> or <c>AtonAssociation</c> must resolve
    /// to an AtoN in the inventory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spec reference: S-100 Part 10b §6.4 — <c>xlink:href</c>
    /// references at the dataset level must resolve. The typed
    /// projection captures the resolved peers in
    /// <see cref="S201AtonAggregation.Peers"/>; comparing the resolved
    /// count to the number of <c>peer</c>-role references on the
    /// source feature surfaces dropped references that would otherwise
    /// hide behind the typed model's already-resolved view.
    /// </para>
    /// <para>
    /// The rule fires at <see cref="ValidationSeverity.Error"/> — an
    /// unresolved aggregation member is a structural defect in the
    /// AtoN inventory.
    /// </para>
    /// </remarks>
    public static IValidationRule<S201AtonInventory> AggregationMembersResolved { get; } =
        ValidationRuleBuilder.RuleFor<S201AtonInventory>("S201-R-6.2")
            .WithDescription("AtonAggregation / AtonAssociation peer xlinks must all resolve.")
            .WithSeverity(ValidationSeverity.Error)
            .Yield((inventory, _) =>
            {
                var findings = new List<ValidationFinding>();
                foreach (var agg in inventory.Aggregations)
                    CheckXlinkResolution(findings, inventory, agg.Id, "AtonAggregation", agg.Peers);
                foreach (var asoc in inventory.Associations)
                    CheckXlinkResolution(findings, inventory, asoc.Id, "AtonAssociation", asoc.Peers);
                return findings;
            })
            .Build();

    private static void CheckXlinkResolution(
        List<ValidationFinding> findings,
        S201AtonInventory inventory,
        string id,
        string kind,
        ImmutableArray<S201AtonObject> peers)
    {
        var source = FindSourceFeature(inventory, id);
        if (source is null) return;

        var expected = 0;
        foreach (var r in source.FeatureReferences)
        {
            if (string.Equals(r.Role, "peer", StringComparison.OrdinalIgnoreCase))
                expected++;
        }

        var actual = peers.IsDefaultOrEmpty ? 0 : peers.Length;
        if (actual >= expected) return;

        findings.Add(new ValidationFinding
        {
            RuleId = "S201-R-6.2",
            Severity = ValidationSeverity.Error,
            Message =
                $"{kind} '{id}' has {expected - actual} unresolved peer xlink(s) " +
                $"({actual} of {expected} resolved).",
            RelatedFeatureId = id,
            DatasetId = inventory.DatasetIdentifier,
        });
    }

    private static S201Feature? FindSourceFeature(S201AtonInventory inventory, string id)
    {
        if (inventory.Source.Features.IsDefaultOrEmpty) return null;
        foreach (var f in inventory.Source.Features)
        {
            if (string.Equals(f.Id, id, StringComparison.Ordinal))
                return f;
        }
        return null;
    }

    /// <summary>The canonical default rule set for S-201 AtoN inventories.</summary>
    public static ValidationRuleSet<S201AtonInventory> Default { get; } = new(
        CoordinatesInWgs84Range,
        GmlIdsUnique,
        NavigableAtoNHasGeometry,
        PhysicalAisHasMmsi,
        VirtualAisMmsiFormat,
        ChangeTypesInEnumeration,
        DateRangeOrdered,
        EquipmentHasHostStructure,
        AggregationHasMembers,
        AggregationMembersResolved);

    /// <summary>
    /// Convenience wrapper around <see cref="ValidationRuleSet{T}.Run(T, ValidationContext?)"/>
    /// using the <see cref="Default"/> rule set.
    /// </summary>
    /// <param name="inventory">The S-201 AtoN inventory to validate.</param>
    /// <param name="context">Optional validation context; defaults to <see cref="ValidationContext.Default"/>.</param>
    /// <returns>The validation report for <paramref name="inventory"/>.</returns>
    public static ValidationReport Validate(S201AtonInventory inventory, ValidationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return Default.Run(inventory, context);
    }
}
