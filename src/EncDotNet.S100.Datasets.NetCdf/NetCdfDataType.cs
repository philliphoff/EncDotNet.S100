namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>The type of a NetCDF variable's or attribute's values.</summary>
public enum NetCdfDataType
{
    /// <summary>8-bit signed integer (<c>NC_BYTE</c>).</summary>
    Byte,

    /// <summary>8-bit character, read as text (<c>NC_CHAR</c>).</summary>
    Char,

    /// <summary>16-bit signed integer (<c>NC_SHORT</c>).</summary>
    Short,

    /// <summary>32-bit signed integer (<c>NC_INT</c>).</summary>
    Int,

    /// <summary>32-bit IEEE float (<c>NC_FLOAT</c>).</summary>
    Float,

    /// <summary>64-bit IEEE float (<c>NC_DOUBLE</c>).</summary>
    Double,

    /// <summary>8-bit unsigned integer (NetCDF-4 <c>NC_UBYTE</c>).</summary>
    UByte,

    /// <summary>16-bit unsigned integer (NetCDF-4 <c>NC_USHORT</c>).</summary>
    UShort,

    /// <summary>32-bit unsigned integer (NetCDF-4 <c>NC_UINT</c>).</summary>
    UInt,

    /// <summary>64-bit signed integer (NetCDF-4 <c>NC_INT64</c>).</summary>
    Int64,

    /// <summary>64-bit unsigned integer (NetCDF-4 <c>NC_UINT64</c>).</summary>
    UInt64,

    /// <summary>A string (NetCDF-4 <c>NC_STRING</c>).</summary>
    String,

    /// <summary>A type this reader doesn't read (compound, enum, opaque, variable-length).</summary>
    Other,
}
