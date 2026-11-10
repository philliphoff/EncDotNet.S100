using Avalonia.Controls;
using Avalonia.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Builds the application's <see cref="NativeMenu"/> tree (File menu and
/// View › Appearance toggles) and keeps the toggle items' check state in
/// sync with <see cref="MainViewModel"/>. Owns the subscriptions for the
/// lifetime of the window it is attached to — call <see cref="Attach"/>
/// once during window construction.
/// </summary>
internal sealed class NativeMenuBuilder
{
    private readonly MainViewModel _viewModel;
    private readonly IRecentFilesService _recentFiles;
    private readonly NativeMenu _openRecentMenu = new();
    private NativeMenuItem? _openRecentMenuItem;
    private Func<Task>? _openDatasetAsync;
    private Func<Task>? _openExchangeSetAsync;
    private Func<Task>? _openExchangeSetZipAsync;
    private ViewModels.ILibraryImporter? _libraryImporter;

    public NativeMenuBuilder(MainViewModel viewModel, IRecentFilesService recentFiles)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(recentFiles);
        _viewModel = viewModel;
        _recentFiles = recentFiles;
    }

    /// <summary>
    /// Constructs the native menu tree, attaches it to <paramref name="window"/>,
    /// wires the recent-files menu, and starts mirroring view-model toggle
    /// state into the corresponding <see cref="NativeMenuItemToggleType.CheckBox"/>
    /// items.
    /// </summary>
    /// <param name="window">The window to attach the menu to.</param>
    /// <param name="openDatasetAsync">Invoked when the user selects File › Open Dataset…</param>
    /// <param name="openExchangeSetAsync">Invoked when the user selects File › Open Exchange Set…</param>
    /// <param name="openExchangeSetZipAsync">Invoked when the user selects File › Open Exchange Set (ZIP)…</param>
    /// <param name="libraryImporter">
    /// Backs the File › Add to Library submenu (issue #655); the submenu is
    /// omitted when <see langword="null"/>.
    /// </param>
    public void Attach(
        Window window,
        Func<Task> openDatasetAsync,
        Func<Task> openExchangeSetAsync,
        Func<Task> openExchangeSetZipAsync,
        ViewModels.ILibraryImporter? libraryImporter = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(openDatasetAsync);
        ArgumentNullException.ThrowIfNull(openExchangeSetAsync);
        ArgumentNullException.ThrowIfNull(openExchangeSetZipAsync);

        _openDatasetAsync = openDatasetAsync;
        _openExchangeSetAsync = openExchangeSetAsync;
        _openExchangeSetZipAsync = openExchangeSetZipAsync;
        _libraryImporter = libraryImporter;

        var sideBarItem = BuildToggleItem(
            Strings.Menu_PrimarySideBar,
            _viewModel.Layout,
            initiallyChecked: _viewModel.Layout.IsLeftDockOpen,
            execute: () => _viewModel.Layout.TogglePrimarySideBarCommand.Execute(null),
            propertyName: nameof(ShellLayoutViewModel.IsLeftDockOpen),
            checkedSelector: () => _viewModel.Layout.IsLeftDockOpen);

        var statusBarItem = BuildToggleItem(
            Strings.Menu_StatusBar,
            _viewModel.Layout,
            initiallyChecked: _viewModel.Layout.IsStatusBarVisible,
            execute: () => _viewModel.Layout.ToggleStatusBarCommand.Execute(null),
            propertyName: nameof(ShellLayoutViewModel.IsStatusBarVisible),
            checkedSelector: () => _viewModel.Layout.IsStatusBarVisible);

        var timelineItem = BuildToggleItem(
            Strings.Menu_Timeline,
            _viewModel.Layout,
            initiallyChecked: _viewModel.Layout.IsBottomDockOpen,
            execute: () => _viewModel.Layout.ToggleTimelineCommand.Execute(null),
            propertyName: nameof(ShellLayoutViewModel.IsBottomDockOpen),
            checkedSelector: () => _viewModel.Layout.IsBottomDockOpen);

        var pickPanelItem = BuildToggleItem(
            Strings.Menu_PickReport,
            _viewModel.Layout,
            initiallyChecked: _viewModel.Layout.IsRightDockOpen,
            execute: () => _viewModel.Layout.TogglePickPanelCommand.Execute(null),
            propertyName: nameof(ShellLayoutViewModel.IsRightDockOpen),
            checkedSelector: () => _viewModel.Layout.IsRightDockOpen);

        var pickModeItem = BuildToggleItem(
            Strings.Menu_PickMode,
            _viewModel.MapTools,
            initiallyChecked: _viewModel.MapTools.IsPickModeActive,
            execute: () => _viewModel.MapTools.TogglePickModeCommand.Execute(null),
            propertyName: nameof(MapToolsViewModel.IsPickModeActive),
            checkedSelector: () => _viewModel.MapTools.IsPickModeActive,
            gesture: new KeyGesture(Key.I));

        var measureModeItem = BuildToggleItem(
            Strings.Menu_MeasureMode,
            _viewModel.MapTools,
            initiallyChecked: _viewModel.MapTools.IsMeasureModeActive,
            execute: () => _viewModel.MapTools.ToggleMeasureModeCommand.Execute(null),
            propertyName: nameof(MapToolsViewModel.IsMeasureModeActive),
            checkedSelector: () => _viewModel.MapTools.IsMeasureModeActive,
            gesture: new KeyGesture(Key.M, KeyModifiers.Meta));

        var rangeBearingModeItem = BuildToggleItem(
            Strings.Menu_RangeBearingMode,
            _viewModel.MapTools,
            initiallyChecked: _viewModel.MapTools.IsRangeBearingModeActive,
            execute: () => _viewModel.MapTools.ToggleRangeBearingModeCommand.Execute(null),
            propertyName: nameof(MapToolsViewModel.IsRangeBearingModeActive),
            checkedSelector: () => _viewModel.MapTools.IsRangeBearingModeActive,
            gesture: new KeyGesture(Key.R, KeyModifiers.Meta));

        // ⌘E is also handled by Tools.RouteEditorKeys; the menu's copy of the
        // shortcut must not fire while a text field has focus either.
        var routeEditModeItem = BuildToggleItem(
            Strings.Menu_RouteEditMode,
            _viewModel.MapTools,
            initiallyChecked: _viewModel.MapTools.IsRouteEditModeActive,
            execute: () =>
            {
                if (!Tools.RouteEditorKeys.IsTextInputFocused(window))
                    _viewModel.MapTools.ToggleRouteEditModeCommand.Execute(null);
            },
            propertyName: nameof(MapToolsViewModel.IsRouteEditModeActive),
            checkedSelector: () => _viewModel.MapTools.IsRouteEditModeActive,
            gesture: new KeyGesture(Key.E, KeyModifiers.Meta));

        var appearanceMenu = new NativeMenuItem(Strings.Menu_Appearance)
        {
            Menu = new NativeMenu { sideBarItem, statusBarItem, timelineItem, pickPanelItem, pickModeItem, measureModeItem, rangeBearingModeItem, routeEditModeItem },
        };

        var viewMenu = new NativeMenuItem(Strings.Menu_View)
        {
            Menu = new NativeMenu { appearanceMenu },
        };

        var fileMenu = BuildFileMenu();

        var reportFeedbackItem = new NativeMenuItem(Strings.Menu_ReportFeedback);
        reportFeedbackItem.Click += (_, _) => _viewModel.AppCommands.ShowFeedbackCommand.Execute(null);

        // On macOS the About item lives in the application menu (see
        // App.ConfigureMacApplicationMenu); elsewhere it belongs in Help.
        var showLogsItem = new NativeMenuItem(Strings.Menu_ShowLogs);
        showLogsItem.Click += (_, _) => _viewModel.Layout.ShowLogsCommand.Execute(null);

        var helpSubMenu = new NativeMenu { reportFeedbackItem, showLogsItem };
        if (!OperatingSystem.IsMacOS())
        {
            var aboutItem = new NativeMenuItem(Strings.Menu_About);
            aboutItem.Click += (_, _) => _viewModel.AppCommands.ShowAboutCommand.Execute(null);
            helpSubMenu.Add(aboutItem);
        }
        var helpMenu = new NativeMenuItem(Strings.Menu_Help)
        {
            Menu = helpSubMenu,
        };

        var nativeMenu = new NativeMenu { fileMenu, viewMenu, helpMenu };
        NativeMenu.SetMenu(window, nativeMenu);

        RebuildOpenRecentMenu();
        _recentFiles.Changed += RebuildOpenRecentMenu;
    }

    private static NativeMenuItem BuildToggleItem(
        string label,
        System.ComponentModel.INotifyPropertyChanged source,
        bool initiallyChecked,
        Action execute,
        string propertyName,
        Func<bool> checkedSelector,
        KeyGesture? gesture = null)
    {
        var item = new NativeMenuItem(label)
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = initiallyChecked,
        };
        if (gesture is not null)
            item.Gesture = gesture;
        item.Click += (_, _) => execute();

        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != propertyName) return;
            item.IsChecked = checkedSelector();
        };

        return item;
    }

    private NativeMenuItem BuildFileMenu()
    {
        var openItem = new NativeMenuItem(Strings.Menu_OpenDataset)
        {
            Gesture = new KeyGesture(Key.O, KeyModifiers.Meta),
        };
        openItem.Click += (_, _) => _ = _openDatasetAsync!.Invoke();

        _openRecentMenuItem = new NativeMenuItem(Strings.Menu_OpenRecent)
        {
            Menu = _openRecentMenu,
        };

        var openExchangeSetItem = new NativeMenuItem(Strings.Menu_OpenExchangeSet);
        openExchangeSetItem.Click += (_, _) => _ = _openExchangeSetAsync!.Invoke();

        var openExchangeSetZipItem = new NativeMenuItem(Strings.Menu_OpenExchangeSetZip);
        openExchangeSetZipItem.Click += (_, _) => _ = _openExchangeSetZipAsync!.Invoke();

        var menu = new NativeMenu
        {
            openItem,
            _openRecentMenuItem,
            new NativeMenuItemSeparator(),
            openExchangeSetItem,
            openExchangeSetZipItem,
        };

        if (_libraryImporter is { } importer)
        {
            NativeMenuItem Add(string header, Func<Task> action)
            {
                var item = new NativeMenuItem(header);
                item.Click += (_, _) => _ = action();
                return item;
            }

            menu.Add(new NativeMenuItemSeparator());
            menu.Add(new NativeMenuItem(Strings.Menu_AddToLibrary)
            {
                Menu = new NativeMenu
                {
                    // Grouped by where the data is: this computer, online, another computer.
                    Add(Strings.Menu_AddLibraryFolder, () => importer.AddFolderAsync(null)),
                    Add(Strings.Menu_AddLibraryExchangeSetZip, () => importer.AddExchangeSetZipAsync(null)),
                    Add(Strings.Menu_AddLibraryS128, () => importer.AddS128CatalogueAsync(null)),
                    Add(Strings.Menu_AddLibraryManifest, () => importer.AddCollectionManifestAsync(null)),
                    new NativeMenuItemSeparator(),
                    Add(Strings.Menu_AddLibraryOnlineCatalogue, () => importer.AddOnlineCatalogueAsync(null)),
                    new NativeMenuItemSeparator(),
                    Add(Strings.Menu_AddLibrarySharedFeed, () => importer.AddSharedFeedAsync(null)),
                },
            });
        }

        return new NativeMenuItem(Strings.Menu_File) { Menu = menu };
    }

    private void RebuildOpenRecentMenu()
    {
        _openRecentMenu.Items.Clear();

        var paths = _recentFiles.Items;
        if (paths.Count == 0)
        {
            var empty = new NativeMenuItem(Strings.Menu_NoRecentDatasets) { IsEnabled = false };
            _openRecentMenu.Items.Add(empty);
            if (_openRecentMenuItem is not null)
                _openRecentMenuItem.IsEnabled = false;
            return;
        }

        if (_openRecentMenuItem is not null)
            _openRecentMenuItem.IsEnabled = true;

        foreach (var path in paths)
        {
            // Entries may be exchange-set folders as well as files; name a
            // folder by its last segment.
            var label = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            var item = new NativeMenuItem(string.IsNullOrEmpty(label) ? path : label)
            {
                ToolTip = path,
                IsEnabled = File.Exists(path) || Directory.Exists(path),
            };
            var captured = path;
            item.Click += (_, _) => _viewModel.DatasetOpening.OpenRecentCommand.Execute(captured);
            _openRecentMenu.Items.Add(item);
        }

        _openRecentMenu.Items.Add(new NativeMenuItemSeparator());
        var clear = new NativeMenuItem(Strings.Menu_ClearRecentlyOpened);
        clear.Click += (_, _) => _recentFiles.Clear();
        _openRecentMenu.Items.Add(clear);
    }
}
