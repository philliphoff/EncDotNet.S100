using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S125.DataModel;

/// <summary>
/// Classification of a change reported against an aid to navigation,
/// per S-125 Edition 1.0.0 §changeTypes (Feature Catalogue listed values).
/// </summary>
public enum S125ChangeType
{
    /// <summary>The change type was absent or unrecognised in the source data.</summary>
    Unknown = 0,

    /// <summary>Code <c>1</c> — Advance Notice of Change.</summary>
    AdvanceNoticeOfChange = 1,

    /// <summary>Code <c>2</c> — Discrepancy.</summary>
    Discrepancy = 2,

    /// <summary>Code <c>3</c> — Proposed Change.</summary>
    ProposedChange = 3,

    /// <summary>Code <c>4</c> — Temporary Change.</summary>
    TemporaryChange = 4,

    /// <summary>Code <c>5</c> — Permanent Change.</summary>
    PermanentChange = 5,
}

/// <summary>
/// A date range used by S-125 AtoN status reporting
/// (S-125 Edition 1.0.0 §fixedDateRange / §periodicDateRange).
/// </summary>
public sealed record S125DateRange
{
    /// <summary>Start of the validity period (UTC, ISO 8601 round-trip).</summary>
    public DateTimeOffset? Start { get; init; }

    /// <summary>End of the validity period (UTC, ISO 8601 round-trip).</summary>
    public DateTimeOffset? End { get; init; }
}

/// <summary>
/// Typed projection of an <c>AtonStatusInformation</c> information type
/// (S-125 Edition 1.0.0 §AtonStatusInformation): the payload bound to an
/// aid to navigation via the <c>AtonStatus</c> information association
/// (role <c>AtoNStatus</c> as written in datasets).
/// </summary>
public sealed class S125AtonStatusInformation
{
    /// <summary>The GML identifier of the source <c>AtonStatusInformation</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The raw numeric <c>changeTypes</c> code, when present.</summary>
    public int? ChangeTypeCode { get; init; }

    /// <summary>Strongly-typed change classification.</summary>
    public S125ChangeType ChangeType { get; init; }

    /// <summary>Free-text <c>changeDetails</c> description, when present.</summary>
    public string? ChangeDetails { get; init; }

    /// <summary>The fixed validity range, when present (§fixedDateRange).</summary>
    public S125DateRange? FixedDateRange { get; init; }

    /// <summary>Periodic validity ranges, when present (§periodicDateRange).</summary>
    public ImmutableArray<S125DateRange> PeriodicDateRanges { get; init; } =
        ImmutableArray<S125DateRange>.Empty;

    /// <summary>
    /// Convenience derived flag indicating whether the bound aid is judged
    /// operational by the status payload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns <c>null</c> when no <see cref="ChangeType"/> was recorded;
    /// returns <c>false</c> for <see cref="S125ChangeType.Discrepancy"/> and
    /// <see cref="S125ChangeType.TemporaryChange"/> (the change codes that
    /// typically denote an outage or temporary alteration); returns
    /// <c>true</c> for all other recorded codes. This is a heuristic
    /// interpretation of the FC enumeration — callers needing precise
    /// classification should inspect <see cref="ChangeType"/> directly.
    /// </para>
    /// </remarks>
    public bool? IsOperational => ChangeType switch
    {
        S125ChangeType.Unknown => null,
        S125ChangeType.Discrepancy => false,
        S125ChangeType.TemporaryChange => false,
        _ => true,
    };

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of the <c>AtonStatusIndication</c> feature
/// (S-125 Edition 1.0.0 §AtonStatusIndication): a positioned, portrayable
/// feature that visualises status changes against one or more
/// aids to navigation.
/// </summary>
/// <remarks>
/// Distinct from <see cref="S125AtonStatusInformation"/>, which is the
/// information type carrying the change payload. An
/// <c>AtonStatusIndication</c> feature binds to the AtoN(s) via the
/// <c>AtonStatusIndicationAssociation</c> feature association and
/// optionally references an <c>AtonStatusInformation</c> imember via the
/// <c>AtonStatus</c> information association.
/// </remarks>
public sealed class S125AtonStatusIndication
{
    /// <summary>The GML identifier of the source feature.</summary>
    public required string Id { get; init; }

    /// <summary>The feature's position, when present.</summary>
    public GeoPosition? Position { get; init; }

    /// <summary>The expected outage period, when present (§expectedOutage).</summary>
    public S125DateRange? ExpectedOutage { get; init; }

    /// <summary>The resolved status payload, when the feature carries an <c>AtoNStatus</c> binding.</summary>
    public S125AtonStatusInformation? Status { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
