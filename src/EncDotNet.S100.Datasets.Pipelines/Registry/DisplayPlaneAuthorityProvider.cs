namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// The default <see cref="IDisplayPlaneAuthorityProvider"/>: exposes the
/// default-plane authority of a <see cref="ProductRegistry"/>
/// (<see cref="ProductRegistry.DisplayPlanes"/>) and never raises
/// <see cref="CurrentChanged"/> (the plane table is policy-invariant).
/// </summary>
public sealed class DisplayPlaneAuthorityProvider : IDisplayPlaneAuthorityProvider
{
    /// <summary>
    /// Creates a provider over <see cref="ProductRegistry.Default"/>.
    /// </summary>
    /// <remarks>
    /// A real parameterless constructor rather than an optional parameter, so
    /// reflective construction (<see cref="Activator.CreateInstance(Type)"/>,
    /// as the perf runner's version probe does) keeps working.
    /// </remarks>
    public DisplayPlaneAuthorityProvider()
        : this(null)
    {
    }

    /// <summary>
    /// Creates a provider over <paramref name="registry"/>, or over
    /// <see cref="ProductRegistry.Default"/> when it is <see langword="null"/>.
    /// </summary>
    /// <param name="registry">The products whose default planes to serve.</param>
    public DisplayPlaneAuthorityProvider(ProductRegistry? registry)
    {
        Current = (registry ?? ProductRegistry.Default).DisplayPlanes;
    }

    /// <inheritdoc />
    public IDisplayPlaneAuthority Current { get; }

    /// <inheritdoc />
    public event Action? CurrentChanged
    {
        add { }
        remove { }
    }
}
