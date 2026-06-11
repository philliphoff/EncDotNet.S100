using System.Collections.Immutable;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S57.ExchangeSets;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// One base cell discovered in an S-57 / S-63 exchange-set catalogue
/// (<c>CATALOG.031</c>), together with its in-set sequential update files and
/// (optionally) its geographic extent.
/// </summary>
/// <remarks>
/// A cell is identified by an 8-character cell name (S-57 Appendix B.1 §B.1
/// "Dataset names"). The base cell carries the <c>.000</c> file extension; its
/// updates carry <c>.001</c>, <c>.002</c>, … in application order (S-57 Part 3,
/// dataset updating). Both <see cref="RelativePath"/> and
/// <see cref="UpdateRelativePaths"/> are normalised to the current platform's
/// directory separator so they can be opened directly from a
/// <c>FileSystemAssetSource</c> rooted at the exchange-set directory.
/// </remarks>
public sealed class S57ExchangeSetCell
{
    /// <summary>The 8-character cell name (e.g. <c>US5MA1BO</c>).</summary>
    public required string CellName { get; init; }

    /// <summary>
    /// Source-relative path of the base cell (<c>….000</c>), normalised to the
    /// current platform's directory separator.
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// Source-relative paths of the in-set sequential update files
    /// (<c>….001</c>, <c>….002</c>, …) in ascending update-number order,
    /// normalised to the current platform's directory separator. Empty when the
    /// cell has no updates in this exchange set.
    /// </summary>
    public required ImmutableArray<string> UpdateRelativePaths { get; init; }

    /// <summary>
    /// The cell's geographic extent (EPSG:4326) from the catalogue's
    /// <c>CATD</c> bounding-box fields, or <see langword="null"/> when the
    /// catalogue did not declare one for the base cell.
    /// </summary>
    public BoundingBox? BoundingBox { get; init; }
}

/// <summary>
/// Enumerates the renderable base cells of an S-57 / S-63 exchange set by
/// reading its <c>CATALOG.031</c> via the upstream <c>EncDotNet.S57</c>
/// catalogue reader and grouping the catalogued files into base-cell + update
/// sets.
/// </summary>
/// <remarks>
/// <para>
/// This is the loading-side companion to <see cref="S57ExchangeSetVerification"/>
/// (which is verification-only): both are deliberately thin adapters over the
/// directory-rooted S-57 model rather than a shared interface. The viewer pairs
/// this enumeration with a <c>FileSystemAssetSource</c> rooted at the
/// exchange-set directory so each cell flows through the same
/// <see cref="S57DatasetProcessor"/> code path as a single dropped <c>.000</c>
/// file.
/// </para>
/// <para>
/// The catalogue's <c>CATD</c> records reference files by an exchange-set
/// relative path that conventionally uses backslashes (S-57 Appendix B.1); these
/// are normalised to the running platform's separator here. Non-cell entries
/// (text files, the catalogue itself, certificates, README files) are ignored.
/// </para>
/// </remarks>
public static class S57ExchangeSetCatalog
{
    /// <summary>The S-57 / S-63 exchange-set catalogue filename, matched case-insensitively.</summary>
    public const string CatalogueFileName = "CATALOG.031";

    /// <summary>
    /// Reads the <c>CATALOG.031</c> at <paramref name="rootPath"/> (a directory
    /// containing it, or the catalogue file itself) and returns its base cells.
    /// </summary>
    /// <param name="rootPath">
    /// The exchange-set root directory (the folder that contains
    /// <c>CATALOG.031</c>), or the path to the <c>CATALOG.031</c> file.
    /// </param>
    /// <returns>The base cells discovered in the catalogue.</returns>
    /// <exception cref="ArgumentException"><paramref name="rootPath"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">No <c>CATALOG.031</c> was found.</exception>
    public static IReadOnlyList<S57ExchangeSetCell> ReadBaseCells(string rootPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);

