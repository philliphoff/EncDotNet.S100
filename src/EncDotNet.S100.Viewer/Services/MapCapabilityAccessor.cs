namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default in-memory, settable implementation of
/// <see cref="ICapabilityAccessor{TCapability}"/> for a map capability that is
/// attached after container composition (the map surface is created by the UI).
/// </summary>
/// <typeparam name="TCapability">The focused map capability type.</typeparam>
internal sealed class MapCapabilityAccessor<TCapability> : ICapabilityAccessor<TCapability>
    where TCapability : class
{
    /// <inheritdoc />
    public TCapability? Current { get; set; }
}
