using System;
using System.IO;
using System.Threading.Tasks;
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
    private Func<string, Task>? _openRecentAsync;

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
    /// <param name="openRecentAsync">Invoked when the user selects a path from the Recent submenu.</param>
    public void Attach(
        Window window,
        Func<Task> openDatasetAsync,
        Func<string, Task> openRecentAsync)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(openDatasetAsync);
        ArgumentNullException.ThrowIfNull(openRecentAsync);

        _openDatasetAsync = openDatasetAsync;
        _openRecentAsync = openRecentAsync;

        var sideBarItem = BuildToggleItem(
            Strings.Menu_PrimarySideBar,
            initiallyChecked: _viewModel.IsPaneVisible,
            execute: () => _viewModel.TogglePrimarySideBarCommand.Execute(null),
            propertyName: nameof(MainViewModel.IsPaneVisible),
            checkedSelector: () => _viewModel.IsPaneVisible);

        var statusBarItem = BuildToggleItem(
            Strings.Menu_StatusBar,
            initiallyChecked: _viewModel.IsStatusBarVisible,
            execute: () => _viewModel.ToggleStatusBarCommand.Execute(null),
            propertyName: nameof(MainViewModel.IsStatusBarVisible),
            checkedSelector: () => _viewModel.IsStatusBarVisible);

        var pickPanelItem = BuildToggleItem(
            Strings.Menu_PickReport,
            initiallyChecked: _viewModel.IsPickPanelEnabled,
            execute: () => _viewModel.TogglePickPanelCommand.Execute(null),
            propertyName: nameof(MainViewModel.IsPickPanelEnabled),
            checkedSelector: () => _viewModel.IsPickPanelEnabled);

        var pickModeItem = BuildToggleItem(
            Strings.Menu_PickMode,
            initiallyChecked: _viewModel.IsPickModeActive,
            execute: () => _viewModel.TogglePickModeCommand.Execute(null),
            propertyName: nameof(MainViewModel.IsPickModeActive),
            checkedSelector: () => _viewModel.IsPickModeActive,
            gesture: new KeyGesture(Key.I));

        var appearanceMenu = new NativeMenuItem(Strings.Menu_Appearance)
        {
            Menu = new NativeMenu { sideBarItem, statusBarItem, pickPanelItem, pickModeItem },
        };

        var viewMenu = new NativeMenuItem(Strings.Menu_View)
        {
            Menu = new NativeMenu { appearanceMenu },
        };

        var fileMenu = BuildFileMenu();

        var nativeMenu = new NativeMenu { fileMenu, viewMenu };
        NativeMenu.SetMenu(window, nativeMenu);

        RebuildOpenRecentMenu();
        _recentFiles.Changed += RebuildOpenRecentMenu;
    }

    private NativeMenuItem BuildToggleItem(
        string label,
        bool initiallyChecked,
        Action execute,
        string propertyName,
        Func<bool> checkedSelector,
        KeyGesture? gesture = null)
    {
        var item = new NativeMenuItem(label)
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = initiallyChecked,
        };
        if (gesture is not null)
            item.Gesture = gesture;
        item.Click += (_, _) => execute();

        _viewModel.PropertyChanged += (_, e) =>
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

        return new NativeMenuItem(Strings.Menu_File)
        {
            Menu = new NativeMenu { openItem, _openRecentMenuItem },
        };
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
            var label = Path.GetFileName(path);
            var item = new NativeMenuItem(label)
            {
                ToolTip = path,
                IsEnabled = File.Exists(path),
            };
            var captured = path;
            item.Click += async (_, _) => await _openRecentAsync!.Invoke(captured);
            _openRecentMenu.Items.Add(item);
        }

        _openRecentMenu.Items.Add(new NativeMenuItemSeparator());
        var clear = new NativeMenuItem(Strings.Menu_ClearRecentlyOpened);
        clear.Click += (_, _) => _recentFiles.Clear();
        _openRecentMenu.Items.Add(clear);
    }
}
