using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Viewer.ViewModels;
using ShadUI;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Adds sources to the library interactively (issue #655): picks a folder,
/// ZIP or S-128 file and confirms the target collection in the "Add to
/// Library" dialog, or walks through the "Add online catalogue" wizard for an
/// online catalogue; then reveals the Library panel.
/// </summary>
internal sealed class LibraryImportCoordinator : ILibraryImporter
{
    /// <summary>The Library activity tab id.</summary>
    public const string LibraryPanelId = "Library";

    private readonly Library.LibraryService _library;
    private readonly IFileDialogService _fileDialogs;
    private readonly DialogManager _dialogManager;
    private readonly Func<AddToLibraryDialogViewModel> _dialogFactory;
    private readonly Func<AddOnlineCatalogueWizardViewModel> _wizardFactory;
    private readonly Func<SharedFeedDialogViewModel>? _sharedFeedFactory;
    private readonly IViewerUiControllerAccessor? _ui;

    public LibraryImportCoordinator(
        Library.LibraryService library,
        IFileDialogService fileDialogs,
        DialogManager dialogManager,
        Func<AddToLibraryDialogViewModel> dialogFactory,
        Func<AddOnlineCatalogueWizardViewModel> wizardFactory,
        Func<SharedFeedDialogViewModel>? sharedFeedFactory = null,
        IViewerUiControllerAccessor? ui = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(fileDialogs);
        ArgumentNullException.ThrowIfNull(dialogManager);
        ArgumentNullException.ThrowIfNull(dialogFactory);
        ArgumentNullException.ThrowIfNull(wizardFactory);
        _library = library;
        _fileDialogs = fileDialogs;
        _dialogManager = dialogManager;
        _dialogFactory = dialogFactory;
        _wizardFactory = wizardFactory;
        _sharedFeedFactory = sharedFeedFactory;
        _ui = ui;
    }

    public async Task AddFolderAsync(Guid? targetCollectionId)
    {
        if (await _fileDialogs.OpenLibraryFolderAsync(MainTopLevel()) is { } path)
            ShowDialog(AddToLibraryKind.Folder, path, targetCollectionId);
    }

    public async Task AddExchangeSetZipAsync(Guid? targetCollectionId)
    {
        if (await _fileDialogs.OpenExchangeSetZipAsync(MainTopLevel()) is { } path)
            ShowDialog(AddToLibraryKind.ExchangeSet, path, targetCollectionId);
    }

    public async Task AddS128CatalogueAsync(Guid? targetCollectionId)
    {
        if (await _fileDialogs.OpenS128CatalogueAsync(MainTopLevel()) is { } path)
            ShowDialog(AddToLibraryKind.S128Catalogue, path, targetCollectionId);
    }

    public Task AddOnlineCatalogueAsync(Guid? targetCollectionId)
    {
        var wizard = _wizardFactory();
        wizard.Start(targetCollectionId);
        ShowWizard(wizard);
        return Task.CompletedTask;
    }

    public Task AddSharedFeedAsync(Guid? targetCollectionId)
    {
        if (_sharedFeedFactory is null)
            return Task.CompletedTask;

        var dialog = _sharedFeedFactory();
        dialog.Connected += (_, source) =>
        {
            _dialogManager.Close(dialog);
            _ = AddKnownCatalogueAsync(source, targetCollectionId);
        };
        dialog.Cancelled += (_, _) => _dialogManager.Close(dialog);

        _dialogManager.CreateDialog(dialog)
            .Dismissible()
            .WithMaxWidth(520)
            .Show();
        return Task.CompletedTask;
    }

    public async Task AddKnownCatalogueAsync(KnownCatalogueSource source, Guid? targetCollectionId)
    {
        ArgumentNullException.ThrowIfNull(source);

        var wizard = _wizardFactory();
        var load = wizard.StartAtIncludeAsync(source, targetCollectionId);
        ShowWizard(wizard);
        await load;
    }

    private void ShowWizard(AddOnlineCatalogueWizardViewModel wizard)
    {
        wizard.Closed += (_, added) =>
        {
            _dialogManager.Close(wizard);
            if (added)
                RevealLibrary();
        };

        _dialogManager.CreateDialog(wizard)
            .Dismissible()
            .WithMaxWidth(640)
            .Show();
    }

    private void RevealLibrary()
    {
        if (_ui?.Current is { } ui)
            _ = ui.SetPanelVisibilityAsync(LibraryPanelId, visible: true);
    }

    public Task AddPathAsync(string path, Guid? targetCollectionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var kind = Directory.Exists(path)
            ? (ExchangeSetLayout.PickS100Catalogue(SafeFileNames(path)) is not null
               || SafeFileNames(path).Any(ExchangeSetLayout.IsS57CatalogueName)
                ? AddToLibraryKind.ExchangeSet
                : AddToLibraryKind.Folder)
            : AddToLibraryKind.ExchangeSet;

        ShowDialog(kind, path, targetCollectionId);
        return Task.CompletedTask;
    }

    public bool IsInLibrary(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var full = Normalize(path);
        return _library.Collections
            .Where(c => !c.IsSession)
            .SelectMany(c => c.Sources)
            .Any(s => s.Definition switch
            {
                LocalFolderSource f => IsSameOrUnder(full, Normalize(f.Path)),
                ExchangeSetSource e => string.Equals(full, Normalize(e.Path), StringComparison.OrdinalIgnoreCase),
                S128CatalogueSource c => string.Equals(full, Normalize(c.Path), StringComparison.OrdinalIgnoreCase),
                _ => false,
            });
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSameOrUnder(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private void ShowDialog(AddToLibraryKind kind, string? path, Guid? targetCollectionId)
    {
        var dialog = _dialogFactory();
        dialog.Initialize(kind, path, targetCollectionId);
        dialog.Closed += (_, confirmed) =>
        {
            _dialogManager.Close(dialog);
            if (confirmed)
                RevealLibrary();
        };

        _dialogManager.CreateDialog(dialog)
            .Dismissible()
            .WithMaxWidth(520)
            .Show();
    }

    private static IEnumerable<string?> SafeFileNames(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory).Select(Path.GetFileName).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static TopLevel? MainTopLevel() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }
            ? window
            : null;
}
