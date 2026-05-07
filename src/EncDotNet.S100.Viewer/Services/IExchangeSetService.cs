using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Per-dataset progress snapshot reported by
/// <see cref="IExchangeSetService.OpenAsync"/> as it works through a
/// catalogue. Reports are made on a background thread; the UI must
/// marshal back to the dispatcher before mutating bound state.
/// </summary>
/// <param name="SourcePath">The folder or .zip path the user opened.</param>
/// <param name="Total">Number of catalogued datasets discovered. Zero
/// before the catalogue has been parsed.</param>
/// <param name="Completed">Number of datasets the loader has dispatched
/// (success + failure). Always &lt;= <paramref name="Total"/>.</param>
/// <param name="Failed">Number of datasets that have errored or been
/// skipped (unsupported product specification).</param>
/// <param name="CurrentDataset">Catalogue-relative path of the dataset
/// being routed right now, or <c>null</c> at the start/end.</param>
public readonly record struct ExchangeSetProgress(
    string SourcePath,
    int Total,
    int Completed,
    int Failed,
    string? CurrentDataset);

/// <summary>
/// Final summary of an <see cref="IExchangeSetService.OpenAsync"/> run.
/// Always returned (even on cancellation) so the caller can surface a
/// partial-success / partial-failure banner without having to subscribe
/// to progress.
/// </summary>
public sealed class ExchangeSetOpenResult
{
    /// <summary>The folder or .zip path that was opened.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Total datasets in the catalogue.</summary>
    public int Total { get; init; }

    /// <summary>Datasets successfully dispatched to a processor.</summary>
    public int Loaded { get; init; }

    /// <summary>Datasets skipped because their product identifier is not
    /// supported (no per-spec processor available).</summary>
    public int SkippedUnsupported { get; init; }

    /// <summary>Datasets the catalogue parser flagged as a hard failure
    /// (e.g. missing file, IO error). Always 0 today; reserved for
    /// future per-dataset failure capture.</summary>
    public int Failed { get; init; }

    /// <summary>True when the user cancelled before all datasets were
    /// dispatched.</summary>
    public bool Cancelled { get; init; }

    /// <summary>True when the catalogue could not be located / parsed.</summary>
    public bool CatalogueNotFound { get; init; }

    /// <summary>Top-level fatal error (catalogue parse failure or
    /// unexpected exception). Empty when none.</summary>
    public string? FailureMessage { get; init; }

    /// <summary>Human-readable per-dataset skip messages (currently all
    /// "unsupported product spec" entries).</summary>
    public IReadOnlyList<string> SkipMessages { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Opens an S-100 exchange set (a folder containing a
/// <c>CATALOG.XML</c> or a <c>.zip</c> archive containing one) and
/// dispatches every catalogued dataset through the existing dataset
/// loader. Each successfully-routed dataset becomes a regular
/// <see cref="EncDotNet.S100.Viewer.ViewModels.DatasetEntry"/> in the
/// Datasets panel and renders through the same code path as a plain
/// file load.
/// </summary>
internal interface IExchangeSetService
{
    /// <summary>
    /// Opens an exchange set rooted at <paramref name="folderOrZipPath"/>,
    /// which may be either a directory (with a top-level
    /// <c>CATALOG.XML</c>) or a <c>.zip</c> archive (with the same).
    /// </summary>
    /// <param name="folderOrZipPath">Local path to the folder or ZIP archive.</param>
    /// <param name="progress">Optional progress sink. Reports are raised
    /// once after the catalogue is parsed and once per dataset thereafter.</param>
    /// <param name="cancellationToken">Honored between datasets; the loader
    /// stops dispatching new datasets but does not interrupt one that is
    /// already loading.</param>
    /// <returns>A summary that is always non-null (even on cancellation
    /// or fatal error).</returns>
    Task<ExchangeSetOpenResult> OpenAsync(
        string folderOrZipPath,
        IProgress<ExchangeSetProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

