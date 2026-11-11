using System.Text;

namespace EncDotNet.S100.Core;

/// <summary>
/// A read-only, in-memory view of an asset previously read from an
/// <see cref="IAssetSource"/>. Backed by a <see cref="ReadOnlyMemory{T}"/>
/// so cache hits can be served without copying.
/// </summary>
/// <remarks>
/// Intended for small, read-only assets (Lua source, XSLT templates,
/// SVG symbols, palette XML, Feature/Portrayal Catalogue documents)
/// whose contents are immutable for the lifetime of the originating
/// source. See S-100 Edition 5.2.1 Part 4 (Feature Catalogue) and
/// Part 9 (Portrayal Catalogue) for the canonical asset shapes.
/// </remarks>
/// <param name="Bytes">The asset contents.</param>
/// <param name="RelativePath">
/// The forward-slash relative path that produced these bytes, preserved
/// for diagnostics and to keep <see cref="AssetBytes"/> self-describing.
/// </param>
public readonly record struct AssetBytes(ReadOnlyMemory<byte> Bytes, string RelativePath)
{
    /// <summary>
    /// Returns a non-seekable-aware <see cref="Stream"/> over the asset
    /// bytes. The returned stream is read-only and does not own the
    /// underlying buffer; disposing it does not affect the cached
    /// <see cref="AssetBytes"/> instance.
    /// </summary>
    /// <returns>A stream positioned at the start of the asset.</returns>
    public Stream AsStream() => new ReadOnlyMemoryStream(Bytes);

    /// <summary>
    /// Decodes the asset bytes as text using the supplied
    /// <paramref name="encoding"/> (defaulting to UTF-8 when null).
    /// </summary>
    /// <param name="encoding">
    /// The encoding to use, or null for UTF-8 (the default for S-100
    /// XML and Lua content).
    /// </param>
    public string AsString(Encoding? encoding = null) =>
        (encoding ?? Encoding.UTF8).GetString(Bytes.Span);
}
