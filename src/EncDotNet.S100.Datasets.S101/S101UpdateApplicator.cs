namespace EncDotNet.S100.Datasets.S101;

/// <summary>Severity of a message produced while applying S-101 sequential updates.</summary>
public enum S101UpdateSeverity
{
    /// <summary>Informational note (e.g. an update was applied cleanly).</summary>
    Info = 0,

    /// <summary>A recoverable problem; application continued best-effort.</summary>
    Warning = 1,

    /// <summary>A problem that prevented an update (or part of it) from being applied.</summary>
    Error = 2,
}

/// <summary>A single diagnostic emitted during update application.</summary>
/// <param name="Severity">Message severity.</param>
/// <param name="Text">Human-readable description.</param>
/// <param name="UpdateNumber">Update number the message relates to, when known.</param>
public readonly record struct S101UpdateMessage(
    S101UpdateSeverity Severity,
    string Text,
    int? UpdateNumber = null);

/// <summary>
/// Structured outcome of applying one or more S-101 sequential updates onto a base
/// cell. Application is <b>best-effort</b>: a failed or invalid update is recorded
/// here and never prevents the (partially) updated document from being used.
/// </summary>
public sealed class S101UpdateReport
{
    /// <summary>Update number of the base cell the chain started from (0 for a base cell).</summary>
    public int BaseUpdateNumber { get; init; }

    /// <summary>The highest update number successfully applied (equals <see cref="BaseUpdateNumber"/> if none applied).</summary>
    public int AppliedThroughUpdateNumber { get; init; }

    /// <summary>Number of records inserted across all applied updates.</summary>
    public int Inserted { get; init; }

    /// <summary>Number of records deleted across all applied updates.</summary>
    public int Deleted { get; init; }

    /// <summary>Number of records modified across all applied updates.</summary>
    public int Modified { get; init; }

    /// <summary>Diagnostics gathered during application.</summary>
    public IReadOnlyList<S101UpdateMessage> Messages { get; init; } = [];

    /// <summary><see langword="true"/> when no <see cref="S101UpdateSeverity.Error"/> or warning was recorded.</summary>
    public bool Success => !Messages.Any(m => m.Severity >= S101UpdateSeverity.Warning);
}

/// <summary>
/// Applies S-101 sequential updates (application profile <c>2</c>) onto a base
/// cell (profile <c>1</c>) to produce an "up-to-date" document, mirroring the
/// pure document-to-document merge of <c>EncDotNet.S57.S57Document.ApplyChanges</c>.
/// </summary>
/// <remarks>
/// Records are keyed by record id (RCID) within each record class. Record-level
/// <c>RUIN</c> selects insert / delete / modify; for feature and information
/// records, <c>Modify</c> merges attributes and associations using their inlined
/// per-element instructions (<c>ATIN</c> / <c>SAUI</c> / <c>FAUI</c> / <c>IUIN</c>).
/// Spatial records (point / multi-point / curve / composite-curve / surface) carry
/// no inline element instructions, so <c>Modify</c> replaces the record wholesale.
/// </remarks>
public static class S101UpdateApplicator
{
    /// <summary>
    /// Applies an ordered list of update documents onto <paramref name="baseDocument"/>,
    /// best-effort, and reports the outcome.
    /// </summary>
    /// <param name="baseDocument">The base cell (application profile <c>1</c>).</param>
    /// <param name="orderedUpdates">
    /// Update documents in ascending update-number order. The list may be empty
    /// (returns the base unchanged).
    /// </param>
    /// <param name="report">Receives a structured, best-effort outcome report.</param>
    /// <returns>
    /// The merged document. On any failure, the most up-to-date usable document
    /// produced so far (at minimum the base) is returned and the failure is noted
    /// in <paramref name="report"/>.
    /// </returns>
    public static S101Document Apply(
        S101Document baseDocument,
        IReadOnlyList<S101Document> orderedUpdates,
        out S101UpdateReport report)
    {
        ArgumentNullException.ThrowIfNull(baseDocument);
        ArgumentNullException.ThrowIfNull(orderedUpdates);

        var messages = new List<S101UpdateMessage>();
        var counts = new ApplyCounts();

        var baseUpdateNumber = baseDocument.Identification.UpdateNumber;
        var current = baseDocument;
        var appliedThrough = baseUpdateNumber;
        var expectedNext = baseUpdateNumber + 1;

        foreach (var update in orderedUpdates)
        {
            var updateNumber = update.Identification.UpdateNumber;

            if (!update.Identification.IsUpdate)
            {
                messages.Add(new S101UpdateMessage(
                    S101UpdateSeverity.Warning,
                    $"Skipped '{update.Identification.DatasetName}': not an update dataset (application profile is '{update.Identification.ApplicationProfile}').",
                    updateNumber));
                continue;
            }

            if (updateNumber != expectedNext)
            {
                // Gap or out-of-order break the chain; stop best-effort here.
                messages.Add(new S101UpdateMessage(
                    S101UpdateSeverity.Warning,
                    $"Stopped at update {updateNumber}: expected update {expectedNext} (non-contiguous sequence). Earlier updates were applied.",
                    updateNumber));
                break;
            }

            try
            {
                current = ApplySingle(current, update, messages, counts);
                appliedThrough = updateNumber;
                expectedNext = updateNumber + 1;
                messages.Add(new S101UpdateMessage(
                    S101UpdateSeverity.Info, $"Applied update {updateNumber}.", updateNumber));
            }
            catch (Exception ex)
            {
                messages.Add(new S101UpdateMessage(
                    S101UpdateSeverity.Error,
                    $"Failed to apply update {updateNumber}: {ex.Message}. Earlier updates were applied.",
                    updateNumber));
                break;
            }
        }

        report = new S101UpdateReport
        {
            BaseUpdateNumber = baseUpdateNumber,
            AppliedThroughUpdateNumber = appliedThrough,
            Inserted = counts.Inserted,
            Deleted = counts.Deleted,
            Modified = counts.Modified,
            Messages = messages.ToArray(),
        };

        return current;
    }

