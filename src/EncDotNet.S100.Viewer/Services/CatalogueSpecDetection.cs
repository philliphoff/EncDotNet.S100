using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using EncDotNet.S100.Core;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Tells what a catalogue file, folder or package is from its content: a
/// feature catalogue XML file, a portrayal catalogue folder, or a portrayal
/// catalogue ZIP package (as S-164 delivers them), and which product it is
/// for. Also reads version metadata from the bundled built-in catalogues.
/// </summary>
/// <remarks>
/// Detection never looks at file names to choose the product and never
/// assumes one (design note <c>catalogue-panels.md</c> D5, A1): a catalogue
/// that doesn't say which product it is for is refused with a reason.
/// </remarks>
internal static class CatalogueSpecDetection
{
    /// <summary>The portrayal catalogue's root file, S-100 Part 9 §9.</summary>
    public const string PortrayalCatalogueFileName = "portrayal_catalogue.xml";

    private const string FeatureCatalogueRoot = "S100_FC_FeatureCatalogue";
    private const string PortrayalCatalogueRoot = "portrayalCatalog";

    private static readonly Regex SpecPattern = new(@"S-\d+", RegexOptions.Compiled);

    // Bundled content never changes in a process, so its metadata is read once.
    private static readonly ConcurrentDictionary<string, CatalogueMetadata?> BuiltInFeature = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, CatalogueMetadata?> BuiltInPortrayal = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads <paramref name="path"/> and tells what catalogue it is: a feature
    /// catalogue XML file, a portrayal catalogue folder (or a
    /// <c>portrayal_catalogue.xml</c> file in one), or a portrayal catalogue
    /// ZIP package.
    /// </summary>
    /// <param name="path">The file or folder the user chose.</param>
    /// <returns>The catalogue, or the reason it can't be used.</returns>
    public static CatalogueDetectionResult Detect(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return CatalogueDetectionResult.Refused(Strings.Catalogues_Refused_NotFound);

        try
        {
            if (Directory.Exists(path))
                return DetectFolder(path);
            if (!File.Exists(path))
                return CatalogueDetectionResult.Refused(Strings.Catalogues_Refused_NotFound);
            if (IsZip(path))
                return DetectZip(path);
            return DetectXmlFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CatalogueDetectionResult.Refused(string.Format(Strings.Catalogues_Refused_CantRead, ex.Message));
        }
    }

    private static CatalogueDetectionResult DetectFolder(string folder)
    {
        var catalogueFolder = FindPortrayalCatalogueFolder(folder);
        if (catalogueFolder is null)
            return CatalogueDetectionResult.Refused(Strings.Catalogues_Refused_NoPortrayalXmlInFolder);

        using var stream = File.OpenRead(Path.Combine(catalogueFolder, PortrayalCatalogueFileName));
        return ReadPortrayal(stream, folder, catalogueFolder, zipBasePath: null);
    }

    // The catalogue's own folder, or one folder down (a download that unpacks
    // into "PortrayalCatalog/"), so a user can pick either.
    private static string? FindPortrayalCatalogueFolder(string folder)
    {
        if (File.Exists(Path.Combine(folder, PortrayalCatalogueFileName)))
            return folder;
        var nested = Directory.EnumerateDirectories(folder)
            .Where(d => File.Exists(Path.Combine(d, PortrayalCatalogueFileName)))
            .Take(2)
            .ToList();
        return nested.Count == 1 ? nested[0] : null;
    }

