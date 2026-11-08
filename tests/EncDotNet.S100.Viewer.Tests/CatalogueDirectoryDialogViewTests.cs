using Avalonia.Controls;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The source step's layout follows the body's width (online-catalogue-dialog
/// D2): a details pane when wide, the chosen row's details inline when
/// compact, and a segmented category strip in place of the sidebar when narrow.
/// </summary>
public sealed class CatalogueDirectoryDialogViewTests
{
    [Theory]
    [InlineData(839.9, "compact")]
    [InlineData(840, "wide")]
    [InlineData(720, "compact")]
    [InlineData(719, "narrow")]
    public void Layout_breakpoints_are_measured_on_the_body(double width, string expected) =>
        Assert.Equal(expected, CatalogueDirectoryDialogView.LayoutFor(width));

    [AvaloniaTheory]
    [InlineData(920, true, false, true, false)]
    [InlineData(780, false, true, true, false)]
    [InlineData(640, false, true, false, true)]
    public void Each_width_shows_its_layout(double width, bool pane, bool inline, bool sidebar, bool strip)
    {
        var directory = new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All, probe: (_, _) =>
            Task.FromResult(new CatalogueProbe(null, null, null)));
        using var host = ViewHost.Show(new CatalogueDirectoryDialogView { DataContext = directory }, width: width, height: 560);

        Assert.Equal(pane, host.IsShown("Catalogues.Details"));
        Assert.Equal(inline, host.IsShown("Catalogues.Row.CopyUrl"));
        Assert.Equal(sidebar, host.IsShown("Catalogues.Categories"));
        Assert.Equal(strip, host.IsShown("Catalogues.CategoryStrip"));
        if (pane)
            Assert.Equal("NOAA ENC — all U.S. waters", host.Find<TextBlock>("Catalogues.Details.Name").Text);
    }

    [AvaloniaFact]
    public void The_details_pane_states_limitations_only_when_there_are_some()
    {
        var directory = new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All);
        using var host = ViewHost.Show(new CatalogueDirectoryDialogView { DataContext = directory }, width: 920, height: 560);
        Assert.False(host.IsShown("Catalogues.Details.Limitations"));

        directory.SelectedEntry = directory.Entries.Single(e => e.Source.Id == "chartcatalogs-at-ienc");
        host.Settle();

        Assert.True(host.IsShown("Catalogues.Details.Limitations"));
        Assert.Contains(host.All<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Download sizes are not known in advance.");
        Assert.Equal(KnownCatalogueCategory.Charts, directory.SelectedCategory);
    }

    [AvaloniaFact]
    public void Choosing_a_category_in_the_narrow_strip_opens_it()
    {
        var directory = new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All);
        using var host = ViewHost.Show(new CatalogueDirectoryDialogView { DataContext = directory }, width: 640, height: 560);

        host.Click(host.FindAll<Button>("Catalogues.StripCategory")[2]);

        Assert.Equal(KnownCatalogueCategory.Weather, directory.SelectedCategory);
    }

    [AvaloniaTheory]
    [InlineData(920)]
    [InlineData(640)]
    public void Add_by_url_opens_my_sources_and_focuses_the_form(double width)
    {
        var directory = new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All, probe: (_, _) =>
            Task.FromResult(new CatalogueProbe(null, null, null)));
        using var host = ViewHost.Show(new CatalogueDirectoryDialogView { DataContext = directory }, width: width, height: 560);
        Assert.False(host.IsShown("Catalogues.Url"));

        host.Click(host.Find<Button>("Catalogues.AddByUrl"));
        host.Settle();

        Assert.Equal(KnownCatalogueCategory.User, directory.SelectedCategory);
        Assert.True(host.Find<TextBox>("Catalogues.Url").IsFocused);
    }

    [AvaloniaFact]
    public void The_registry_action_is_a_row_in_secom_services_only()
    {
        var directory = new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All,
            loadSecomRegistry: (_, _) => Task.FromException<Collections.Secom.SecomRegistryListing>(new HttpRequestException("offline")));
        using var host = ViewHost.Show(new CatalogueDirectoryDialogView { DataContext = directory }, width: 920, height: 560);
        Assert.False(host.IsShown("Catalogues.ShowRegistry"));

        directory.SelectedCategory = KnownCatalogueCategory.Secom;
        host.Settle();
        host.Click(host.Find<Button>("Catalogues.ShowRegistry"));
        host.Settle();

        Assert.True(directory.HasRegistryError);
        Assert.Contains(host.All<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.Contains("offline", StringComparison.Ordinal) == true);
    }
}
