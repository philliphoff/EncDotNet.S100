using System.Globalization;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>Severity of a message produced while applying S-57 updates.</summary>
public enum S57UpdateSeverity
{
    /// <summary>Informational note, such as an update applied cleanly.</summary>
    Info = 0,

    /// <summary>A problem that stopped the updates; the ones before it were applied.</summary>
    Warning = 1,

    /// <summary>An update was refused or failed; the ones before it were applied.</summary>
    Error = 2,
}

/// <summary>A message produced while applying S-57 updates.</summary>
/// <param name="Severity">How serious the message is.</param>
/// <param name="Text">What happened, for the user.</param>
/// <param name="UpdateNumber">The update the message is about, when there is one.</param>
public readonly record struct S57UpdateMessage(
    S57UpdateSeverity Severity,
    string Text,
    int? UpdateNumber = null);

/// <summary>
/// What happened when S-57 updates were applied to a cell. Application is
/// best-effort: the updates up to the first problem are applied, and the
/// problem is recorded here rather than stopping the cell from loading.
/// </summary>
public sealed class S57UpdateReport
{
    /// <summary>The cell's edition (DSID <c>EDTN</c>).</summary>
    public int Edition { get; init; }

    /// <summary>The update number the cell started at: 0 for a new edition, higher for a re-issue (DSID <c>UPDN</c>).</summary>
    public int BaseUpdateNumber { get; init; }

    /// <summary>The last update applied, or <see cref="BaseUpdateNumber"/> when none was.</summary>
    public int AppliedThroughUpdateNumber { get; init; }

    /// <summary>
    /// The update that cancelled the cell (one with edition 0), or
    /// <see langword="null"/> when it wasn't cancelled. A cancelled cell
    /// should no longer be used for navigation.
    /// </summary>
    public int? CancelledByUpdateNumber { get; init; }

    /// <summary><see langword="true"/> when an update cancelled the cell.</summary>
    public bool IsCancelled => CancelledByUpdateNumber is not null;

    /// <summary>What happened to each update.</summary>
    public IReadOnlyList<S57UpdateMessage> Messages { get; init; } = [];

    /// <summary><see langword="true"/> when every update was applied and none cancelled the cell.</summary>
    public bool Success => !IsCancelled && !Messages.Any(m => m.Severity >= S57UpdateSeverity.Warning);
}