    internal static S101Document ApplySingle(
        S101Document baseDocument,
        S101Document update,
        ICollection<S101UpdateMessage>? messages)
        => ApplySingle(baseDocument, update, messages, new ApplyCounts());

    private static S101Document ApplySingle(
        S101Document baseDocument,
        S101Document update,
        ICollection<S101UpdateMessage>? messages,
        ApplyCounts counts)
    {
        var points = new Dictionary<uint, S101PointRecord>(baseDocument.Points);
        var multiPoints = new Dictionary<uint, S101MultiPointRecord>(baseDocument.MultiPoints);
        var curves = new Dictionary<uint, S101CurveSegmentRecord>(baseDocument.CurveSegments);
        var compositeCurves = new Dictionary<uint, S101CompositeCurveRecord>(baseDocument.CompositeCurves);
        var surfaces = new Dictionary<uint, S101SurfaceRecord>(baseDocument.Surfaces);
        var informationTypes = new Dictionary<uint, S101InformationRecord>(baseDocument.InformationTypes);

        ApplySpatial(points, update.Points, counts, messages);
        ApplySpatial(multiPoints, update.MultiPoints, counts, messages);
        ApplySpatial(curves, update.CurveSegments, counts, messages);
        ApplySpatial(compositeCurves, update.CompositeCurves, counts, messages);
        ApplySpatial(surfaces, update.Surfaces, counts, messages);

        ApplyInformationRecords(informationTypes, update.InformationTypes, counts, messages);

        var features = ApplyFeatures(baseDocument.Features, update.Features, counts, messages);

        return new S101Document
        {
            Identification = baseDocument.Identification with
            {
                UpdateNumber = update.Identification.UpdateNumber,
                ApplicationProfile = baseDocument.Identification.ApplicationProfile,
            },
            StructureInfo = baseDocument.StructureInfo,
            FeatureTypeCatalogue = baseDocument.FeatureTypeCatalogue,
            AttributeTypeCatalogue = baseDocument.AttributeTypeCatalogue,
            Points = points,
            MultiPoints = multiPoints,
            CurveSegments = curves,
            CompositeCurves = compositeCurves,
            Surfaces = surfaces,
            Features = features,
            InformationTypes = informationTypes,
            InformationTypeCatalogue = baseDocument.InformationTypeCatalogue,
            InformationAssociationCatalogue = baseDocument.InformationAssociationCatalogue,
            FeatureAssociationCatalogue = baseDocument.FeatureAssociationCatalogue,
            RoleCatalogue = baseDocument.RoleCatalogue,
        };
    }

