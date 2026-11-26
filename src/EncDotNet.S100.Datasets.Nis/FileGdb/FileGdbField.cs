namespace EncDotNet.S100.Datasets.Nis.FileGdb;

/// <summary>A field of a file geodatabase table.</summary>
internal sealed record FileGdbField(string Name, FileGdbFieldType Type, bool IsNullable, FileGdbGeometryInfo? Geometry);
