using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Collections.RemoteCatalogues;

/// <summary>
/// A remote S-100 exchange catalogue read into online items: one per
/// dataset, with its coverage, edition, folder and download URL.
/// </summary>
/// <param name="CatalogUri">Where the catalogue was read from.</param>
/// <param name="RootUri">
/// The exchange set's root: the deepest folder holding both the catalogue's
/// folder and every dataset (ending in <c>/</c>). Item folders are relative to it.
/// </param>
/// <param name="IssuedAt">The catalogue's own date-time (<c>identifier/dateTime</c>), if it parses.</param>
/// <param name="Items">Every dataset, in catalogue order, without sizes.</param>
/// <param name="Diagnostics">Problems met while reading; none are fatal.</param>
public sealed record RemoteS100Catalogue(
    Uri CatalogUri,
    Uri RootUri,
    DateTimeOffset? IssuedAt,
    IReadOnlyList<CollectionItem> Items,
    IReadOnlyList<IndexDiagnostic> Diagnostics)
{
    /// <summary>
    /// The item property holding the dataset's folder relative to
    /// <see cref="RootUri"/> (e.g. <c>Northeast/Long_Island_Sound</c>; empty at
    /// the root). It is also the item's group (<see cref="LocalManifestIndexer.GroupProperty"/>).
    /// </summary>
    public const string FolderProperty = LocalManifestIndexer.GroupProperty;

    /// <summary>The item property holding the coverage's approximate grid resolution in metres (e.g. <c>4</c>).</summary>
    public const string GridResolutionProperty = "gridResolution";

    /// <summary>The item property holding the catalogue's <c>navigationPurpose</c> (e.g. <c>port</c>).</summary>
    public const string NavigationPurposeProperty = "navigationPurpose";

    /// <summary>The folder of <paramref name="item"/>, relative to the root.</summary>
    public static string FolderOf(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Properties.GetValueOrDefault(FolderProperty) ?? string.Empty;
    }

    /// <summary>A folder's display name: its last segment with underscores as spaces ("Long Island Sound").</summary>
    public static string FolderName(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var trimmed = folder.Trim('/');
        var leaf = trimmed[(trimmed.LastIndexOf('/') + 1)..];
        return leaf.Replace('_', ' ');
    }

    /// <summary>The first segment of a folder (the region, for NOAA's S-102), or empty at the root.</summary>
    public static string TopFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var trimmed = folder.Trim('/');
        var slash = trimmed.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? trimmed : trimmed[..slash];
    }

    /// <summary>The URL of a folder (relative to <see cref="RootUri"/>), ending in <c>/</c>.</summary>
    public Uri FolderUri(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var trimmed = folder.Trim('/');
        return trimmed.Length == 0
            ? RootUri
            : new Uri(RootUri, string.Join('/', trimmed.Split('/').Select(Uri.EscapeDataString)) + "/");
    }
}

/// <summary>Reads a <see cref="RemoteS100Catalogue"/> from a downloaded S-100 <c>CATALOG.XML</c>.</summary>
public static partial class RemoteS100CatalogueReader
{
    /// <summary>
    /// Reads the catalogue in <paramref name="stream"/>, which may be
    /// gzip-compressed (NOAA serves some copies with
    /// <c>Content-Encoding: gzip</c>), resolving dataset file names against
    /// <paramref name="catalogUri"/>.
    /// </summary>
    /// <param name="stream">The catalogue's bytes.</param>
    /// <param name="catalogUri">Where the catalogue was fetched from.</param>
    /// <param name="downloadFolder">The managed download folder items go to (see <see cref="RemoteItemLocation.DownloadFolder"/>).</param>
    /// <exception cref="System.Xml.XmlException">The content is not an S-100 exchange catalogue.</exception>
    public static RemoteS100Catalogue Read(Stream stream, Uri catalogUri, string? downloadFolder = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(catalogUri);

        using var content = OpenMaybeGzip(stream);
        var catalogue = ExchangeCatalogueReader.Read(content, ExchangeCatalogueReadOptions.DiscoveryOnly);
        var diagnostics = new List<IndexDiagnostic>();
        var context = new ExchangeSetContext(string.Empty, false, string.Empty, "CATALOG.XML", string.Empty, _ => null);

        // Build the items as for a local exchange set, then point each at its URL.
        var located = new List<(CollectionItem Item, Uri Uri, string FileName)>();
        foreach (var item in ExchangeSetItemReader.ReadS100(catalogue, context, diagnostics))
        {
            var local = (LocalItemLocation)item.Location;
            Uri uri;
            try
            {
                uri = new Uri(catalogUri, local.RelativePath);
            }
            catch (UriFormatException)
            {
                diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Warning, "The file name is not a valid URL.", local.RelativePath));
                continue;
            }