    private static CatalogueDetectionResult DetectZip(string zipPath)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(zipPath);
        }
        catch (InvalidDataException ex)
        {
            return CatalogueDetectionResult.Refused(string.Format(Strings.Catalogues_Refused_CantRead, ex.Message));
        }

        using (archive)
        {
            // The shallowest portrayal_catalogue.xml is the package's root.
            var entry = archive.Entries
                .Where(e => string.Equals(e.Name, PortrayalCatalogueFileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName.Count(c => c == '/'))
                .FirstOrDefault();
            if (entry is null)
                return CatalogueDetectionResult.Refused(Strings.Catalogues_Refused_NoPortrayalXmlInPackage);

            var basePath = entry.FullName[..^entry.Name.Length];
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;
            return ReadPortrayal(buffer, zipPath, zipPath, basePath);
        }
    }

    private static CatalogueDetectionResult DetectXmlFile(string filePath)
    {
        string root;
        try
        {
            using var stream = File.OpenRead(filePath);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            reader.MoveToContent();
            root = reader.LocalName;
        }
        catch (XmlException ex)
        {
            return CatalogueDetectionResult.Refused(string.Format(Strings.Catalogues_Refused_NotXml, ex.Message));
        }

        if (root == PortrayalCatalogueRoot)
        {
            // The portrayal catalogue's root file: the catalogue is its folder.
            var folder = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
            if (!string.Equals(Path.GetFileName(filePath), PortrayalCatalogueFileName, StringComparison.OrdinalIgnoreCase))
                return CatalogueDetectionResult.Refused(Strings.Catalogues_Refused_NoPortrayalXmlInFolder);
            return DetectFolder(folder);
        }

        if (root != FeatureCatalogueRoot)
            return CatalogueDetectionResult.Refused(string.Format(Strings.Catalogues_Refused_UnknownRoot, root));

        FeatureCatalogue catalogue;
        try
        {
            using var stream = File.OpenRead(filePath);
            catalogue = FeatureCatalogueReader.Read(stream);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or FormatException)
        {
            return CatalogueDetectionResult.Refused(string.Format(Strings.Catalogues_Refused_BadFeatureCatalogue, ex.Message));
        }

        var spec = FeatureCatalogueSpec(catalogue);
        if (spec is null)
            return CatalogueDetectionResult.Refused(Strings.Catalogues_Refused_NoFeatureProduct);

        return CatalogueDetectionResult.Found(new DetectedCatalogue(
            CatalogueKind.Feature,
            spec,
            Path.GetFullPath(filePath),
            new CatalogueMetadata(NullIfEmpty(catalogue.Name), NullIfEmpty(catalogue.VersionNumber), NullIfEmpty(catalogue.VersionDate)),
            Path.GetFullPath(filePath),
            ZipBasePath: null));
    }

    private static CatalogueDetectionResult ReadPortrayal(Stream stream, string chosenPath, string sourcePath, string? zipBasePath)
    {
        PortrayalCatalogue catalogue;
        try
        {
            catalogue = PortrayalCatalogueReader.Read(stream);
        }
        catch (XmlException ex)
        {
            return CatalogueDetectionResult.Refused(string.Format(Strings.Catalogues_Refused_BadPortrayalCatalogue, ex.Message));
        }

        if (!SpecName.TryNormalize(catalogue.ProductId, out var spec) || SpecName.IsNonS100Format(spec))
            return CatalogueDetectionResult.Refused(Strings.Catalogues_Refused_NoPortrayalProduct);

        return CatalogueDetectionResult.Found(new DetectedCatalogue(
            CatalogueKind.Portrayal,
            spec,
            Path.GetFullPath(chosenPath),
            new CatalogueMetadata(Name: null, NullIfEmpty(catalogue.Version), VersionDate: null),
            Path.GetFullPath(sourcePath),
            zipBasePath));
    }

    // The declared productId; failing that, the first "S-NNN" in the
    // catalogue's name, which is how some bundled catalogues (S-421) say it.
    private static string? FeatureCatalogueSpec(FeatureCatalogue catalogue)
    {
        if (SpecName.TryNormalize(catalogue.ProductId, out var declared) && !SpecName.IsNonS100Format(declared))
            return declared;
        var match = SpecPattern.Match(catalogue.Name ?? "");
        return match.Success && SpecName.TryNormalize(match.Value, out var named) ? named : null;
    }

    private static bool IsZip(string path)
    {
        Span<byte> magic = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        return stream.Read(magic) == 4 && magic[0] == 'P' && magic[1] == 'K' && magic[2] == 3 && magic[3] == 4;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// The name, version and version date of the bundled feature catalogue for
    /// <paramref name="spec"/>, or <see langword="null"/> when it isn't
    /// bundled or can't be read. Read once per process.
    /// </summary>
    public static CatalogueMetadata? ReadBuiltInFeatureCatalogue(string spec) =>
        BuiltInFeature.GetOrAdd(spec, static s =>
        {
            try
            {
                using var stream = Specifications.Specification.TryOpenFeatureCatalogue(s);
                if (stream is null) return null;
                var catalogue = FeatureCatalogueReader.Read(stream);
                return new CatalogueMetadata(NullIfEmpty(catalogue.Name), NullIfEmpty(catalogue.VersionNumber), NullIfEmpty(catalogue.VersionDate));
            }
            catch
            {
                return null;
            }
        });

    /// <summary>
    /// The version of the bundled portrayal catalogue for
    /// <paramref name="spec"/>, or <see langword="null"/> when it isn't
    /// bundled or can't be read. Read once per process.
    /// </summary>
    public static CatalogueMetadata? ReadBuiltInPortrayalCatalogue(string spec) =>
        BuiltInPortrayal.GetOrAdd(spec, static s =>
        {
            try
            {
                if (!Specifications.Specification.HasPortrayalCatalogue(s)) return null;
                using var source = Specifications.Specification.CreatePortrayalCatalogueSource(s);
                // SYNC BRIDGE: bundled PC bytes come from an in-process
                // embedded resource read.
                using var stream = source.OpenAsync(PortrayalCatalogueFileName).GetAwaiter().GetResult();
                var catalogue = PortrayalCatalogueReader.Read(stream);
                return new CatalogueMetadata(Name: null, NullIfEmpty(catalogue.Version), VersionDate: null);
            }
            catch
            {
                return null;
            }
        });
}

