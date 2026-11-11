namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>A NetCDF dimension: a name and a length.</summary>
/// <param name="Name">The dimension's name.</param>
/// <param name="Length">The number of points along it; for the record dimension, the number of records.</param>
/// <param name="IsUnlimited">Whether it's the record (unlimited) dimension.</param>
public sealed record NetCdfDimension(string Name, long Length, bool IsUnlimited = false);
