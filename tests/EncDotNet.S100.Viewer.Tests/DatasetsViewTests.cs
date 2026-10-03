using Avalonia;
using Avalonia.Controls;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using ShadTheme = ShadUI.ShadTheme;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The real Datasets panel, bound and laid out headlessly: the inspector's
/// tab strip follows <see cref="DatasetsViewModel.InspectorTab"/> and a tab
/// click writes back to it (the binding the MCP <c>select_dataset</c> tool
/// relies on).
/// </summary>
public sealed class DatasetsViewTests
{
    private static (Window Window, DatasetsView View) Show(DatasetsViewModel datasets)
    {
        var view = new DatasetsView { DataContext = datasets };
        // The app's ShadUI theme templates the tab strips; add it before the
        // content, since a control resolves its implicit theme when it joins
        // the tree.
        var window = new Window { Width = 420, Height = 900 };
        window.Styles.Add(new ShadTheme());
        window.Content = view;
        window.Show();
        window.Measure(new Size(420, 900));
        window.Arrange(new Rect(0, 0, 420, 900));
        return (window, view);
    }

    [Fact]
    public void Inspector_tabs_follow_the_view_model_both_ways() =>
        HeadlessTest.Run(() =>
        {
            var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
            var cell = datasets.Add("/data/US5SEAFL.000", "S-57");
            datasets.SelectDataset(cell);
            datasets.InspectorTab = DatasetInspectorTab.Validation;

            var (window, view) = Show(datasets);
            try
            {
                var inspector = view.FindControl<TabControl>("InspectorTabs")!;
                Assert.True(inspector.IsVisible);
                Assert.Equal(2, inspector.SelectedIndex);

                datasets.InspectorTab = DatasetInspectorTab.Layers;
                Assert.Equal(1, inspector.SelectedIndex);

                // A click on the Dataset tab selects it in the tab strip.
                ((TabItem)inspector.Items[0]!).IsSelected = true;
                Assert.Equal(DatasetInspectorTab.Dataset, datasets.InspectorTab);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void Selecting_a_dataset_highlights_its_row_on_the_datasets_tab() =>
        HeadlessTest.Run(() =>
        {
            var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
            datasets.Add("/data/US5OTHER.000", "S-57");
            var cell = datasets.Add("/data/US5SEAFL.000", "S-57");

            var (window, view) = Show(datasets);
            try
            {
                datasets.SelectDataset(cell);

                var list = view.FindControl<ListBox>("DatasetList")!;
                Assert.Same(cell, list.SelectedItem);
                Assert.True(view.FindControl<TabControl>("InspectorTabs")!.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
}
