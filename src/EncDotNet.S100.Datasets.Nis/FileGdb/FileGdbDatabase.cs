using System.Globalization;
using System.Xml.Linq;

namespace EncDotNet.S100.Datasets.Nis.FileGdb;

/// <summary>
/// A file geodatabase directory: its system catalogue, which names every
/// table and the <c>a&lt;n&gt;.gdbtable</c> file that holds it, and the item
/// definitions (<c>GDB_Items</c>) that describe each table's subtypes.
/// </summary>
internal sealed class FileGdbDatabase
{
    private readonly string _directory;
    private readonly Dictionary<string, int> _tables;
    private Dictionary<string, string>? _definitions;

    private FileGdbDatabase(string directory, Dictionary<string, int> tables)
    {
        _directory = directory;
        _tables = tables;
    }

    /// <summary>The names of the tables that have a table file.</summary>
    public IReadOnlyCollection<string> TableNames => _tables.Keys;

    /// <summary>Opens a file geodatabase directory.</summary>
    /// <exception cref="InvalidDataException">The directory isn't a file geodatabase.</exception>
    public static FileGdbDatabase Open(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var catalogue = Path.Combine(directory, "a00000001.gdbtable");
        if (!File.Exists(catalogue))
            throw new InvalidDataException($"{directory} is not a file geodatabase: it has no a00000001.gdbtable.");

        var tables = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var table = FileGdbTable.Open("GDB_SystemCatalog", catalogue))
        {
            int name = table.IndexOf("Name");
            foreach (var (id, values) in table.ReadRows())
            {
                if (values[name] is not string tableName || tableName.Length == 0) continue;
                if (File.Exists(TablePath(directory, (int)id))) tables[tableName] = (int)id;
            }
        }
        return new FileGdbDatabase(directory, tables);
    }

    /// <summary>Opens a table by name, or returns <see langword="null"/> when there is none.</summary>
    public FileGdbTable? TryOpenTable(string name)
        => _tables.TryGetValue(name, out var id) ? FileGdbTable.Open(name, TablePath(_directory, id)) : null;

    /// <summary>
    /// The subtypes a table defines, from its item definition, by code; empty
    /// when it defines none.
    /// </summary>
    public IReadOnlyDictionary<int, string> Subtypes(string table)
    {
        var result = new Dictionary<int, string>();
        if (!Definitions().TryGetValue(table, out var xml)) return result;
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return result;
        }
        foreach (var subtype in document.Descendants().Where(e => e.Name.LocalName == "Subtype"))
        {
            var name = subtype.Elements().FirstOrDefault(e => e.Name.LocalName == "SubtypeName")?.Value;
            var code = subtype.Elements().FirstOrDefault(e => e.Name.LocalName == "SubtypeCode")?.Value;
            if (name is not null && int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                result.TryAdd(value, name);
        }
        return result;
    }

    // Item names are bare table names; GDB_Items is read once.
    private Dictionary<string, string> Definitions()
    {
        if (_definitions is not null) return _definitions;
        var definitions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var items = TryOpenTable("GDB_Items"))
        {
            if (items is not null)
            {
                int name = items.IndexOf("Name"), definition = items.IndexOf("Definition");
                if (name >= 0 && definition >= 0)
                {
                    foreach (var (_, values) in items.ReadRows())
                    {
                        if (values[name] is string n && values[definition] is string d && n.Length > 0)
                            definitions.TryAdd(n, d);
                    }
                }
            }
        }
        return _definitions = definitions;
    }

    private static string TablePath(string directory, int id)
        => Path.Combine(directory, $"a{id.ToString("x8", CultureInfo.InvariantCulture)}.gdbtable");
}