    private static void ApplySpatial<T>(
        Dictionary<uint, T> target,
        IReadOnlyDictionary<uint, T> updates,
        ApplyCounts counts,
        ICollection<S101UpdateMessage>? messages)
        where T : class
    {
        foreach (var (id, record) in updates)
        {
            var instruction = GetInstruction(record);
            switch (instruction)
            {
                case S101UpdateInstruction.Delete:
                    if (target.Remove(id)) counts.Deleted++;
                    break;

                case S101UpdateInstruction.Modify:
                    // Spatial records carry no inline element instructions; replace wholesale.
                    target[id] = record;
                    counts.Modified++;
                    break;

                case S101UpdateInstruction.Insert:
                case S101UpdateInstruction.None:
                default:
                    var existed = target.ContainsKey(id);
                    target[id] = record;
                    if (existed) counts.Modified++; else counts.Inserted++;
                    break;
            }
        }
    }

    private static void ApplyInformationRecords(
        Dictionary<uint, S101InformationRecord> target,
        IReadOnlyDictionary<uint, S101InformationRecord> updates,
        ApplyCounts counts,
        ICollection<S101UpdateMessage>? messages)
    {
        foreach (var (id, record) in updates)
        {
            switch (record.UpdateInstruction)
            {
                case S101UpdateInstruction.Delete:
                    if (target.Remove(id)) counts.Deleted++;
                    break;

                case S101UpdateInstruction.Modify when target.TryGetValue(id, out var existing):
                    target[id] = new S101InformationRecord
                    {
                        RecordId = existing.RecordId,
                        InformationTypeCode = record.InformationTypeCode != 0
                            ? record.InformationTypeCode
                            : existing.InformationTypeCode,
                        Attributes = MergeAttributes(existing.Attributes, record.Attributes),
                        RecordVersion = record.RecordVersion,
                        UpdateInstruction = S101UpdateInstruction.None,
                    };
                    counts.Modified++;
                    break;

                default:
                    var existed = target.ContainsKey(id);
                    target[id] = record;
                    if (existed) counts.Modified++; else counts.Inserted++;
                    break;
            }
        }
    }

    private static IReadOnlyList<S101FeatureRecord> ApplyFeatures(
        IReadOnlyList<S101FeatureRecord> baseFeatures,
        IReadOnlyList<S101FeatureRecord> updateFeatures,
        ApplyCounts counts,
        ICollection<S101UpdateMessage>? messages)
    {
        // Preserve dataset order while allowing keyed insert/delete/modify by RCID.
        var order = new List<uint>(baseFeatures.Count);
        var byId = new Dictionary<uint, S101FeatureRecord>(baseFeatures.Count);
        foreach (var f in baseFeatures)
        {
            if (!byId.ContainsKey(f.RecordId)) order.Add(f.RecordId);
            byId[f.RecordId] = f;
        }

        foreach (var update in updateFeatures)
        {
            switch (update.UpdateInstruction)
            {
                case S101UpdateInstruction.Delete:
                    if (byId.Remove(update.RecordId))
                    {
                        order.Remove(update.RecordId);
                        counts.Deleted++;
                    }
                    break;

                case S101UpdateInstruction.Modify when byId.TryGetValue(update.RecordId, out var existing):
                    byId[update.RecordId] = MergeFeature(existing, update);
                    counts.Modified++;
                    break;

                default:
                    if (byId.ContainsKey(update.RecordId))
                    {
                        byId[update.RecordId] = update;
                        counts.Modified++;
                    }
                    else
                    {
                        order.Add(update.RecordId);
                        byId[update.RecordId] = update;
                        counts.Inserted++;
                    }
                    break;
            }
        }

        var result = new List<S101FeatureRecord>(order.Count);
        foreach (var id in order)
            result.Add(byId[id]);
        return result;
    }

    private static S101FeatureRecord MergeFeature(S101FeatureRecord existing, S101FeatureRecord update) =>
        new()
        {
            RecordId = existing.RecordId,
            FeatureTypeCode = update.FeatureTypeCode != 0 ? update.FeatureTypeCode : existing.FeatureTypeCode,
            ProducingAgency = update.ProducingAgency != 0 ? update.ProducingAgency : existing.ProducingAgency,
            FeatureIdentificationNumber = update.FeatureIdentificationNumber != 0
                ? update.FeatureIdentificationNumber
                : existing.FeatureIdentificationNumber,
            FeatureIdentificationSubdivision = update.FeatureIdentificationSubdivision != 0
                ? update.FeatureIdentificationSubdivision
                : existing.FeatureIdentificationSubdivision,
            Attributes = MergeAttributes(existing.Attributes, update.Attributes),
            SpatialAssociations = MergeSpatialAssociations(existing.SpatialAssociations, update.SpatialAssociations),
            FeatureAssociations = MergeFeatureAssociations(existing.FeatureAssociations, update.FeatureAssociations),
            InformationAssociations = MergeInformationAssociations(existing.InformationAssociations, update.InformationAssociations),
            RecordVersion = update.RecordVersion,
            UpdateInstruction = S101UpdateInstruction.None,
        };

