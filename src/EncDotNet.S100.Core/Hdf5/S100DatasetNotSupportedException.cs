namespace EncDotNet.S100.Hdf5;

/// <summary>
/// Thrown when an S-100 dataset uses an optional spec feature
/// (data-coding format, CRS variant, attribute encoding, etc.) that
/// the current reader does not yet implement. Distinct from
/// <see cref="S100DatasetSchemaException"/>: the file is conforming;
/// it just exercises a region of the spec we haven't built yet.
/// </summary>
public sealed class S100DatasetNotSupportedException : Exception
{
    /// <summary>Product code, e.g. <c>"S-104"</c>.</summary>
    public string Product { get; }

    /// <summary>
    /// File name (without path) the failure relates to, or <c>null</c>
    /// when the dataset was opened from a stream and the caller hasn't
    /// yet attached the source name (see <see cref="WithFile"/>).
    /// </summary>
    public string? File { get; }

    /// <summary>
    /// Human-readable description of the unsupported feature, e.g.
    /// <c>"data coding format 8 (time series at fixed stations)"</c>.
    /// </summary>
    public string Feature { get; }

    /// <summary>
    /// Citation into the relevant S-100 spec, e.g.
    /// <c>"S-100 Part 10c §10.2.1"</c>. <c>null</c> when no
    /// authoritative section number is known.
    /// </summary>
    public string? SpecReference { get; }

    /// <summary>
    /// Initializes a new <see cref="S100DatasetNotSupportedException"/>.
    /// </summary>
    public S100DatasetNotSupportedException(
        string product,
        string? file,
        string feature,
        string? specReference,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(message);

        Product = product;
        File = file;
        Feature = feature;
        SpecReference = specReference;
    }

    /// <summary>
    /// Returns a copy of this exception with <see cref="File"/> set to
    /// <paramref name="file"/>. Used by dataset processors to attach the
    /// source file name to an exception thrown by a reader that only had
    /// a stream.
    /// </summary>
    public S100DatasetNotSupportedException WithFile(string? file)
    {
        if (string.Equals(File, file, StringComparison.Ordinal))
            return this;

        return new S100DatasetNotSupportedException(
            Product,
            file,
            Feature,
            SpecReference,
            ExceptionMessageFormatter.FormatNotSupported(Product, file, Feature, SpecReference, trailingHint: null),
            InnerException);
    }
}
