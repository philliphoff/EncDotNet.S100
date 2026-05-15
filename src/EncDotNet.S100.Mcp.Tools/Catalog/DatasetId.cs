namespace EncDotNet.S100.Mcp.Tools.Catalog;

/// <summary>
/// Stable identifier for a dataset within an <see cref="IDatasetCatalog"/>.
/// </summary>
/// <remarks>
/// IDs are opaque strings minted by the catalog implementation. They must
/// be stable for the lifetime of a host session (e.g. while a viewer is
/// running) so that tool callers can hold onto an ID across multiple
/// invocations.
/// </remarks>
public readonly record struct DatasetId
{
    /// <summary>The underlying opaque identifier value.</summary>
    public string Value { get; }

    /// <summary>Creates a new <see cref="DatasetId"/>.</summary>
    /// <exception cref="ArgumentException">The value is null or empty.</exception>
    public DatasetId(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>Returns the underlying string value.</summary>
    public override string ToString() => Value;
}
