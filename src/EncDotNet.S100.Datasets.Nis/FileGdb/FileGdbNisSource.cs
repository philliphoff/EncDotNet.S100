using System.Text.RegularExpressions;

namespace EncDotNet.S100.Datasets.Nis.FileGdb;

/// <summary>
/// An <see cref="INisSource"/> over an S-57 Nautical Information System
/// stored in a file geodatabase (a <c>.gdb</c> directory), read with a
/// managed, read-only reader that needs no Esri software or native library.
/// </summary>
/// <remarks>
/// <para>
/// The NIS feature classes are the tables with an <c>FCSubtype</c> field and an
/// <c>LNAM</c> field, such as <c>AidsToNavigationP</c> or <c>DepthsA</c>, plus
/// the <c>PLTS_COLLECTIONS</c> table of S-57 collection objects. Each row's
/// subtype is decoded through the table's item definition: the NIS names
/// subtypes after the S-57 object class, such as <c>BOYSPP_BuoySpecialPurpose</c>
/// or <c>M_COVR_Coverage</c>, and the first six characters are the acronym. A
/// subtype not named that way, such as <c>Representation</c>, gives its whole
/// name as the object class. Relationships come from the <c>PLTS_FREL</c> table.
/// </para>
/// <para>
/// Enterprise and mobile geodatabases, versioning, and writing are out of
/// scope. Geometry must be in WGS 84 geographic coordinates, as NIS data is.
/// </para>
/// </remarks>
public sealed partial class FileGdbNisSource : INisSource
{
    private const string Collections = "PLTS_COLLECTIONS";
    private const string Relationships = "PLTS_FREL";

    private readonly FileGdbDatabase _database;
    private readonly IReadOnlyList<string> _featureTables;

    private FileGdbNisSource(string path, FileGdbDatabase database, IReadOnlyList<string> featureTables)
    {
        Path = path;
        _database = database;
        _featureTables = featureTables;
    }

    /// <summary>The path of the <c>.gdb</c> directory.</summary>
    public string Path { get; }

    /// <summary>The names of the tables read as NIS features, in catalogue order.</summary>
    public IReadOnlyList<string> FeatureTables => _featureTables;

    /// <summary>Opens a file geodatabase holding an S-57 NIS.</summary>
    /// <param name="path">The path of the <c>.gdb</c> directory.</param>
    /// <returns>The source.</returns>
    /// <exception cref="InvalidDataException">
    /// The directory isn't a file geodatabase, has no NIS feature classes, or
    /// stores geometry in a coordinate system other than WGS 84.
    /// </exception>
    public static FileGdbNisSource Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var database = FileGdbDatabase.Open(path);
        var tables = new List<string>();
        foreach (var name in database.TableNames)
        {
            if (name.StartsWith("GDB_", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.StartsWith("PLTS_", StringComparison.OrdinalIgnoreCase) && !name.Equals(Collections, StringComparison.OrdinalIgnoreCase)) continue;
            using var table = database.TryOpenTable(name);
            if (table is null || table.IndexOf("FCSubtype") < 0 || table.IndexOf("LNAM") < 0) continue;
            if (table.GeometryField?.Geometry is { } geometry && !IsWgs84(geometry.Wkt))
                throw new InvalidDataException($"{name} is not in WGS 84 geographic coordinates; NIS data must be.");
            tables.Add(name);
        }
        if (tables.Count == 0)
            throw new InvalidDataException($"{path} has no NIS feature classes (tables with FCSubtype and LNAM fields).");
        return new FileGdbNisSource(path, database, tables);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetDatasetNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _featureTables)
        {
            using var table = _database.TryOpenTable(name);
            int dsnm = table?.IndexOf("DSNM") ?? -1;
            if (table is null || dsnm < 0) continue;
            foreach (var (_, values) in table.ReadRows())
            {
                if (values[dsnm] is string s && s.Length > 0) names.Add(s);
            }
        }
        return names.Order(StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc />
    public IEnumerable<NisFeature> GetFeatures(NisQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        foreach (var name in _featureTables)
        {
            using var table = _database.TryOpenTable(name);
            if (table is null) continue;
            var subtypes = _database.Subtypes(name);
            int subtype = table.IndexOf("FCSubtype");
            int lnam = table.IndexOf("LNAM");
            int dsnm = table.IndexOf("DSNM");
            int scale = table.IndexOf("PLTS_COMP_SCALE");
            int deleted = table.IndexOf("IS_DELETE");
            int shape = table.GeometryField is { } g ? table.IndexOf(g.Name) : -1;

            foreach (var (objectId, values) in table.ReadRows())
            {
                var attributes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < values.Length; i++)
                {
                    if (i == shape || values[i] is null) continue;
                    var field = table.Fields[i];
                    if (field.Type is FileGdbFieldType.ObjectId) continue;
                    attributes[field.Name] = values[i]!;
                }

                var feature = new NisFeature
                {
                    FeatureClass = name,
                    ObjectClass = ObjectClass(values[subtype], subtypes),
                    ObjectId = objectId,
                    LongName = lnam >= 0 ? values[lnam] as string : null,
                    DatasetName = dsnm >= 0 ? values[dsnm] as string : null,
                    CompilationScale = scale >= 0 && values[scale] is int s ? s : null,
                    IsDeleted = deleted >= 0 && values[deleted] is int d && d != 0,
                    Geometry = shape >= 0 ? values[shape] as NisGeometry : null,
                    Attributes = attributes,
                };
                if (query.Matches(feature)) yield return feature;
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<NisRelationship> GetRelationships()
    {
        using var table = _database.TryOpenTable(Relationships);
        if (table is null) yield break;
        int source = table.IndexOf("SRC_LNAM"), destination = table.IndexOf("DEST_LNAM"), rind = table.IndexOf("RIND");
        if (source < 0 || destination < 0 || rind < 0) yield break;
        foreach (var (_, values) in table.ReadRows())
        {
            if (values[source] is not string s || values[destination] is not string d) continue;
            if (values[rind] is not int kind || kind is < 1 or > 3) continue;
            yield return new NisRelationship(s, d, (NisRelationshipKind)kind);
        }
    }

    // The S-57 acronym of a subtype named after it, else the subtype's name,
    // else its code.
    private static string ObjectClass(object? code, IReadOnlyDictionary<int, string> subtypes)
    {
        if (code is not int value) return string.Empty;
        if (!subtypes.TryGetValue(value, out var name)) return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return AcronymPrefix().IsMatch(name) ? name[..6] : name;
    }

    private static bool IsWgs84(string wkt)
        => wkt.Length == 0
            || (wkt.StartsWith("GEOGCS", StringComparison.OrdinalIgnoreCase)
                && wkt.Contains("WGS_1984", StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex("^[A-Z0-9$_]{6}(_|$)")]
    private static partial Regex AcronymPrefix();
}
