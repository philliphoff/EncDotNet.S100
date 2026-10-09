using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Resources;
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

    private readonly CollectionLibrary _library;
    private readonly IFileDialogService _fileDialogs;
    private readonly DialogManager _dialogManager;
    private readonly Func<AddToLibraryDialogViewModel> _dialogFactory;
    private readonly Func<AddOnlineCatalogueWizardViewModel> _wizardFactory;
    private readonly Func<SharedFeedDialogViewModel>? _sharedFeedFactory;
    private readonly IViewerUiControllerAccessor? _ui;
    private readonly Notifications.INotificationService? _notifications;

    public LibraryImportCoordinator(
        CollectionLibrary library,
        IFileDialogService fileDialogs,
        DialogManager dialogManager,
        Func<AddToLibraryDialogViewModel> dialogFactory,
        Func<AddOnlineCatalogueWizardViewModel> wizardFactory,
        Func<SharedFeedDialogViewModel>? sharedFeedFactory = null,
        IViewerUiControllerAccessor? ui = null,
        Notifications.INotificationService? notifications = null)
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
        _notifications = notifications;
    }

    public async Task AddFolderAsync(Guid? targetCollectionId)
    {
        if (await _fileDialogs.OpenLibraryFolderAsync(MainTopLevel()) is { } path)
            ShowDialog(LibrarySourceKind.Folder, path, targetCollectionId);
    }

    public async Task AddExchangeSetZipAsync(Guid? targetCollectionId)
    {
        if (await _fileDialogs.OpenExchangeSetZipAsync(MainTopLevel()) is { } path)
            ShowDialog(LibrarySourceKind.ExchangeSet, path, targetCollectionId);
    }

    public async Task AddS128CatalogueAsync(Guid? targetCollectionId)
    {
        if (await _fileDialogs.OpenS128CatalogueAsync(MainTopLevel()) is { } path)
            ShowDialog(LibrarySourceKind.S128Catalogue, path, targetCollectionId);
    }

    public async Task AddCollectionManifestAsync(Guid? targetCollectionId)
    {
        if (await _fileDialogs.OpenCollectionManifestAsync(MainTopLevel()) is { } path)
            ShowManifestDialog(path, targetCollectionId, editing: null);
    }

    public Task ChooseManifestGroupsAsync(Guid collectionId, LocalManifestSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ShowManifestDialog(source.Path, collectionId, new EditedManifestSource(collectionId, source));
        return Task.CompletedTask;
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

    public async Task AddCurrentsForAreaAsync(GeoBounds area, Guid? targetCollectionId)
    {
        if (KnownCatalogueSources.All.FirstOrDefault(s => s.Format == KnownCatalogueFormat.S100ForecastModels && s.Product == "S-111")
            is not { } currents)
        {
            return;
        }

        var wizard = _wizardFactory();
        var load = wizard.StartAtIncludeAsync(currents, targetCollectionId);
        ShowWizard(wizard);
        await load;
        wizard.Scope?.PreselectModelsCovering(area);
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

        var kind = Classify(path);
        if (kind == LibrarySourceKind.LocalManifest)
            ShowManifestDialog(path, targetCollectionId, editing: null);
        else
            ShowDialog(kind, path, targetCollectionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// What adding <paramref name="path"/> means: a folder with a catalogue is
    /// an exchange set, any other folder is scanned; a collection manifest is
    /// one (by its <c>.s100collection.json</c> name or its <c>format</c>); any
    /// other file is an exchange set (a ZIP or catalogue).
    /// </summary>
    internal static LibrarySourceKind Classify(string path) => LibrarySourceKinds.Classify(path);

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
                LocalManifestSource m => string.Equals(full, Normalize(m.Path), StringComparison.OrdinalIgnoreCase),
                _ => false,
            });
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSameOrUnder(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private void ShowDialog(LibrarySourceKind kind, string path, Guid? targetCollectionId)
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

    /// <summary>
    /// Shows the one-page collection-manifest dialog: adding
    /// <paramref name="path"/>, or (with <paramref name="editing"/>) changing an
    /// existing source's groups. Adding announces what was added and reveals
    /// the Library.
    /// </summary>
    private void ShowManifestDialog(string path, Guid? targetCollectionId, EditedManifestSource? editing)
    {
        var scope = _dialogFactory();
        if (editing is not null)
            scope.InitializeEdit(editing.CollectionId, editing.Source);
        else
            scope.Initialize(LibrarySourceKind.LocalManifest, path, targetCollectionId);
        scope.PickManifest = () => _fileDialogs.OpenCollectionManifestAsync(MainTopLevel());
        scope.OpenManifest = OpenInEditor;

        var dialog = new AddCollectionManifestDialogViewModel(scope);
        scope.Closed += (_, confirmed) =>
        {
            _dialogManager.Close(dialog);
            if (!confirmed)
                return;
            if (editing is null)
            {
                _notifications?.Create(Strings.Manifest_DoneTitle)
                    .WithSeverity(Notifications.NotificationSeverity.Success)
                    .WithContent(scope.DoneMessage)
                    .Show();
            }
            RevealLibrary();
        };

        _dialogManager.CreateDialog(dialog)
            .Dismissible()
            .WithMaxWidth(640)
            .Show();
        _ = scope.LoadCatalogAsync();
    }

    private static void OpenInEditor(string path)
    {
        if (MainTopLevel()?.Launcher is { } launcher)
            _ = launcher.LaunchFileInfoAsync(new FileInfo(path));
    }

    private static TopLevel? MainTopLevel() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }
            ? window
            : null;
}
