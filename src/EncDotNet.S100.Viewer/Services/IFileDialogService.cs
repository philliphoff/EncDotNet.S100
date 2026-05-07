using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Wraps Avalonia's <see cref="Avalonia.Platform.Storage.IStorageProvider"/>
/// with the file-type presets used across the viewer (datasets, feature
/// catalogues, portrayal catalogue folders). Returns local paths so
/// callers don't have to round-trip through <c>IStorageFile</c>.
/// </summary>
internal interface IFileDialogService
{
    /// <summary>
    /// Prompts for one or more S-100 dataset files (ISO 8211 / HDF5 / GML).
    /// Returns the local paths the user chose, or an empty list when the
    /// dialog was cancelled or no <see cref="TopLevel"/> was available.
    /// </summary>
    Task<IReadOnlyList<string>> OpenDatasetsAsync(TopLevel? topLevel, bool allowMultiple);

    /// <summary>
    /// Prompts for a single Feature Catalogue XML file.
    /// </summary>
    Task<string?> OpenFeatureCatalogueAsync(TopLevel? topLevel);

    /// <summary>
    /// Prompts for a single Portrayal Catalogue folder.
    /// </summary>
    Task<string?> OpenPortrayalCatalogueFolderAsync(TopLevel? topLevel);

    /// <summary>
    /// Prompts for a single folder containing an S-100 exchange set
    /// (must include a <c>CATALOG.XML</c> at the root).
    /// </summary>
    Task<string?> OpenExchangeSetFolderAsync(TopLevel? topLevel);

    /// <summary>
    /// Prompts for a single <c>.zip</c> archive containing an S-100
    /// exchange set (a <c>CATALOG.XML</c> at the root of the archive).
    /// </summary>
    Task<string?> OpenExchangeSetZipAsync(TopLevel? topLevel);
}