/// <summary>
/// Applies S-57 sequential updates to a base cell, checking each update's
/// DSID against the cell's first (S-57 Part 3, sequential updating; tested by
/// S-64 §2.2).
/// </summary>
/// <remarks>
/// <para>
/// An update applies when it's for the cell's edition and its update number
/// follows the last one applied. Updates the cell already holds, such as
/// those before a re-issue, are skipped. The first update that is for another
/// edition, leaves a gap, or fails to apply stops the sequence; the updates
/// before it stay applied.
/// </para>
/// <para>
/// Each record an update changes must also follow on: a record it modifies or
/// deletes must be at the version just below the update's (<c>RVER</c>), and
/// a record it inserts must not exist yet. An update that changes a record
/// twice gives each change the next version. That catches an update whose DSID
/// is right but whose content belongs elsewhere in the sequence.
/// </para>
/// <para>
/// An update with edition 0 cancels the cell. Nothing after it applies, and
/// <see cref="S57UpdateReport.IsCancelled"/> is set; the cell is still
/// returned so the caller decides how to show it.
/// </para>
/// </remarks>
public static class S57UpdateApplicator
{
    /// <summary>
    /// Applies <paramref name="orderedUpdates"/> to <paramref name="baseDocument"/>
    /// and reports what was applied.
    /// </summary>
    /// <param name="baseDocument">The base cell (<c>.000</c>).</param>
    /// <param name="orderedUpdates">The update datasets, in ascending update-number order.</param>
    /// <param name="report">What was applied, and the first problem if there was one.</param>
    /// <returns>The cell with every update up to the first problem applied.</returns>
    public static S57Document Apply(
        S57Document baseDocument,
        IReadOnlyList<S57Document> orderedUpdates,
        out S57UpdateReport report)
    {
        ArgumentNullException.ThrowIfNull(baseDocument);
        ArgumentNullException.ThrowIfNull(orderedUpdates);

        var messages = new List<S57UpdateMessage>();
        var edition = Number(baseDocument.DataSetIdentification?.EditionNumber) ?? 0;
        var baseUpdateNumber = Number(baseDocument.DataSetIdentification?.UpdateNumber) ?? 0;
        var current = baseDocument;
        var appliedThrough = baseUpdateNumber;
        int? cancelledBy = null;

        foreach (var update in orderedUpdates)
        {
            ArgumentNullException.ThrowIfNull(update);
            var updateNumber = Number(update.DataSetIdentification?.UpdateNumber);
            var updateEdition = Number(update.DataSetIdentification?.EditionNumber);

            if (updateNumber is not { } number || updateEdition is not { } forEdition)
            {
                messages.Add(new S57UpdateMessage(
                    S57UpdateSeverity.Error,
                    $"Stopped at '{update.DataSetIdentification?.DataSetName}': it has no edition or update number."));
                break;
            }

            if (number <= appliedThrough && forEdition == edition)
            {
                messages.Add(new S57UpdateMessage(
                    S57UpdateSeverity.Info, $"Skipped update {number}: the cell already includes it.", number));
                continue;
            }

            if (number != appliedThrough + 1)
            {
                messages.Add(new S57UpdateMessage(
                    S57UpdateSeverity.Warning,
                    $"Stopped at update {number}: update {appliedThrough + 1} is missing. The updates before it were applied.",
                    number));
                break;
            }

            if (forEdition == 0)
            {
                cancelledBy = number;
                appliedThrough = number;
                messages.Add(new S57UpdateMessage(
                    S57UpdateSeverity.Warning,
                    $"Update {number} cancels the cell. It's no longer for use in navigation.",
                    number));
                break;
            }

            if (forEdition != edition)
            {
                messages.Add(new S57UpdateMessage(
                    S57UpdateSeverity.Error,
                    forEdition < edition
                        ? $"Refused update {number}: it's for edition {forEdition}, but the cell is edition {edition}. Updates for an older edition don't apply."
                        : $"Refused update {number}: it's for edition {forEdition}, but the cell is edition {edition}. Load edition {forEdition} of the cell first.",
                    number));
                break;
            }

            if (FirstOutOfSequenceRecord(current, update) is { } record)
            {
                messages.Add(new S57UpdateMessage(
                    S57UpdateSeverity.Error,
                    $"Refused update {number}: it doesn't follow on from the updates applied ({record}). The updates before it were applied.",
                    number));
                break;
            }

            try
            {
                current = current.ApplyChanges(update);
                appliedThrough = number;
                messages.Add(new S57UpdateMessage(S57UpdateSeverity.Info, $"Applied update {number}.", number));
            }
            catch (Exception ex)
            {
                messages.Add(new S57UpdateMessage(
                    S57UpdateSeverity.Error,
                    $"Failed to apply update {number}: {ex.Message} The updates before it were applied.",
                    number));
                break;
            }
        }

        report = new S57UpdateReport
        {
            Edition = edition,
            BaseUpdateNumber = baseUpdateNumber,
            AppliedThroughUpdateNumber = appliedThrough,
            CancelledByUpdateNumber = cancelledBy,
            Messages = messages.ToArray(),
        };

        return current;
    }

    /// <summary>
    /// Describes the first record in <paramref name="update"/> that doesn't
    /// follow on from <paramref name="document"/>, or returns
    /// <see langword="null"/> when they all do.
    /// </summary>
    private static string? FirstOutOfSequenceRecord(S57Document document, S57Document update)
    {
        var versions = new Dictionary<(int Code, long Id), int>();
        foreach (var record in document.FeatureRecords)
            versions[Key(record.RecordName)] = record.RecordVersion;
        foreach (var record in document.VectorRecords)
            versions[Key(record.RecordName)] = record.RecordVersion;

        foreach (var (name, version, instruction) in update.FeatureRecords
            .Select(r => (r.RecordName, r.RecordVersion, r.UpdateInstruction))
            .Concat(update.VectorRecords.Select(r => (r.RecordName, r.RecordVersion, r.UpdateInstruction))))
        {
            var key = Key(name);
            var exists = versions.TryGetValue(key, out var current);
            var problem = instruction switch
            {
                S57UpdateInstruction.Insert when exists => $"it inserts record {key.Id}, which is already there",
                S57UpdateInstruction.Delete or S57UpdateInstruction.Modify when !exists => $"it changes record {key.Id}, which isn't there",
                S57UpdateInstruction.Delete or S57UpdateInstruction.Modify when version != current + 1 =>
                    $"record {key.Id} is at version {current}, and the update makes it version {version}",
                _ => null,
            };
            if (problem is not null)
                return problem;

            // An update may change the same record more than once, each time
            // one version on.
            if (instruction == S57UpdateInstruction.Delete)
                versions.Remove(key);
            else
                versions[key] = version;
        }

        return null;
    }

    private static (int Code, long Id) Key(S57RecordName name) => ((int)name.RecordNameCode, name.RecordId);

    /// <summary>A DSID number subfield (<c>EDTN</c>, <c>UPDN</c>), which S-57 holds as text.</summary>
    private static int? Number(string? value)
        => int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
}
