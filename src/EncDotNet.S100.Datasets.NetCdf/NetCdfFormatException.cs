namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>
/// Thrown when a file isn't a NetCDF file this library can read: a damaged
/// header, an unsupported format variant (CDF-5, NetCDF-4 groups), or an
/// HDF5 compression filter other than deflate and shuffle. The message says
/// why.
/// </summary>
public class NetCdfFormatException : FormatException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Why the file can't be read.</param>
    public NetCdfFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">Why the file can't be read.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public NetCdfFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
