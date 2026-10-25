using System.Text;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Decides, from a file's leading bytes and its extension, whether the file
/// belongs to a particular product. Contributed per
/// <see cref="ProductRegistration"/> by formats with a signature of their own
/// (GRIB2 starts with <c>GRIB</c>, NetCDF-3 with <c>CDF\x01</c>), alongside the
/// envelope matchers (<see cref="DatasetIso8211Matcher"/>,
/// <see cref="DatasetGmlMatcher"/>, <see cref="DatasetHdf5Matcher"/>).
/// <see cref="DatasetPipelineFactory"/> consults it for every file no envelope
/// matcher claimed, whatever its extension, so a matcher must check the
/// signature and treat the extension as a hint at most.
/// <para>
/// Matchers must be <b>mutually exclusive</b>: each recognizes only its own
/// format's signature, so the result does not depend on the order
/// registrations are enumerated.
/// </para>
/// </summary>
/// <param name="header">The file's leading bytes and its extension.</param>
public delegate bool DatasetBytesMatcher(DatasetHeader header);

/// <summary>
/// The leading bytes of a dataset file and its extension, read once by
/// <see cref="DatasetPipelineFactory"/> and handed to each product's
/// <see cref="DatasetBytesMatcher"/>.
/// </summary>
public readonly record struct DatasetHeader
{
    /// <summary>How many leading bytes the factory reads (fewer for a shorter file).</summary>
    public const int MaxLength = 1024;

    /// <summary>The file's leading bytes, at most <see cref="MaxLength"/>; empty when unreadable.</summary>
    public required ReadOnlyMemory<byte> LeadingBytes { get; init; }

    /// <summary>
    /// The file's extension including the dot (e.g. <c>".grib2"</c>), or empty
    /// when it has none. A NOMADS GRIB filter download, for one, has no useful
    /// extension.
    /// </summary>
    public required string Extension { get; init; }

    /// <summary>Whether <see cref="LeadingBytes"/> starts with <paramref name="signature"/>.</summary>
    /// <param name="signature">The bytes to look for at offset 0.</param>
    public bool StartsWith(ReadOnlySpan<byte> signature) =>
        LeadingBytes.Span.StartsWith(signature);

    /// <summary>
    /// Whether <see cref="LeadingBytes"/> starts with the ASCII text
    /// <paramref name="signature"/> (e.g. <c>"GRIB"</c>), compared exactly.
    /// </summary>
    /// <param name="signature">The ASCII signature to look for at offset 0.</param>
    public bool StartsWithAscii(string signature)
    {
        ArgumentException.ThrowIfNullOrEmpty(signature);
        return StartsWith(Encoding.ASCII.GetBytes(signature));
    }

    /// <summary>Whether <see cref="Extension"/> is <paramref name="extension"/>, ignoring case.</summary>
    /// <param name="extension">The extension including the dot, e.g. <c>".grib2"</c>.</param>
    public bool HasExtension(string extension) =>
        string.Equals(Extension, extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The 8-byte HDF5 format signature (HDF5 File Format Specification §II.A).</summary>
    internal static ReadOnlySpan<byte> Hdf5Signature => [0x89, (byte)'H', (byte)'D', (byte)'F', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Whether the file starts with the HDF5 signature.</summary>
    public bool IsHdf5 => StartsWith(Hdf5Signature);
}
