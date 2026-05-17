using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S131.DataModel;

/// <summary>
/// Common contract exposed by every typed S-131 information type
/// (<see cref="S131Authority"/>, <see cref="S131ContactDetails"/>,
/// <see cref="S131RxNInformation"/>, etc.). Mirrors
/// <see cref="IS131Feature"/> but without the geometry surface —
/// information types in S-131 (FC §B.1) never carry geometry.
/// </summary>
public interface IS131InformationType
{
    /// <summary>The GML identifier of the source information type.</summary>
    string Id { get; }

    /// <summary>The raw information type code (e.g. <c>"ContactDetails"</c>).</summary>
    string TypeCode { get; }

    /// <summary>
    /// Cross-references from this information type to other objects in
    /// the dataset, resolved via <c>xlink:href</c>. Most concrete S-131
    /// information types do not declare outgoing references, but the
    /// surface is preserved for forward compatibility.
    /// </summary>
    ImmutableArray<S131ResolvedReference> ResolvedReferences { get; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    ImmutableDictionary<string, string> ExtraAttributes { get; }

    /// <summary>The originating raw GML information type instance.</summary>
    S131InformationType Source { get; }
}

/// <summary>
/// Typed projection of an S-131 <c>Authority</c> information type
/// (FC Ed 1.0.0 §B.1). A container-style record that aggregates
/// contact, applicability, and service-hours bindings for a port,
/// terminal, pilotage, or other operating authority.
/// </summary>
/// <remarks>
/// Authority is treated as an information type in this codebase per
/// the S-131 FC (<c>S100_FC_InformationType</c>); it never carries
/// geometry and is exclusively referenced by other features /
/// information types via <c>xlink:href</c>.
/// </remarks>
public sealed class S131Authority : IS131InformationType
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public string TypeCode => "Authority";

    /// <summary>The first resolved <c>contactDetails</c> peer, when present.</summary>
    public S131ContactDetails? ContactDetails { get; init; }

    /// <summary>The first resolved <c>applicability</c> peer, when present.</summary>
    public S131Applicability? Applicability { get; init; }

    /// <inheritdoc/>
    public ImmutableArray<S131ResolvedReference> ResolvedReferences { get; init; } =
        ImmutableArray<S131ResolvedReference>.Empty;
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    /// <inheritdoc/>
    public required S131InformationType Source { get; init; }
}

/// <summary>
/// Typed projection of an S-131 <c>ContactDetails</c> information type
/// (FC Ed 1.0.0 §B.1).
/// </summary>
public sealed class S131ContactDetails : IS131InformationType
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public string TypeCode => "ContactDetails";
    /// <inheritdoc/>
    public ImmutableArray<S131ResolvedReference> ResolvedReferences { get; init; } =
        ImmutableArray<S131ResolvedReference>.Empty;
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    /// <inheritdoc/>
    public required S131InformationType Source { get; init; }
}

/// <summary>
/// Typed projection of an S-131 <c>Applicability</c> information type
/// (FC Ed 1.0.0 §B.1).
/// </summary>
public sealed class S131Applicability : IS131InformationType
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public string TypeCode => "Applicability";
    /// <inheritdoc/>
    public ImmutableArray<S131ResolvedReference> ResolvedReferences { get; init; } =
        ImmutableArray<S131ResolvedReference>.Empty;
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    /// <inheritdoc/>
    public required S131InformationType Source { get; init; }
}

/// <summary>
/// Typed projection of an S-131 <c>AvailablePortServices</c>
/// information type (FC Ed 1.0.0 §B.1).
/// </summary>
public sealed class S131AvailablePortServices : IS131InformationType
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public string TypeCode => "AvailablePortServices";
    /// <inheritdoc/>
    public ImmutableArray<S131ResolvedReference> ResolvedReferences { get; init; } =
        ImmutableArray<S131ResolvedReference>.Empty;
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    /// <inheritdoc/>
    public required S131InformationType Source { get; init; }
}

