namespace EncDotNet.S100.Datasets.Nis.FileGdb;

/// <summary>The field types of a file geodatabase table, by their stored code.</summary>
internal enum FileGdbFieldType
{
    Int16 = 0,
    Int32 = 1,
    Float32 = 2,
    Float64 = 3,
    String = 4,
    DateTime = 5,
    ObjectId = 6,
    Geometry = 7,
    Binary = 8,
    Raster = 9,
    Guid = 10,
    GlobalId = 11,
    Xml = 12,
    Int64 = 13,
    DateOnly = 14,
    TimeOnly = 15,
    DateTimeWithOffset = 16,
}
