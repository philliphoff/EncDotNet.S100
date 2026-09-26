using System.IO.Compression;
using System.Text.Json;
using System.Xml;
using EncDotNet.S100.Collections.ChartCatalogs;
using EncDotNet.S100.Collections.Usace;

namespace EncDotNet.S100.Collections.KnownSources;

/// <summary>What <see cref="CatalogueFormatDetector"/> found at a catalogue URL or in a stream.</summary>
/// <param name="RootElement">The document's root element (local name), or <see langword="null"/> when it is not XML.</param>
/// <param name="Format">The recognised, supported format, or <see langword="null"/>.</param>
/// <param name="Title">The catalogue's own title (<c>Header/title</c>), when it has one.</param>
/// <param name="IsJson">True when the document is JSON rather than XML (an S-100 feed, or something else).</param>
public sealed record CatalogueProbe(string? RootElement, KnownCatalogueFormat? Format, string? Title, bool IsJson = false)
{
    /// <summary>True when the document is an S-100 exchange catalogue, recognised but not yet supported online.</summary>
    public bool IsS100ExchangeCatalogue => RootElement == "S100_ExchangeCatalogue";
}

/// <summary>
/// Recognises an online chart catalogue's format from its root element
/// (issue #670): <c>EncProductCatalog</c> (NOAA), <c>IENC…ProductCatalog</c>
/// (USACE) and <c>RncProductCatalogChartCatalogs</c> (community lists) — and
/// S-100 feeds, JSON whose <c>format</c> is <c>encdotnet-s100-feed</c>
/// (issue #680). Only the start of the document is read.
/// </summary>
public static class CatalogueFormatDetector
{
    /// <summary>The format a root element names, or <see langword="null"/> when it is not a supported catalogue.</summary>
    public static KnownCatalogueFormat? FromRootElement(string? localName) => localName switch
    {
        "EncProductCatalog" => KnownCatalogueFormat.NoaaEnc,
        ChartCatalogsProductCatalogReader.RootElementName => KnownCatalogueFormat.ChartCatalogs,
        { } name when UsaceIencProductCatalogReader.IsCatalogueRoot(name) => KnownCatalogueFormat.UsaceIenc,
        _ => null,
    };

    /// <summary>
    /// Reads the root element and header title from <paramref name="stream"/>
    /// (gzip-compressed or not). A stream that cannot seek is read in full.
    /// </summary>
    public static CatalogueProbe Probe(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Peek for the gzip magic; a forward-only stream is copied first.
        var seekable = stream.CanSeek ? stream : Copy(stream);
        var start = seekable.Position;
        Span<byte> magic = stackalloc byte[2];
        var peeked = seekable.ReadAtLeast(magic, 2, throwOnEndOfStream: false);
        seekable.Position = start;
        Stream content = peeked == 2 && magic[0] == 0x1f && magic[1] == 0x8b
            ? new GZipStream(seekable, CompressionMode.Decompress, leaveOpen: true)
            : seekable;

        try
        {
            // Only the head is needed: at most 256 KB, decompressed.
            var head = ReadHead(content);
            var first = FirstSignificantByte(head);
            if (first == (byte)'{')
                return ProbeJson(head);

            using var reader = XmlReader.Create(new MemoryStream(head), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            });
            if (reader.MoveToContent() != XmlNodeType.Element)
                return new CatalogueProbe(null, null, null);

            var root = reader.LocalName;
            var format = FromRootElement(root);
            return new CatalogueProbe(root, format, format is null ? null : ReadHeaderTitle(reader));
        }
        catch (XmlException)
        {
            return new CatalogueProbe(null, null, null);
        }
        finally
        {
            if (!ReferenceEquals(content, seekable))
                content.Dispose();
            if (!ReferenceEquals(seekable, stream))
                seekable.Dispose();
        }
    }

    private const int HeadLength = 256 * 1024;

    private static byte[] ReadHead(Stream content)
    {
        var buffer = new byte[HeadLength];
        var length = content.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return buffer[..length];
    }

    /// <summary>The first byte that is not whitespace or a UTF-8 byte-order mark.</summary>
    private static byte? FirstSignificantByte(byte[] head)
    {
        var start = head.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        foreach (var b in head.AsSpan(start))
        {
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
                return b;
        }

        return null;
    }

    /// <summary>Reads the top-level <c>format</c> and <c>title</c> of a (possibly truncated) JSON head.</summary>
    private static CatalogueProbe ProbeJson(byte[] head)
    {
        string? format = null;
        string? title = null;
        try
        {
            var json = head.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? head.AsSpan(3) : head;
            var reader = new Utf8JsonReader(json, isFinalBlock: false, state: default);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return new CatalogueProbe(null, null, null, IsJson: true);

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName && (format is null || title is null))
            {
                var name = reader.GetString();
                if (!reader.Read())
                    break;
                if (reader.TokenType == JsonTokenType.String && name == "format")
                    format = reader.GetString();
                else if (reader.TokenType == JsonTokenType.String && name == "title")
                    title = reader.GetString();
                else if (!reader.TrySkip())
                    break;
            }
        }
        catch (JsonException)
        {
            // Not JSON after all, or truncated mid-token: go with what was read.
        }

        return format == Feeds.S100Feed.FormatName
            ? new CatalogueProbe(null, KnownCatalogueFormat.S100Feed, string.IsNullOrWhiteSpace(title) ? null : title, IsJson: true)
            : new CatalogueProbe(null, null, null, IsJson: true);
    }

    private static MemoryStream Copy(Stream stream)
    {
        var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        return copy;
    }

    /// <summary>Fetches the start of the document at <paramref name="uri"/> and probes it.</summary>
    /// <exception cref="HttpRequestException">The URL could not be fetched.</exception>
    public static async Task<CatalogueProbe> ProbeAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(uri);

        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        // Only the head of the document is needed; don't stream a 10 MB catalogue.
        var head = new MemoryStream();
        var buffer = new byte[16384];
        int read;
        while (head.Length < 256 * 1024
            && (read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            head.Write(buffer, 0, read);
        }

        head.Position = 0;
        return Probe(head);
    }

    /// <summary>Reads <c>Header/title</c> when the header comes first; stops at the first other element.</summary>
    private static string? ReadHeaderTitle(XmlReader reader)
    {
        try
        {
            if (!reader.Read() || reader.NodeType != XmlNodeType.Element || reader.LocalName != "Header")
                return null;

            using var header = reader.ReadSubtree();
            while (header.Read())
            {
                if (header.NodeType == XmlNodeType.Element && header.LocalName == "title")
                {
                    var title = header.ReadElementContentAsString().Trim();
                    return title.Length == 0 ? null : title;
                }
            }
        }
        catch (XmlException)
        {
            // A truncated head (e.g. only the first 256 KB) is fine: the root is known.
        }

        return null;
    }
}
