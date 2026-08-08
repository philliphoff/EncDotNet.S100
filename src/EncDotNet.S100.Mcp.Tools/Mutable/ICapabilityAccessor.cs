namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>
/// Provides late-bound access to one host capability used by a mutating tool.
/// </summary>
/// <remarks>
/// <para>
/// The mutating tools depend on host capabilities (presentation, time, viewport,
/// rendering, pixel conversion) that may not exist for the whole life of the MCP
/// server. In the desktop viewer a capability only attaches once the map control
/// has been constructed; in the headless CLI session it is present from the
/// start. This accessor lets one shared tool bind to both: it reads
/// <see cref="Current"/> per invocation and surfaces a <c>host_not_ready</c>
/// error (<see cref="EncDotNet.S100.Datasets.Pipelines.Query.HostNotReady"/>)
/// when it is still <see langword="null"/>.
/// </para>
/// <para>
/// Covariant so a <c>ICapabilityAccessor&lt;ConcreteCapability&gt;</c> satisfies a
/// parameter of <c>ICapabilityAccessor&lt;ICapability&gt;</c>.
/// </para>
/// </remarks>
/// <typeparam name="TCapability">The focused capability type.</typeparam>
public interface ICapabilityAccessor<out TCapability>
    where TCapability : class
{
    /// <summary>
    /// The current capability instance, or <see langword="null"/> before it has
    /// been attached to the host.
    /// </summary>
    TCapability? Current { get; }
}

/// <summary>
/// Trivial <see cref="ICapabilityAccessor{TCapability}"/> over a value that is
/// known at construction time — the headless case, where the capability exists
/// for the whole life of the session.
/// </summary>
/// <typeparam name="TCapability">The focused capability type.</typeparam>
/// <param name="current">The always-present capability.</param>
public sealed class StaticCapabilityAccessor<TCapability>(TCapability current)
    : ICapabilityAccessor<TCapability>
    where TCapability : class
{
    /// <inheritdoc />
    public TCapability Current { get; } = current
        ?? throw new ArgumentNullException(nameof(current));
}
