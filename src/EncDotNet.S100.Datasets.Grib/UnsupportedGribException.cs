namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// Thrown when a GRIB file is valid but uses an edition or template this
/// decoder doesn't support, for example GRIB edition 1, a Lambert conformal
/// grid (template 3.30), or JPEG 2000 (template 5.40) or CCSDS (template 5.42)
/// packing. The message names the edition or template.
/// </summary>
public class UnsupportedGribException : NotSupportedException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What isn't supported, naming the edition or template.</param>
    public UnsupportedGribException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">What isn't supported, naming the edition or template.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public UnsupportedGribException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