/// <summary>Which of a product's two catalogues.</summary>
internal enum CatalogueKind
{
    /// <summary>The feature catalogue (S-100 Part 5): one XML file.</summary>
    Feature,

    /// <summary>The portrayal catalogue (S-100 Part 9): a folder or a ZIP package.</summary>
    Portrayal,
}

/// <summary>What a catalogue says about itself.</summary>
/// <param name="Name">The declared name (feature catalogues only); null when absent.</param>
/// <param name="Version">The declared version, e.g. <c>"2.0.0"</c>; null when absent.</param>
/// <param name="VersionDate">The declared version date (feature catalogues only); null when absent.</param>
internal sealed record CatalogueMetadata(string? Name, string? Version, string? VersionDate);

/// <summary>A catalogue <see cref="CatalogueSpecDetection.Detect"/> recognised.</summary>
/// <param name="Kind">Feature or portrayal.</param>
/// <param name="Spec">The product it declares, normalised (e.g. <c>"S-101"</c>).</param>
/// <param name="Path">The full path the user chose; the registry stores this.</param>
/// <param name="Metadata">Its name, version and date.</param>
/// <param name="SourcePath">
/// Where its content is read from: the feature catalogue file, the folder
/// that holds <c>portrayal_catalogue.xml</c>, or the ZIP package.
/// </param>
/// <param name="ZipBasePath">For a ZIP package, the entry folder that holds <c>portrayal_catalogue.xml</c> (<c>""</c> at the root); otherwise null.</param>
internal sealed record DetectedCatalogue(
    CatalogueKind Kind,
    string Spec,
    string Path,
    CatalogueMetadata Metadata,
    string SourcePath,
    string? ZipBasePath)
{
    /// <summary>Opens an asset source over the catalogue's content, which the caller owns.</summary>
    public IAssetSource OpenSource() => Kind switch
    {
        CatalogueKind.Feature => new SingleFileAssetSource(SourcePath, "FeatureCatalogue.xml"),
        _ when ZipBasePath is not null => ZipAssetSource.Create(SourcePath, ZipBasePath),
        _ => FileSystemAssetSource.Create(SourcePath),
    };
}

/// <summary>The outcome of <see cref="CatalogueSpecDetection.Detect"/>: a catalogue, or why the path was refused.</summary>
internal sealed record CatalogueDetectionResult
{
    private CatalogueDetectionResult(DetectedCatalogue? catalogue, string? reason)
    {
        Catalogue = catalogue;
        Reason = reason;
    }

    /// <summary>The catalogue; null when refused.</summary>
    public DetectedCatalogue? Catalogue { get; }

    /// <summary>Why the path was refused, as a sentence for the user; null when found.</summary>
    public string? Reason { get; }

    internal static CatalogueDetectionResult Found(DetectedCatalogue catalogue) => new(catalogue, null);

    internal static CatalogueDetectionResult Refused(string reason) => new(null, reason);
}

/// <summary>
/// An <see cref="IAssetSource"/> that serves one file under a fixed asset
/// name, so a feature catalogue file anywhere on disk can be registered with
/// <see cref="FeatureCatalogueManager.SetSource(SpecRef, IAssetSource)"/>.
/// </summary>
internal sealed class SingleFileAssetSource : IAssetSource
{
    private readonly string _path;
    private readonly string _assetName;

    /// <summary>Serves <paramref name="path"/> as <paramref name="assetName"/>.</summary>
    public SingleFileAssetSource(string path, string assetName)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(assetName);
        _path = path;
        _assetName = assetName;
    }

    /// <inheritdoc />
    public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(relativePath, _assetName, StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException($"'{relativePath}' is not in this catalogue.", relativePath);
        return Task.FromResult<Stream>(File.OpenRead(_path));
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
