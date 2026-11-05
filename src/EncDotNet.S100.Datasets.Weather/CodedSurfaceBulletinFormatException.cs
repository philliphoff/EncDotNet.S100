namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// A file or text isn't a coded surface bulletin the reader understands, or
/// it is one but something in it can't be decoded. The message says what and
/// where.
/// </summary>
public sealed class CodedSurfaceBulletinFormatException : FormatException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What couldn't be read, and where.</param>
    public CodedSurfaceBulletinFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception around the one that caused it.</summary>
    /// <param name="message">What couldn't be read, and where.</param>
    /// <param name="innerException">The cause.</param>
    public CodedSurfaceBulletinFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