        var cataloguePath = ResolveCataloguePath(rootPath);
        var catalogue = S57CatalogReader.ReadFromFile(cataloguePath, logger: null);
        return SelectBaseCells(catalogue);
    }

    /// <summary>
    /// Resolves the absolute path to the <c>CATALOG.031</c> file given either
    /// the exchange-set root directory or the catalogue file path itself.
    /// </summary>
    /// <param name="rootPath">A directory containing <c>CATALOG.031</c>, or the file itself.</param>
    /// <returns>The absolute path to the catalogue file.</returns>
    /// <exception cref="FileNotFoundException">No <c>CATALOG.031</c> was found.</exception>
    public static string ResolveCataloguePath(string rootPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);

        if (File.Exists(rootPath) &&
            string.Equals(Path.GetFileName(rootPath), CatalogueFileName, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(rootPath);
        }

        if (Directory.Exists(rootPath))
        {
            var match = Directory
                .EnumerateFiles(rootPath, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => string.Equals(
                    Path.GetFileName(f), CatalogueFileName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return Path.GetFullPath(match);
        }

        throw new FileNotFoundException(
            $"No {CatalogueFileName} found at: {rootPath}", rootPath);
    }

    /// <summary>
    /// Groups the catalogue's <c>CATD</c> entries into base cells and their
    /// ordered updates. Exposed (rather than private) so the grouping/normalising
    /// logic can be unit-tested directly against an in-memory
    /// <see cref="S57Catalog"/> without a real <c>CATALOG.031</c> on disk.
    /// </summary>
    /// <param name="catalogue">The parsed catalogue.</param>
    /// <returns>
    /// The base cells, in ascending cell-name order, each with its updates in
    /// ascending update-number order.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="catalogue"/> is null.</exception>
    public static IReadOnlyList<S57ExchangeSetCell> SelectBaseCells(S57Catalog catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        // Group every ENC dataset file (….000/.001/…) by its cell name (the
        // filename without extension), so a base cell and its sequential updates
        // collapse into one group. Non-numeric extensions (.TXT, certificates,
        // README files, the catalogue itself) are skipped.
        var groups = new Dictionary<string, List<(int Update, S57CatalogEntry Entry)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in catalogue.Entries)
        {
            var fileName = entry.FileName;
            if (string.IsNullOrEmpty(fileName))
                continue;

            var leaf = LeafName(fileName);
            var dot = leaf.LastIndexOf('.');
            if (dot < 0 || dot == leaf.Length - 1)
                continue;

            var extension = leaf[(dot + 1)..];
            if (extension.Length != 3 ||
                !int.TryParse(extension, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var updateNumber))
            {
                continue;
            }

            var cellName = leaf[..dot];
            if (!groups.TryGetValue(cellName, out var list))
            {
                list = new List<(int, S57CatalogEntry)>();
                groups[cellName] = list;
            }
            list.Add((updateNumber, entry));
        }

        var cells = new List<S57ExchangeSetCell>(groups.Count);
        foreach (var (cellName, entries) in groups)
        {
            entries.Sort((a, b) => a.Update.CompareTo(b.Update));

            // A group with no .000 base cell cannot be rendered on its own
            // (an orphaned update); skip it rather than fail the whole set.
            var baseEntry = entries.FirstOrDefault(e => e.Update == 0);
            if (baseEntry.Entry is null)
                continue;

            var updates = entries
                .Where(e => e.Update > 0)
                .Select(e => Normalise(e.Entry.FileName))
                .ToImmutableArray();

            cells.Add(new S57ExchangeSetCell
            {
                CellName = cellName,
                RelativePath = Normalise(baseEntry.Entry.FileName),
                UpdateRelativePaths = updates,
                BoundingBox = ToBoundingBox(baseEntry.Entry),
            });
        }

        cells.Sort((a, b) => string.CompareOrdinal(a.CellName, b.CellName));
        return cells;
    }

    /// <summary>
    /// Returns the EPSG:4326 union of every cell's
    /// <see cref="S57ExchangeSetCell.BoundingBox"/>, ignoring cells that lack
    /// one. Returns <see langword="null"/> when no cell declared an extent.
    /// </summary>
    /// <param name="cells">The cells to union.</param>
    /// <exception cref="ArgumentNullException"><paramref name="cells"/> is null.</exception>
    public static BoundingBox? UnionBoundingBox(IEnumerable<S57ExchangeSetCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        double? west = null, east = null, south = null, north = null;
        foreach (var cell in cells)
        {
            if (cell.BoundingBox is not { } b)
                continue;
            west = west is null ? b.WestBoundLongitude : Math.Min(west.Value, b.WestBoundLongitude);
            east = east is null ? b.EastBoundLongitude : Math.Max(east.Value, b.EastBoundLongitude);
            south = south is null ? b.SouthBoundLatitude : Math.Min(south.Value, b.SouthBoundLatitude);
            north = north is null ? b.NorthBoundLatitude : Math.Max(north.Value, b.NorthBoundLatitude);
        }

        if (west is null)
            return null;

        return new BoundingBox
        {
            WestBoundLongitude = west.Value,
            EastBoundLongitude = east!.Value,
            SouthBoundLatitude = south!.Value,
            NorthBoundLatitude = north!.Value,
        };
    }

    private static BoundingBox? ToBoundingBox(S57CatalogEntry entry)
    {
        if (entry.WesternmostLongitude is not { } west ||
            entry.EasternmostLongitude is not { } east ||
            entry.SouthernmostLatitude is not { } south ||
            entry.NorthernmostLatitude is not { } north)
        {
            return null;
        }

        return new BoundingBox
        {
            WestBoundLongitude = west,
            EastBoundLongitude = east,
            SouthBoundLatitude = south,
            NorthBoundLatitude = north,
        };
    }

    /// <summary>
    /// Normalises a catalogue-relative path (which conventionally uses
    /// backslashes per S-57 Appendix B.1) to the running platform's directory
    /// separator so it can be opened from a <c>FileSystemAssetSource</c>.
    /// </summary>
    private static string Normalise(string path) =>
        path.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

    private static string LeafName(string path)
    {
        var normalized = path.Replace('\\', '/');
        var idx = normalized.LastIndexOf('/');
        return idx < 0 ? normalized : normalized[(idx + 1)..];
    }
}
