namespace EncDotNet.S100.Viewer.Services.McpCapabilities;

/// <summary>
/// A late-bound <see cref="ICapabilityAccessor{TCapability}"/> whose
/// <see cref="Current"/> is produced by a delegate on each read.
/// </summary>
/// <remarks>
/// The desktop viewer's capabilities attach only after the map control and its
/// view-models finish constructing, so the shared mutating tools must bind to
/// them before they exist. This accessor bridges the viewer's own late-bound
/// accessors (<see cref="ICapabilityAccessor{TCapability}"/>,
/// <see cref="IRenderStateControllerAccessor"/>) onto the shared
/// <see cref="ICapabilityAccessor{TCapability}"/> the tools expect: the read
/// delegate returns <see langword="null"/> while the underlying viewer service
/// is unattached — which the tools surface as <c>host_not_ready</c> — and a
/// freshly wrapped capability once it is.
/// </remarks>
/// <typeparam name="TCapability">The focused shared capability type.</typeparam>
/// <param name="read">
/// Produces the current capability, or <see langword="null"/> when the backing
/// viewer service is not yet attached. Invoked once per <see cref="Current"/>
/// read.
/// </param>
internal sealed class DelegatingCapabilityAccessor<TCapability>(Func<TCapability?> read)
    : ICapabilityAccessor<TCapability>
    where TCapability : class
{
    private readonly Func<TCapability?> _read = read
        ?? throw new ArgumentNullException(nameof(read));

    /// <inheritdoc />
    public TCapability? Current => _read();
}
