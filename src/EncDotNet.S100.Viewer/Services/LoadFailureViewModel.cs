using System;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Lightweight view-model rendered by
/// <see cref="EncDotNet.S100.Viewer.Views.DatasetLoadFailureDialog"/>.
/// Built by <see cref="FromException"/> from the failing exception, the
/// dataset display name, and the file path; carries pre-resolved
/// localized strings plus the raw <c>ex.ToString()</c> text for the
/// collapsible details pane.
/// </summary>
internal sealed class LoadFailureViewModel
{
    public LoadFailureViewModel(
        string title,
        string displayName,
        string primaryMessage,
        string filePathLabel,
        string filePath,
        string? specReference,
        string details,
        string showDetailsLabel,
        string copyDetailsLabel,
        string copyDetailsTooltip,
        string closeLabel,
        string closeTooltip,
        string detailsCopiedLabel)
    {
        Title = title;
        DisplayName = displayName;
        PrimaryMessage = primaryMessage;
        FilePathLabel = filePathLabel;
        FilePath = filePath;
        SpecReference = specReference;
        Details = details;
        ShowDetailsLabel = showDetailsLabel;
        CopyDetailsLabel = copyDetailsLabel;
        CopyDetailsTooltip = copyDetailsTooltip;
        CloseLabel = closeLabel;
        CloseTooltip = closeTooltip;
        DetailsCopiedLabel = detailsCopiedLabel;
    }

    public string Title { get; }
    public string DisplayName { get; }
    public string PrimaryMessage { get; }
    public string FilePathLabel { get; }
    public string FilePath { get; }
    public string? SpecReference { get; }

    /// <summary>True when <see cref="SpecReference"/> is non-empty.</summary>
    public bool HasSpecReference => !string.IsNullOrWhiteSpace(SpecReference);

    /// <summary>Full <c>ex.ToString()</c> text for the details box.</summary>
    public string Details { get; }

    public string ShowDetailsLabel { get; }
    public string CopyDetailsLabel { get; }
    public string CopyDetailsTooltip { get; }
    public string CloseLabel { get; }
    public string CloseTooltip { get; }
    public string DetailsCopiedLabel { get; }

    /// <summary>
    /// Builds a view-model from the outermost exception thrown by the
    /// dataset loader. The primary message is shaped from the
    /// innermost structured S-100 exception (when present); the details
    /// pane always shows <paramref name="originalException"/>'s full
    /// <see cref="object.ToString"/> output so the user sees the whole
    /// chain.
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

        string primary;
        string? specRef;
        switch (unwrapped)
        {
            case S100DatasetNotSupportedException ns:
                primary = string.Format(
                    Strings.LoadFailureDialog_NotSupportedBody,
                    ns.Product,
                    ns.Feature);
                specRef = ns.SpecReference;
                break;

            case S100DatasetSchemaException sx:
                primary = string.Format(
                    Strings.LoadFailureDialog_SchemaBody,
                    sx.Product,
                    sx.AttributeOrDataset ?? string.Empty,
                    sx.GroupPath);
                specRef = sx.SpecReference;
                break;

            default:
                primary = string.Format(
                    Strings.LoadFailureDialog_GenericBody,
                    unwrapped.Message);
                specRef = null;
                break;
        }

        return new LoadFailureViewModel(
            title: Strings.LoadFailureDialog_Title,
            displayName: displayName,
            primaryMessage: primary,
            filePathLabel: Strings.LoadFailureDialog_FilePathLabel,
            filePath: filePath,
            specReference: string.IsNullOrWhiteSpace(specRef)
                ? null
                : string.Format(Strings.LoadFailureDialog_SpecReference, specRef),
            details: originalException.ToString(),
            showDetailsLabel: Strings.LoadFailureDialog_ShowDetails,
            copyDetailsLabel: Strings.LoadFailureDialog_CopyDetails,
            copyDetailsTooltip: Strings.LoadFailureDialog_CopyDetailsTooltip,
            closeLabel: Strings.LoadFailureDialog_Close,
            closeTooltip: Strings.LoadFailureDialog_CloseTooltip,
            detailsCopiedLabel: Strings.LoadFailureDialog_DetailsCopied);
    }
}
