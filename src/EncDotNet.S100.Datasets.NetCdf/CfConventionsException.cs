namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>
/// Thrown when a NetCDF file can be read but doesn't follow the CF conventions
/// this library needs: no latitude, longitude or time coordinate it can
/// recognise, time units or a calendar it can't decode, or no variable with a
/// standard name in the weather parameter table. The message lists what the
/// file holds.
/// </summary>
public class CfConventionsException : FormatException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What's missing or unsupported.</param>
    public CfConventionsException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">What's missing or unsupported.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public CfConventionsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
