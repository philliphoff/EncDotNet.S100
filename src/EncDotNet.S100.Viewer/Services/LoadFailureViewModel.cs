using System;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Lightweight value object produced from a dataset-load exception.
/// Used by <see cref="DatasetLoaderService"/> to render a friendly
/// one-line message in the error toast while preserving the full
/// <see cref="object.ToString"/> output of the original exception for
/// the toast's "Copy details" action.
/// </summary>
internal sealed class LoadFailureViewModel
{
    public LoadFailureViewModel(string primaryMessage, string details)
    {
        PrimaryMessage = primaryMessage;
        Details = details;
    }

    /// <summary>
    /// Short, localized one-line message shaped from the innermost
    /// structured S-100 exception when present, otherwise the raw
    /// exception message. Suitable for use as the toast content.
    /// </summary>
    public string PrimaryMessage { get; }

    /// <summary>
    /// Full clipboard payload built from the outermost exception
    /// (preserves the whole inner-exception chain and stack trace),
    /// prefixed with a small header carrying the dataset display name
    /// and file path so the copied text is self-contained.
    /// </summary>
    public string Details { get; }

    /// <summary>
    /// Builds a view-model from the outermost exception thrown by the
    /// dataset loader. The primary message is shaped from the
    /// innermost structured S-100 exception (when present); details
    /// always carry <paramref name="originalException"/>'s full
    /// <see cref="object.ToString"/> output so the copied payload
    /// includes the whole chain.
    /// </summary>
    public static LoadFailureViewModel FromException(
        string displayName,
        string filePath,
        Exception originalException)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(originalException);

        var unwrapped = LoadFailureClassifier.Unwrap(originalException);

        string primary = unwrapped switch
        {
            S100DatasetNotSupportedException ns => string.Format(
                Strings.LoadFailureToast_NotSupportedBody,
                ns.Product, ns.Feature),

            S100DatasetSchemaException sx => string.Format(
                Strings.LoadFailureToast_SchemaBody,
                sx.Product, sx.AttributeOrDataset ?? string.Empty, sx.GroupPath),

            _ => unwrapped.Message,
        };

        return new LoadFailureViewModel(
            primaryMessage: primary,
            details: BuildDetails(displayName, filePath, originalException));
    }

    private static string BuildDetails(string displayName, string filePath, Exception ex) =>
        $"Dataset: {displayName}{Environment.NewLine}" +
        $"File: {filePath}{Environment.NewLine}" +
        $"{Environment.NewLine}" +
        ex;
}
