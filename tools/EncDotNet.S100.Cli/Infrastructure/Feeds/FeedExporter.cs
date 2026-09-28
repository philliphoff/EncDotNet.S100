using System.Text.Json;
using System.Text.RegularExpressions;
using EncDotNet.S100.Collections.Feeds;

namespace EncDotNet.S100.Cli.Infrastructure.Feeds;

/// <summary>What <see cref="FeedExporter.ExportAsync"/> did.</summary>
/// <param name="Items">Datasets in the exported feed.</param>
/// <param name="Written">Item zips written (new or changed).</param>
/// <param name="Unchanged">Item zips kept because their datasets had not changed.</param>
/// <param name="Removed">Item zips removed because their datasets are gone.</param>
/// <param name="Failed">Item zips that could not be written (their datasets are left out of the feed).</param>
/// <param name="TotalBytes">The size of all item zips now in the export.</param>
internal sealed record FeedExportResult(int Items, int Written, int Unchanged, int Removed, int Failed, long TotalBytes);

/// <summary>
/// Writes an S-100 feed as static files (issue #680): <c>feed.json</c> plus
/// <c>items/&lt;id&gt;.zip</c>, ready to upload to any web host. Re-exporting
/// into the same folder rewrites only the zips whose datasets changed
/// (tracked in <c>.s100-feed-export.json</c>) and removes those whose datasets
/// are gone; <c>feed.json</c> is written last, so it never names a zip that
/// is not there yet.
/// </summary>
internal static partial class FeedExporter
{
    /// <summary>The export's bookkeeping file (item stamps), next to <c>feed.json</c>.</summary>
    public const string ManifestFileName = ".s100-feed-export.json";

    private const string ItemsFolder = "items";

    public static async Task<FeedExportResult> ExportAsync(
        PublishedFeed feed,
        string outputDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);

        var items = Path.Combine(outputDirectory, ItemsFolder);
        Directory.CreateDirectory(items);
        var manifestPath = Path.Combine(outputDirectory, ManifestFileName);
        var previous = ReadManifest(manifestPath);
        var stamps = new Dictionary<string, string>(StringComparer.Ordinal);
        var failed = new HashSet<string>(StringComparer.Ordinal);
        int written = 0, unchanged = 0;

        foreach (var (id, (item, location)) in feed.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!S100FeedPackager.Exists(location))
                continue;

            var zip = Path.Combine(items, id + ".zip");
            var stamp = S100FeedPackager.Stamp(location);
            if (previous.TryGetValue(id, out var old) && old == stamp && File.Exists(zip))
            {
                stamps[id] = stamp;
                unchanged++;
                continue;
            }

            var temp = zip + ".tmp";
            try
            {
                await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    await S100FeedPackager.WriteZipAsync(output, location, cancellationToken).ConfigureAwait(false);
                File.Move(temp, zip, overwrite: true);
                stamps[id] = stamp;
                written++;
                progress?.Report(item.Name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                TryDelete(temp);
                failed.Add(id);
                progress?.Report($"{item.Name}: {ex.Message}");
            }
        }

        // Remove zips (only ones this exporter names) whose datasets are gone.
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(items))
        {
            var name = Path.GetFileName(file);
            var match = ItemFileName().Match(name);
            if ((match.Success && !stamps.ContainsKey(match.Groups["id"].Value)) || name.EndsWith(".zip.tmp", StringComparison.Ordinal))
            {
                TryDelete(file);
                removed += match.Success ? 1 : 0;
            }
        }

        // The feed lists only what was exported, and is written last.
        var document = feed.Document with
        {
            Items = feed.Document.Items
                .Where(i => i.Location is Collections.RemoteItemLocation { Package: { } id } && stamps.ContainsKey(id))
                .ToArray(),
        };
        WriteAtomically(Path.Combine(outputDirectory, S100Feed.FileName), stream => S100Feed.Write(stream, document));
        WriteAtomically(manifestPath, stream => JsonSerializer.Serialize(stream, stamps));

        var total = stamps.Keys.Sum(id => new FileInfo(Path.Combine(items, id + ".zip")).Length);
        return new FeedExportResult(document.Items.Count, written, unchanged, removed, failed.Count, total);
    }

    /// <summary>True when <paramref name="output"/> is <paramref name="published"/> or inside it (it would be indexed on the next export).</summary>
    public static bool IsInside(string output, string published)
    {
        var o = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var p = Path.GetFullPath(published).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return o.StartsWith(p, comparison);
    }

    private static Dictionary<string, string> ReadManifest(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return [];
        }
    }

    private static void WriteAtomically(string path, Action<Stream> write)
    {
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
            write(stream);
        File.Move(temp, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex("^(?<id>[0-9a-f]{20})\\.zip$")]
    private static partial Regex ItemFileName();
}
