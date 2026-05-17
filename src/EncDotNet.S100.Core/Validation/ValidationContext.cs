namespace EncDotNet.S100.Validation;

/// <summary>
/// Per-run state available to validation rules during evaluation.
/// </summary>
/// <remarks>
/// <para>
/// The context is intentionally small. Most rules are pure functions of the
/// dataset being validated and do not need anything from the context. The
/// two ambient pieces of state worth surfacing are:
/// </para>
/// <list type="bullet">
///   <item>
///     <description><see cref="ReferenceTime"/> — the "now" against which
///     time-sensitive rules (e.g. a navigational warning whose expiry
///     date is in the past) should evaluate. Defaults to
///     <see cref="DateTimeOffset.UtcNow"/> but can be pinned for
///     reproducible test runs.</description>
///   </item>
///   <item>
///     <description><see cref="Services"/> — an optional service provider
///     for cross-dataset (Tier 3) rules that need to reach a sibling
///     dataset catalogue or other host-provided service. Single-dataset
///     (Tier 1/2) rules ignore this property entirely. Keeping the hook
///     here means cross-dataset support can be added without breaking the
///     <see cref="IValidationRule{TModel}"/> signature.</description>
///   </item>
/// </list>
/// </remarks>
public sealed class ValidationContext
{
    /// <summary>
    /// The reference time used by time-sensitive rules. Defaults to
    /// <see cref="DateTimeOffset.UtcNow"/>; override to make tests
    /// deterministic.
    /// </summary>
    public DateTimeOffset ReferenceTime { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional service provider through which Tier-3 cross-dataset rules
    /// can resolve host-provided services (for example, an
    /// <c>IDatasetCatalog</c> exposing sibling datasets). Null in
    /// single-dataset contexts.
    /// </summary>
    public IServiceProvider? Services { get; init; }

    /// <summary>A default context using the current UTC time and no service provider.</summary>
    public static ValidationContext Default { get; } = new();
}
