using System;
using System.Threading;
using System.Threading.Tasks;

namespace EncDotNet.S100.Viewer.Services;

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
    /// <param name="cancellationToken">Honored between datasets; the loader
    /// stops dispatching new datasets but does not interrupt one that is
    /// already loading.</param>
    Task OpenAsync(string folderOrZipPath, CancellationToken cancellationToken = default);
}
