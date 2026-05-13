using System;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Core;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Header row surfaced above the dataset list when an exchange set is
/// loaded. Carries catalogue-level metadata (producer, issue date,
/// dataset count, source path) plus a Close command that delegates
/// back to the registering service so it can remove every entry that
/// came from this set in one shot.
/// </summary>
/// <remarks>
/// The viewer keeps one of these per loaded exchange set in
/// <see cref="DatasetsViewModel.ExchangeSetHeaders"/>. Removing the
/// last <see cref="DatasetEntry"/> whose <c>Source</c> matches a
/// header (whether by clicking Close or by removing entries
/// individually) causes the registering service to dispose the
/// underlying <see cref="IAssetSource"/> and unregister the header.
/// </remarks>
internal sealed partial class ExchangeSetHeader : ViewModelBase
{
    /// <summary>The asset source backing the exchange set; used to
    /// match this header against <see cref="DatasetEntry.Source"/>.</summary>
    public IAssetSource Source { get; }

    /// <summary>The folder path or .zip path the user opened.</summary>
    public string SourcePath { get; }

    /// <summary>Short, user-facing label derived from
    /// <see cref="SourcePath"/> (the folder or archive name).</summary>
    public string DisplayName { get; }

    /// <summary>Catalogue-declared producer organisation, or
    /// <c>null</c> if unknown.</summary>
    public string? Producer { get; }

    /// <summary>Catalogue-derived issue date string (the latest
    /// <c>DatasetDiscoveryMetadata.IssueDate</c> across the set), or
    /// <c>null</c> if unknown.</summary>
    public string? IssueDate { get; }

    /// <summary>Total number of catalogued datasets.</summary>
    public int DatasetCount { get; }

    [ObservableProperty]
    private int _loadedCount;

    [ObservableProperty]
    private int _unsupportedCount;

    [ObservableProperty]
    private SignatureStatus _signatureStatus = SignatureStatus.Unknown;

    [ObservableProperty]
    private string? _signatureTooltip;

    /// <summary>Single-line summary combining loaded/unsupported counts,
    /// producer and issue date, joined with " · ". Bound to the
    /// trimmable secondary line in the header so any of the three
    /// pieces can fall off into the ellipsis when the pane narrows.
    /// Recomputed whenever <see cref="LoadedCount"/> or
    /// <see cref="UnsupportedCount"/> changes.</summary>
    public string MetadataSummary => BuildMetadataSummary(LoadedCount, UnsupportedCount, Producer, IssueDate);

    partial void OnLoadedCountChanged(int value) => OnPropertyChanged(nameof(MetadataSummary));
    partial void OnUnsupportedCountChanged(int value) => OnPropertyChanged(nameof(MetadataSummary));

    /// <summary>Removes every entry whose <see cref="DatasetEntry.Source"/>
    /// matches this header's <see cref="Source"/>. Wired by the
    /// registering service.</summary>
    public ICommand CloseCommand { get; }

    public ExchangeSetHeader(
        IAssetSource source,
        string sourcePath,
        string? producer,
        string? issueDate,
        int datasetCount,
        Action<ExchangeSetHeader> closeAction)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentNullException.ThrowIfNull(closeAction);

        Source = source;
        SourcePath = sourcePath;
        DisplayName = DeriveDisplayName(sourcePath);
        Producer = producer;
        IssueDate = issueDate;
        DatasetCount = datasetCount;
        // Initialise counts to the catalogue total so the header has a
        // sensible label while datasets are still loading. The service
        // overwrites these once it knows the loaded/unsupported split.
        _loadedCount = datasetCount;
        _unsupportedCount = 0;
        CloseCommand = new RelayCommand(() => closeAction(this));
    }

    private static string BuildMetadataSummary(int loadedCount, int unsupportedCount, string? producer, string? issueDate)
    {
        var parts = new System.Collections.Generic.List<string>(4)
        {
            string.Format(Strings.Pane_ExchangeSetHeader_Count, loadedCount),
        };

        if (unsupportedCount > 0)
        {
            parts.Add(string.Format(Strings.Pane_ExchangeSetHeader_Unsupported, unsupportedCount));
        }

        if (!string.IsNullOrWhiteSpace(producer))
        {
            parts.Add(string.Format(Strings.Pane_ExchangeSetHeader_Producer, producer));
        }

        if (!string.IsNullOrWhiteSpace(issueDate))
        {
            parts.Add(string.Format(Strings.Pane_ExchangeSetHeader_Issued, issueDate));
        }

        return string.Join(" · ", parts);
    }

    private static string DeriveDisplayName(string sourcePath)
    {
        var trimmed = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? sourcePath : name;
    }
}
