namespace EncDotNet.S100.Hosting;

/// <summary>
/// A late-bound slot that may or may not currently hold a host capability of
/// type <typeparamref name="TCapability"/>.
/// </summary>
/// <remarks>
/// <para>
/// Interactive-map capabilities (viewport control, coordinate conversion,
/// snapshot rendering, presentation, and so on) are often unavailable at
/// container-composition time — the map surface they wrap is created later, by
/// the UI. Consumers therefore depend on
/// <see cref="ICapabilityAccessor{TCapability}"/> rather than the capability
/// directly, and read <see cref="Current"/> at the point of use, tolerating a
/// <see langword="null"/> until the capability is wired up.
/// </para>
/// <para>
/// The type parameter is covariant so an accessor of a concrete capability
/// satisfies an accessor of any interface it implements.
/// </para>
/// </remarks>
public interface ICapabilityAccessor<out TCapability>
    where TCapability : class
{
    /// <summary>
    /// The capability if it is currently available; otherwise
    /// <see langword="null"/>.
    /// </summary>
    TCapability? Current { get; }
}

/// <summary>
/// An <see cref="ICapabilityAccessor{TCapability}"/> that always resolves to a
/// single, immutable capability instance supplied at construction.
/// </summary>
public sealed class StaticCapabilityAccessor<TCapability>(TCapability current)
    : ICapabilityAccessor<TCapability>
    where TCapability : class
{
    /// <inheritdoc />
    public TCapability Current { get; } = current
        ?? throw new ArgumentNullException(nameof(current));
}
