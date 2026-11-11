namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// Thrown when a weather grid can't be represented in the model: for example a
/// regional grid that crosses the antimeridian, or one whose geometry is
/// invalid. The message says why, in words a user can act on.
/// </summary>
public class UnsupportedWeatherGridException : NotSupportedException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Why the grid isn't supported.</param>
    public UnsupportedWeatherGridException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">Why the grid isn't supported.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public UnsupportedWeatherGridException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
