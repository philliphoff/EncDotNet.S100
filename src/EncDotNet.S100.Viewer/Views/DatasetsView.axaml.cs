using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Viewer.Views;

public partial class DatasetsView : UserControl
{
    public DatasetsView()
    {
        InitializeComponent();
        EmptyAddButton.Click += OnAddClick;
        ToolbarAddButton.Click += OnAddClick;
        SourcesAddButton.Click += OnAddClick;
        ((MenuFlyout)EmptyAddSourceButton.Flyout!).Opening += OnAddSourceFlyoutOpening;
    }

    /// <summary>
    /// Lists the data sources a user can add (design note D3): those that
    /// need no options, or have defaults. One already in the list is shown
    /// as added, and choosing it selects it.
    /// </summary>
    private void OnAddSourceFlyoutOpening(object? sender, EventArgs e)
    {
        if (DataContext is not DatasetsViewModel vm || sender is not MenuFlyout flyout)
            return;
        var sources = App.Services.GetService<Services.Sources.SourceDatasetService>();
        flyout.Items.Clear();
        if (sources is null)
            return;
        foreach (var registration in sources.Registrations)
        {
            if (registration.OptionsType is not null && registration.DefaultOptions is null)
                continue; // Needs settings first (slice 9's settings pages).
            var added = vm.FindSource(registration.Id) is not null;
            var item = new MenuItem
            {
                Header = added
                    ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Label_DataSourceAlreadyAddedFormat, registration.DisplayName)
                    : registration.DisplayName,
            };
            ToolTip.SetTip(item, registration.Description);
            Avalonia.Automation.AutomationProperties.SetAutomationId(item, "Datasets.Toolbar.AddSource." + registration.Id);
            Avalonia.Automation.AutomationProperties.SetName(item, registration.DisplayName);
            // D8: subscribing puts it in the Library's Live feeds, which puts it on the map.
            item.Click += (_, _) => App.Services.GetService<Services.Sources.LiveFeedLibrary>()?.Subscribe(registration);
            flyout.Items.Add(item);
        }
    }

    /// <summary>
    /// Fills the row menu's <b>Show in</b> with the panes (split map step 2
    /// §2.6, D18): a checked item per pane that toggles the dataset there,
    /// <b>Only Map n</b> per pane, and <b>Every map</b>. It is the keyboard
    /// and automation path of the chips' click and Alt-click.
    /// </summary>
    private void OnRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // Raised by a right-click and by the keyboard's context-menu key,
        // before the row menu opens.
        if (sender is Control { ContextMenu: { } menu, DataContext: DatasetEntry entry })
            FillShowIn(menu, entry);
    }

    /// <summary>Fills <paramref name="menu"/>'s <b>Show in</b> submenu for <paramref name="entry"/>.</summary>
    internal static void FillShowIn(ContextMenu menu, DatasetEntry entry)
    {
        if (menu.Items.OfType<MenuItem>().FirstOrDefault(i => AutomationProperties.GetAutomationId(i) == "Datasets.RowMenu.ShowIn") is not { } showIn)
            return;
        showIn.Items.Clear();
        var chips = entry.PaneChips;
        foreach (var chip in chips)
        {
            showIn.Items.Add(ShowInItem(
                chip.Pane.Title,
                "Datasets.RowMenu.ShowIn.Map",
                chip.ToggleCommand,
                isChecked: chip.IsShown == true));
        }
        if (chips.Count > 0)
            showIn.Items.Add(new Separator());
        foreach (var chip in chips)
        {
            showIn.Items.Add(ShowInItem(
                string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Menu_ShowOnlyInFormat, chip.Pane.Title),
                "Datasets.RowMenu.ShowIn.Only",
                chip.ShowOnlyCommand,
                isChecked: null));
        }
        showIn.Items.Add(ShowInItem(
            Strings.Menu_ShowInEveryMap,
            "Datasets.RowMenu.ShowIn.Every",
            new CommunityToolkit.Mvvm.Input.RelayCommand(() => entry.SetVisible(true)),
            isChecked: null));
    }

    private static MenuItem ShowInItem(string header, string automationId, System.Windows.Input.ICommand command, bool? isChecked)
    {
        var item = new MenuItem { Header = header, Command = command };
        if (isChecked is { } value)
        {
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = value;
        }
        AutomationProperties.SetAutomationId(item, automationId);
        AutomationProperties.SetName(item, header);
        return item;
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DatasetsViewModel vm) return;

        var fileDialog = App.Services.GetRequiredService<IFileDialogService>();
        var paths = await fileDialog.OpenDatasetsAsync(TopLevel.GetTopLevel(this), allowMultiple: false);
        if (paths.Count == 0) return;

        var path = paths[0];
        if (!File.Exists(path)) return;

        var spec = DatasetPipelineFactory.DetectProductSpec(path);
        if (spec is null)
        {
            vm.ReportUnrecognizedFile(path);
            return;
        }

        var entry = vm.Add(path, spec);
        vm.RequestLoad(entry);
    }

    private async void OnDatasetDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not DatasetsViewModel vm) return;

        // Walk up from the tapped element to find the DatasetEntry data context
        if (e.Source is Control source)
        {
            var current = source;
            while (current is not null)
            {
                if (current.DataContext is DatasetEntry entry)
                {
                    // Reveal: ensure the dataset is loaded, then fly the map to
                    // its extent — including exchange-set cells that have zoomed
                    // out of view (issue #446). Awaited (async void, like the
                    // other event handlers here) so a fault surfaces on the UI
                    // thread instead of becoming an unobserved task exception.
                    await vm.RevealDatasetAsync(entry);
                    return;
                }
                current = current.Parent as Control;
            }
        }
    }
}
