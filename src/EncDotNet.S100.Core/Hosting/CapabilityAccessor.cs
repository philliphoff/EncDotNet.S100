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

/// <summary>
/// An <see cref="ICapabilityAccessor{TCapability}"/> for a host with several
/// maps, such as a viewer with its map split into panes. <see cref="ICapabilityAccessor{TCapability}.Current"/>
/// is the active map's capability; <see cref="Get"/> reaches any map's by its id.
/// </summary>
/// <remarks>
/// A tool that takes an optional map id resolves it through
/// <see cref="Get"/> when its accessor is one of these, and refuses a map id
/// when it isn't (the host has one map).
/// </remarks>
public interface IMapCapabilityAccessor<out TCapability> : ICapabilityAccessor<TCapability>
    where TCapability : class
{
    /// <summary>The ids of the host's maps.</summary>
    IReadOnlyList<string> Maps { get; }

    /// <summary>
    /// The capability of the map with id <paramref name="map"/>, or
    /// <see langword="null"/> when there is no such map or it isn't available yet.
    /// </summary>
    /// <param name="map">A map id from <see cref="Maps"/>.</param>
    /// <returns>The capability, or <see langword="null"/>.</returns>
    TCapability? Get(string map);
}
