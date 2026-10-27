namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// Thrown when a file isn't valid GRIB2: a message is truncated, a section
/// length doesn't fit, or a required section is missing. The message names
/// what's wrong and, where it helps, the byte offset.
/// </summary>
/// <remarks>
/// A file that is valid but uses something this decoder doesn't support, such
/// as GRIB edition 1 or CCSDS packing, throws
/// <see cref="UnsupportedGribException"/> instead.
/// </remarks>
public class GribFormatException : FormatException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What's wrong with the file.</param>
    public GribFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">What's wrong with the file.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public GribFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
