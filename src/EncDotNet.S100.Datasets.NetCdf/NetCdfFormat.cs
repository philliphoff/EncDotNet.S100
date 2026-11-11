namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>The on-disk format of a NetCDF file.</summary>
public enum NetCdfFormat
{
    /// <summary>NetCDF-3 classic (<c>CDF\x01</c>): 32-bit offsets.</summary>
    Classic,

    /// <summary>NetCDF-3 64-bit offset (<c>CDF\x02</c>): the classic model with 64-bit variable offsets.</summary>
    Offset64,

    /// <summary>NetCDF-4: an HDF5 file following the NetCDF-4 conventions (dimension scales).</summary>
    NetCdf4,
}