/// <summary>
/// Typed projection of an S-131 <c>Entrance</c> information type
/// (FC Ed 1.0.0 §B.1).
/// </summary>
public sealed class S131Entrance : IS131InformationType
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public string TypeCode => "Entrance";
    /// <inheritdoc/>
    public ImmutableArray<S131ResolvedReference> ResolvedReferences { get; init; } =
        ImmutableArray<S131ResolvedReference>.Empty;
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    /// <inheritdoc/>
    public required S131InformationType Source { get; init; }
}

/// <summary>
/// Typed projection of an S-131 <c>ServiceHours</c> information type
/// (FC Ed 1.0.0 §B.1).
/// </summary>
public sealed class S131ServiceHours : IS131InformationType
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public string TypeCode => "ServiceHours";
    /// <inheritdoc/>
    public ImmutableArray<S131ResolvedReference> ResolvedReferences { get; init; } =
        ImmutableArray<S131ResolvedReference>.Empty;
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    /// <inheritdoc/>
    public required S131InformationType Source { get; init; }
}

/// <summary>
/// Typed projection of an S-131 <c>NonStandardWorkingDay</c>
/// information type (FC Ed 1.0.0 §B.1).
/// </summary>
public sealed class S131NonStandardWorkingDay : IS131InformationType
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public string TypeCode => "NonStandardWorkingDay";
    /// <inheritdoc/>
    public ImmutableArray<S131ResolvedReference> ResolvedReferences { get; init; } =
        ImmutableArray<S131ResolvedReference>.Empty;
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    /// <inheritdoc/>
    public required S131InformationType Source { get; init; }
}

/// <summary>
/// Typed projection of an S-131 information type derived from
/// <c>AbstractRxN</c> — i.e. <c>NauticalInformation</c>,
/// <c>Recommendations</c>, <c>Regulations</c>, or <c>Restrictions</c>
/// (FC Ed 1.0.0 §B.1). These types share a common attribute footprint
/// (textual <c>information</c>, <c>language</c>, optional file
/// locator) so they are projected into a single record discriminated by
/// <see cref="Kind"/>.
/// </summary>
public sealed class S131RxNInformation : IS131InformationType
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public required string TypeCode { get; init; }
    /// <summary>The concrete kind, decoded from <see cref="TypeCode"/>.</summary>
    public required S131RxNKind Kind { get; init; }
    /// <inheritdoc/>
    public ImmutableArray<S131ResolvedReference> ResolvedReferences { get; init; } =
        ImmutableArray<S131ResolvedReference>.Empty;
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    /// <inheritdoc/>
    public required S131InformationType Source { get; init; }
}

/// <summary>
/// Typed projection of an S-131 <c>SpatialQuality</c> information type
/// (FC Ed 1.0.0 §B.1).
/// </summary>
public sealed class S131SpatialQuality : IS131InformationType
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public string TypeCode => "SpatialQuality";
    /// <inheritdoc/>
    public ImmutableArray<S131ResolvedReference> ResolvedReferences { get; init; } =
        ImmutableArray<S131ResolvedReference>.Empty;
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    /// <inheritdoc/>
    public required S131InformationType Source { get; init; }
}

/// <summary>
/// Typed projection fallback for an information type whose
/// <see cref="S131InformationType.TypeCode"/> is not recognised in the
/// S-131 FC enumeration baked into the typed model. The projection
/// also emits an <c>s131.information.unknown</c> info diagnostic.
/// </summary>
public sealed class S131OtherInformationType : IS131InformationType
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public required string TypeCode { get; init; }
    /// <inheritdoc/>
    public ImmutableArray<S131ResolvedReference> ResolvedReferences { get; init; } =
        ImmutableArray<S131ResolvedReference>.Empty;
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    /// <inheritdoc/>
    public required S131InformationType Source { get; init; }
}
