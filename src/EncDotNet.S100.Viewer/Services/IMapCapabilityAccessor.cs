namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Provides late-bound access to one map capability.
/// </summary>
/// <typeparam name="TCapability">The focused map capability type.</typeparam>
internal interface IMapCapabilityAccessor<out TCapability>
    where TCapability : class
{
    /// <summary>The current capability, or <see langword="null"/> before attachment.</summary>
    TCapability? Current { get; }
}

/// <summary>
/// Default in-memory implementation of
/// <see cref="IMapCapabilityAccessor{TCapability}"/>.
/// </summary>
/// <typeparam name="TCapability">The focused map capability type.</typeparam>
internal sealed class MapCapabilityAccessor<TCapability> : IMapCapabilityAccessor<TCapability>
    where TCapability : class
{
    /// <inheritdoc />
    public TCapability? Current { get; set; }
}
