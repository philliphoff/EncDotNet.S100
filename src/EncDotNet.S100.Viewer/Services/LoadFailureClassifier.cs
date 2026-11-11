using System.Reflection;
using System.Text;
using EncDotNet.S100.Hdf5;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Walks an exception's <see cref="Exception.InnerException"/> chain
/// (including <see cref="TargetInvocationException"/> wrapping) to find
/// the first structured S-100 dataset exception
/// (<see cref="S100DatasetSchemaException"/> or
/// <see cref="S100DatasetNotSupportedException"/>) so the load-failure
/// dialog can pick a human-friendly primary message. The original
/// (outermost) exception is still preserved by the caller for the
/// details / stack-trace payload.
/// </summary>
internal static class LoadFailureClassifier
{
    /// <summary>
    /// Walks the inner-exception chain of <paramref name="exception"/>
    /// and returns the innermost <see cref="S100DatasetSchemaException"/>
    /// or <see cref="S100DatasetNotSupportedException"/>; falls back to
    /// <paramref name="exception"/> when no structured S-100 exception is
    /// present.
    /// </summary>
    public static Exception Unwrap(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Exception? structured = null;
        var current = exception;
        while (current is not null)
        {
            if (current is S100DatasetSchemaException or S100DatasetNotSupportedException)
            {
                structured = current;
            }

            current = current.InnerException;
        }

        return structured ?? exception;
    }

    /// <summary>
    /// Returns a one-line English reason for a dataset-load failure, shaped
    /// from the innermost structured S-100 exception like the load-failure
    /// toast, for use in log entries (which stay in English regardless of
    /// the UI culture).
    /// </summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return Unwrap(exception) switch
        {
            S100DatasetNotSupportedException ns =>
                $"{ns.Product} dataset uses {ns.Feature}, which is not yet supported.",
            S100DatasetSchemaException sx =>
                $"This {sx.Product} file is missing or has an invalid '{sx.AttributeOrDataset}' at '{sx.GroupPath}'.",
            _ when FindDamagedFileException(exception) is { } damaged =>
                $"The file is damaged or is not in the format its name suggests ({Printable(damaged.Message)}).",
            var other => Printable(other.Message),
        };
    }

    /// <summary>
    /// Returns the exception a reader threw because the file's bytes are not
    /// what the format requires (<see cref="InvalidDataException"/>, or
    /// <see cref="EndOfStreamException"/> for a truncated file), when the
    /// chain has no structured S-100 exception to report instead; otherwise
    /// <see langword="null"/>. A random or cut-off file named <c>.000</c>
    /// fails this way in the ISO 8211 reader.
    /// </summary>
    public static Exception? FindDamagedFileException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (Unwrap(exception) is S100DatasetSchemaException or S100DatasetNotSupportedException)
            return null;

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidDataException or EndOfStreamException)
                return current;
        }
        return null;
    }

    /// <summary>
    /// Spells out control characters in <paramref name="message"/> as
    /// <c>U+000B</c>, so a reader message that quotes a byte from a damaged
    /// file can't break a toast or log line.
    /// </summary>
    public static string Printable(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!message.Any(char.IsControl))
            return message;

        var sb = new StringBuilder(message.Length + 8);
        foreach (var c in message)
        {
            if (char.IsControl(c))
                sb.Append("U+").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}
