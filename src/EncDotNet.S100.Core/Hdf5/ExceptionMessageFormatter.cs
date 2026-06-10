namespace EncDotNet.S100.Hdf5;

/// <summary>
/// Shared message-text helpers used by
/// <see cref="S100DatasetSchemaException"/>,
/// <see cref="S100DatasetNotSupportedException"/>, and dataset readers
/// that need to construct a "not yet supported" message with the
/// canonical four-piece shape (product, file, feature, spec ref).
/// </summary>
public static class ExceptionMessageFormatter
{
    /// <summary>
    /// Formats the standard "missing required attribute / group" message.
    /// </summary>
    /// <param name="additionalContext">
    /// Optional extra sentence appended after the standard text — used,
    /// for example, to note that the dataset declares an unexpected
    /// product-specification edition that may explain the failure.
    /// </param>
    public static string FormatSchema(
        string product,
        string? file,
        string groupPath,
        string? attributeOrDataset,
        string? specReference,
        string? additionalContext = null)
    {
        string subject = string.IsNullOrEmpty(file)
            ? $"{product} dataset"
            : $"{product} dataset {file}";

        string missing = attributeOrDataset is null
            ? $"is missing required group '{groupPath}'"
            : $"is missing required attribute '{attributeOrDataset}' on group '{groupPath}'";

        string cite = specReference is null ? string.Empty : $" ({specReference})";

        string note = string.IsNullOrEmpty(additionalContext) ? string.Empty : $" {additionalContext}";

        return $"{subject} {missing}{cite}. The file appears to be non-conforming.{note}";
    }

    /// <summary>
    /// Formats a "not yet supported" message with the canonical
    /// four-piece shape used across S-100 readers.
    /// </summary>
    public static string FormatNotSupported(
        string product,
        string? file,
        string feature,
        string? specReference,
        string? trailingHint)
    {
        string subject = string.IsNullOrEmpty(file)
            ? $"{product} dataset"
            : $"{product} dataset {file}";

        string cite = specReference is null ? string.Empty : $" ({specReference})";
        string hint = string.IsNullOrEmpty(trailingHint) ? string.Empty : $" {trailingHint}";

        return $"{subject} uses {feature}, which is not yet supported{cite}.{hint}";
    }
}
