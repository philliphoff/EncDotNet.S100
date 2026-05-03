using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IFileDialogService"/> implementation backed by
/// <see cref="TopLevel.StorageProvider"/>.
/// </summary>
internal sealed class FileDialogService : IFileDialogService
{
    public async Task<IReadOnlyList<string>> OpenDatasetsAsync(TopLevel? topLevel, bool allowMultiple)
    {
        if (topLevel?.StorageProvider is not { } picker)
            return Array.Empty<string>();

        var files = await picker.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.FilePicker_OpenDatasetTitle,
            AllowMultiple = allowMultiple,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Strings.FilePicker_S100DatasetsType)
                {
                    Patterns = new[] { "*.000", "*.h5", "*.hdf5", "*.gml" },
                },
                FilePickerFileTypes.All,
            },
        });

        if (files is null || files.Count == 0)
            return Array.Empty<string>();

        var result = new List<string>(files.Count);
        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is { } path)
                result.Add(path);
        }
        return result;
    }

    public async Task<string?> OpenFeatureCatalogueAsync(TopLevel? topLevel)
    {
        if (topLevel?.StorageProvider is not { } picker)
            return null;

        var files = await picker.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Feature Catalogue XML",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Feature Catalogue XML") { Patterns = new[] { "*.xml" } },
                FilePickerFileTypes.All,
            },
        });

        if (files is null || files.Count == 0)
            return null;

        return files[0].TryGetLocalPath();
    }

    public async Task<string?> OpenPortrayalCatalogueFolderAsync(TopLevel? topLevel)
    {
        if (topLevel?.StorageProvider is not { } picker)
            return null;

        var folders = await picker.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Portrayal Catalogue Folder",
            AllowMultiple = false,
        });

        if (folders is null || folders.Count == 0)
            return null;

        return folders[0].TryGetLocalPath();
    }
}
