namespace EncDotNet.S100.Hdf5;

/// <summary>
/// Thrown when a required HDF5 attribute, dataset, or group is missing
/// from a location that the S-100 spec requires it on, or is present
/// but malformed in a way that cannot be reasonably tolerated. The
/// exception carries the product, file (when known), the HDF5 group
/// path that was being read, the offending attribute/dataset name (when
/// applicable), and a citation into the relevant spec section so that
/// callers can produce actionable diagnostics.
/// </summary>
public sealed class S100DatasetSchemaException : Exception
{
    /// <summary>Product code, e.g. <c>"S-104"</c>.</summary>
    public string Product { get; }

    /// <summary>
    /// File name (without path) the failure relates to, or <c>null</c>
    /// when the dataset was opened from a stream and the caller hasn't
    /// yet attached the source name (see <see cref="WithFile"/>).
    /// </summary>
    public string? File { get; }

    /// <summary>HDF5 group path, e.g. <c>"/WaterLevel/WaterLevel.03"</c>.</summary>
    public string GroupPath { get; }

    /// <summary>
    /// Name of the missing or malformed attribute or dataset; <c>null</c>
    /// when the failure is the absence of the group itself.
    /// </summary>
    public string? AttributeOrDataset { get; }

    /// <summary>
    /// Citation into the relevant S-100 spec, e.g.
    /// <c>"S-100 Part 10c §10.2.1.2"</c>. <c>null</c> when no
    /// authoritative section number is known.
    /// </summary>
    public string? SpecReference { get; }

    /// <summary>
    /// Initializes a new <see cref="S100DatasetSchemaException"/>.
    /// </summary>
    public S100DatasetSchemaException(
        string product,
        string? file,
        string groupPath,
        string? attributeOrDataset,
        string? specReference,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(groupPath);
        ArgumentNullException.ThrowIfNull(message);

        Product = product;
        File = file;
        GroupPath = groupPath;
        AttributeOrDataset = attributeOrDataset;
        SpecReference = specReference;
    }

    /// <summary>
    /// Returns a copy of this exception with <see cref="File"/> set to
    /// <paramref name="file"/>. Used by dataset processors to attach the
    /// source file name to an exception thrown by a reader that only had
    /// a stream.
    /// </summary>
    public S100DatasetSchemaException WithFile(string? file)
    {
        if (string.Equals(File, file, StringComparison.Ordinal))
            return this;

        return new S100DatasetSchemaException(
            Product,
            file,
            GroupPath,
            AttributeOrDataset,
            SpecReference,
            ExceptionMessageFormatter.FormatSchema(Product, file, GroupPath, AttributeOrDataset, SpecReference),
            InnerException);
    }
}