            if (uri.Scheme is not ("http" or "https"))
            {
                diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Warning, "The file is not on the web.", local.RelativePath));
                continue;
            }

            if (local.UpdateRelativePaths.Count > 0)
            {
                diagnostics.Add(new IndexDiagnostic(
                    IndexDiagnosticSeverity.Info, "Update files are not downloaded from remote catalogues.", local.RelativePath));
            }

            located.Add((item, uri, Path.GetFileName(uri.LocalPath)));
        }

        var root = CommonFolder([new Uri(catalogUri, "./"), .. located.Select(l => new Uri(l.Uri, "./"))]);
        var items = located.Select(l => Map(l.Item, l.Uri, l.FileName, root, downloadFolder)).ToArray();
        return new RemoteS100Catalogue(catalogUri, root, ParseDateTime(catalogue.Identifier.DateTime), items, diagnostics);
    }

    /// <summary>
    /// A dataset's name without the version or run-time tokens some
    /// producers append to their file names, so the item keeps its identity
    /// across editions. NOAA appends digits to S-102 tile names
    /// (<c>102US004SC1EV262247</c> → <c>102US004SC1EV</c>) and a run time to
    /// forecast tiles (<c>104US004SC1BO_20251217T12Z</c> → <c>104US004SC1BO</c>).
    /// Other names are kept as they are.
    /// </summary>
    public static string StableName(string stem)
    {
        ArgumentNullException.ThrowIfNull(stem);
        var name = RunTimeToken().Replace(stem, string.Empty);
        var tile = VersionedTileName().Match(name);
        return tile.Success ? tile.Groups[1].Value : name;
    }

    private static CollectionItem Map(CollectionItem item, Uri uri, string fileName, Uri root, string? downloadFolder)
    {
        var folderUri = new Uri(uri, "./");
        var folder = Uri.UnescapeDataString(root.MakeRelativeUri(folderUri).OriginalString).Trim('/');
        var name = StableName(Path.GetFileNameWithoutExtension(fileName));

        var properties = new Dictionary<string, string>(item.Properties, StringComparer.Ordinal)
        {
            [RemoteS100Catalogue.FolderProperty] = folder,
        };
        if (folder.Length > 0)
            properties[LocalManifestIndexer.GroupNameProperty] = RemoteS100Catalogue.FolderName(folder);

        return item with
        {
            Key = folder.Length == 0 ? name : folder + "/" + name,
            Name = name,
            Title = item.Title?.Replace('_', ' '),
            GroupKey = null,
            Status = CollectionItemStatus.Active,
            Location = new RemoteItemLocation(uri, DownloadFolder: downloadFolder, Layout: new PackageLayout(fileName, [])),
            Properties = properties,
        };
    }

    /// <summary>The deepest folder URL that contains every one of <paramref name="folders"/> (each ending in <c>/</c>).</summary>
    private static Uri CommonFolder(IReadOnlyList<Uri> folders)
    {
        var first = folders[0];
        var segments = first.AbsolutePath.Split('/');
        var common = segments.Length - 1;
        foreach (var folder in folders.Skip(1))
        {
            if (!string.Equals(folder.GetLeftPart(UriPartial.Authority), first.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
                return new Uri(first, "/");
            var other = folder.AbsolutePath.Split('/');
            var shared = 0;
            while (shared < Math.Min(common, other.Length - 1) && segments[shared] == other[shared])
                shared++;
            common = shared;
        }

        return new Uri(first, string.Join('/', segments.Take(common)) + "/");
    }

    private static Stream OpenMaybeGzip(Stream stream)
    {
        var buffered = stream.CanSeek ? stream : CopyToMemory(stream);
        var start = buffered.Position;
        var b1 = buffered.ReadByte();
        var b2 = buffered.ReadByte();
        buffered.Position = start;
        return b1 == 0x1F && b2 == 0x8B
            ? new GZipStream(buffered, CompressionMode.Decompress, leaveOpen: true)
            : new NonClosingStream(buffered);
    }

    private static MemoryStream CopyToMemory(Stream stream)
    {
        var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        return memory;
    }

    private static DateTimeOffset? ParseDateTime(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    /// <summary>A run-time token such as <c>_20251217T12Z</c> or <c>_20260930T180000Z</c>.</summary>
    [GeneratedRegex(@"_\d{8}T\d{2,6}Z", RegexOptions.CultureInvariant)]
    private static partial Regex RunTimeToken();

    /// <summary>
    /// An S-100 tile name (product, producer, band digit, five-character
    /// cell) followed by a numeric version suffix.
    /// </summary>
    [GeneratedRegex(@"^(\d{3}[A-Z0-9]{4}\d[A-Z0-9]{5})\d{4,}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionedTileName();

    /// <summary>Leaves the caller's stream open when the reader disposes its wrapper.</summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