    private static IReadOnlyList<S101Attribute> MergeAttributes(
        IReadOnlyList<S101Attribute> existing,
        IReadOnlyList<S101Attribute> updates)
    {
        if (updates.Count == 0)
            return existing;

        var list = existing.Count == 0 ? new List<S101Attribute>() : new List<S101Attribute>(existing);

        foreach (var u in updates)
        {
            var index = list.FindIndex(a => a.NumericCode == u.NumericCode && a.Index == u.Index && a.ParentIndex == u.ParentIndex);
            switch (u.UpdateInstruction)
            {
                case S101UpdateInstruction.Delete:
                    if (index >= 0) list.RemoveAt(index);
                    break;

                default:
                    var replacement = u with { UpdateInstruction = S101UpdateInstruction.None };
                    if (index >= 0) list[index] = replacement;
                    else list.Add(replacement);
                    break;
            }
        }

        return list.ToArray();
    }

    private static IReadOnlyList<S101SpatialAssociation> MergeSpatialAssociations(
        IReadOnlyList<S101SpatialAssociation> existing,
        IReadOnlyList<S101SpatialAssociation> updates)
    {
        if (updates.Count == 0)
            return existing;

        var list = existing.Count == 0 ? new List<S101SpatialAssociation>() : new List<S101SpatialAssociation>(existing);

        foreach (var u in updates)
        {
            var index = list.FindIndex(a => a.RecordName == u.RecordName && a.RecordId == u.RecordId);
            switch (u.UpdateInstruction)
            {
                case S101UpdateInstruction.Delete:
                    if (index >= 0) list.RemoveAt(index);
                    break;

                default:
                    var replacement = u with { UpdateInstruction = S101UpdateInstruction.None };
                    if (index >= 0) list[index] = replacement;
                    else list.Add(replacement);
                    break;
            }
        }

        return list.ToArray();
    }

    private static IReadOnlyList<S101FeatureAssociation> MergeFeatureAssociations(
        IReadOnlyList<S101FeatureAssociation> existing,
        IReadOnlyList<S101FeatureAssociation> updates)
    {
        if (updates.Count == 0)
            return existing;

        var list = existing.Count == 0 ? new List<S101FeatureAssociation>() : new List<S101FeatureAssociation>(existing);

        foreach (var u in updates)
        {
            var index = list.FindIndex(a => a.NumericCode == u.NumericCode && a.RecordId == u.RecordId);
            switch (u.UpdateInstruction)
            {
                case S101UpdateInstruction.Delete:
                    if (index >= 0) list.RemoveAt(index);
                    break;

                default:
                    var replacement = u with { UpdateInstruction = S101UpdateInstruction.None };
                    if (index >= 0) list[index] = replacement;
                    else list.Add(replacement);
                    break;
            }
        }

        return list.ToArray();
    }

    private static IReadOnlyList<S101InformationAssociation> MergeInformationAssociations(
        IReadOnlyList<S101InformationAssociation> existing,
        IReadOnlyList<S101InformationAssociation> updates)
    {
        if (updates.Count == 0)
            return existing;

        var list = existing.Count == 0 ? new List<S101InformationAssociation>() : new List<S101InformationAssociation>(existing);

        foreach (var u in updates)
        {
            var index = list.FindIndex(a => a.NumericCode == u.NumericCode && a.RecordId == u.RecordId);
            switch (u.UpdateInstruction)
            {
                case S101UpdateInstruction.Delete:
                    if (index >= 0) list.RemoveAt(index);
                    break;

                default:
                    var replacement = u with { UpdateInstruction = S101UpdateInstruction.None };
                    if (index >= 0) list[index] = replacement;
                    else list.Add(replacement);
                    break;
            }
        }

        return list.ToArray();
    }

    private static S101UpdateInstruction GetInstruction<T>(T record) where T : class => record switch
    {
        S101PointRecord p => p.UpdateInstruction,
        S101MultiPointRecord m => m.UpdateInstruction,
        S101CurveSegmentRecord c => c.UpdateInstruction,
        S101CompositeCurveRecord cc => cc.UpdateInstruction,
        S101SurfaceRecord s => s.UpdateInstruction,
        _ => S101UpdateInstruction.None,
    };

    private sealed class ApplyCounts
    {
        public int Inserted;
        public int Deleted;
        public int Modified;
    }
}
