namespace EncDotNet.S100.Core;

/// <summary>
/// Says which product specification's catalogue a catalogue manager has
/// replaced, so that caches keyed on that catalogue can be dropped.
/// </summary>
/// <remarks>
/// Raised by the feature and portrayal catalogue managers after they have
/// evicted their own caches for <see cref="Spec"/>, so a handler that reads
/// the catalogue or its content hash sees the new one.
/// </remarks>
public sealed class CataloguesChangedEventArgs : EventArgs
{
    /// <summary>Creates the arguments for a change to <paramref name="spec"/>'s catalogue.</summary>
    /// <param name="spec">The product specification whose catalogue changed.</param>
    public CataloguesChangedEventArgs(SpecRef spec)
    {
        Spec = spec;
    }

    /// <summary>The product specification whose catalogue changed.</summary>
    public SpecRef Spec { get; }
}
