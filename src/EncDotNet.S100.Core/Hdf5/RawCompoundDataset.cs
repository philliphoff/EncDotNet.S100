namespace EncDotNet.S100.Hdf5;

/// <summary>
/// Classifies a compound member's on-disk numeric kind so callers can
/// project it into a target .NET type without recompiling against
/// PureHDF types.
/// </summary>
public enum CompoundMemberKind
{
    /// <summary>The member is not a primitive numeric type recognised here (e.g. string, opaque, compound).</summary>
    Other = 0,

    /// <summary><c>H5T_IEEE_F32LE</c> / <c>H5T_IEEE_F32BE</c>.</summary>
    Float32,

    /// <summary><c>H5T_IEEE_F64LE</c> / <c>H5T_IEEE_F64BE</c>.</summary>
    Float64,

    /// <summary>Signed 8-bit fixed-point integer.</summary>
    Int8,

    /// <summary>Unsigned 8-bit fixed-point integer.</summary>
    UInt8,

    /// <summary>Signed 16-bit fixed-point integer.</summary>
    Int16,

    /// <summary>Unsigned 16-bit fixed-point integer.</summary>
    UInt16,

    /// <summary>Signed 32-bit fixed-point integer.</summary>
    Int32,

    /// <summary>Unsigned 32-bit fixed-point integer.</summary>
    UInt32,

    /// <summary>Signed 64-bit fixed-point integer.</summary>
    Int64,

    /// <summary>Unsigned 64-bit fixed-point integer.</summary>
    UInt64,
}

/// <summary>
/// Describes a single member of a compound HDF5 datatype as observed
/// on disk. Used to project <see cref="RawCompoundDataset"/> rows
/// into product-specific value types when member naming or numeric
/// width varies between producers.
/// </summary>
public sealed record CompoundMemberInfo(string Name, int Offset, int Size, CompoundMemberKind Kind);

/// <summary>
/// A compound dataset returned in raw form: the bytes are laid out
/// exactly as on disk (record-major), and the members describe where
/// each field lives within a record. Callers project rows into their
/// own value types so that small spec-conforming variations (member
/// renames, numeric width changes) can be tolerated without changing
/// public value-struct layouts.
/// </summary>
public sealed class RawCompoundDataset
{
    /// <summary>Initializes a new instance.</summary>
    public RawCompoundDataset(byte[] data, int recordSize, IReadOnlyList<CompoundMemberInfo> members)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(members);
        if (recordSize <= 0) throw new ArgumentOutOfRangeException(nameof(recordSize));
        if (data.Length % recordSize != 0)
            throw new ArgumentException(
                $"Raw compound data length {data.Length} is not a multiple of record size {recordSize}.",
                nameof(data));

        Data = data;
        RecordSize = recordSize;
        Members = members;
        RecordCount = data.Length / recordSize;
    }

    /// <summary>Raw record-major payload.</summary>
    public byte[] Data { get; }

    /// <summary>Size of one record in bytes.</summary>
    public int RecordSize { get; }

    /// <summary>Number of records in <see cref="Data"/>.</summary>
    public int RecordCount { get; }

    /// <summary>Compound member descriptors, in the order declared on disk.</summary>
    public IReadOnlyList<CompoundMemberInfo> Members { get; }

    /// <summary>Looks up a member by name (case-insensitive), trying each candidate in turn.</summary>
    public CompoundMemberInfo? FindMember(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            foreach (var member in Members)
            {
                if (string.Equals(member.Name, candidate, StringComparison.OrdinalIgnoreCase))
                    return member;
            }
        }

        return null;
    }
}
