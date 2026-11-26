namespace EncDotNet.S100.Datasets.Nis;

/// <summary>What <see cref="NisToS57Translator"/> read, wrote, changed and couldn't convert.</summary>
public sealed class NisToS57Diagnostics
{
    private readonly Dictionary<string, int> _droppedObjectClasses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _unknownAttributes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _unknownAgencies = new(StringComparer.Ordinal);

    /// <summary>The features read from the source for the cell.</summary>
    public int FeaturesRead { get; internal set; }

    /// <summary>The S-57 feature records written.</summary>
    public int FeaturesWritten { get; internal set; }

    /// <summary>The soundings written, grouped into SOUNDG objects.</summary>
    public int SoundingsWritten { get; internal set; }

    /// <summary>The SOUNDG objects the soundings were grouped into.</summary>
    public int SoundingObjects { get; internal set; }

    /// <summary>The isolated nodes, connected nodes and edges written.</summary>
    public int VectorRecordsWritten { get; internal set; }

    /// <summary>Attribute values written as an empty (unknown) value because the NIS stored <c>-32767</c>.</summary>
    public int UnknownValues { get; internal set; }

    /// <summary>Feature pointers (FFPT) written from NIS relationships.</summary>
    public int RelationshipsWritten { get; internal set; }

    /// <summary>NIS relationships not written because one of their features isn't in the cell.</summary>
    public int RelationshipsUnresolved { get; internal set; }

    /// <summary>
    /// Features given a new identity because their long name was missing,
    /// unreadable or repeated. SOUNDG groups usually count here: the NIS stores
    /// soundings without long names.
    /// </summary>
    public int IdentitiesAssigned { get; internal set; }

    /// <summary>Line or area features dropped because their geometry was empty or degenerate.</summary>
    public int EmptyGeometries { get; internal set; }

    /// <summary>Whether an M_COVR was added because the cell had none.</summary>
    public bool CoverageSynthesised { get; internal set; }

    /// <summary>Text the S-57 lexical levels couldn't encode, dropped.</summary>
    public int UnencodableText { get; internal set; }

    /// <summary>Features dropped because their object class isn't an S-57 one, by class.</summary>
    public IReadOnlyDictionary<string, int> DroppedObjectClasses => _droppedObjectClasses;

    /// <summary>Fields with a value that look like S-57 attributes but aren't in the IHO catalogue, by field.</summary>
    public IReadOnlyDictionary<string, int> UnknownAttributes => _unknownAttributes;

    /// <summary>Long-name agency tokens with no known numeric code, by token; those features took the producing agency.</summary>
    public IReadOnlyDictionary<string, int> UnknownAgencies => _unknownAgencies;

    internal void RecordDroppedObjectClass(string objectClass) => Increment(_droppedObjectClasses, objectClass);

    internal void RecordUnknownAttribute(string field) => Increment(_unknownAttributes, field);

    internal void RecordUnknownAgency(string token) => Increment(_unknownAgencies, token);

    private static void Increment(Dictionary<string, int> counts, string key)
        => counts[key] = counts.GetValueOrDefault(key) + 1;
}
