using System;
using System.Reflection;
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
}
