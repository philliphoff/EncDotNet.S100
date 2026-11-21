using Avalonia.Controls;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Object Information shows a hit's pick sections through their kind's
/// presenter: the ice egg code as the egg diagram, and a kind with no
/// presenter as its heading and rows.
/// </summary>
public sealed class PickReportSectionsViewTests
{
    [AvaloniaFact]
    public void An_egg_code_section_shows_the_egg_diagram()
    {
        var vm = TestPickReport.Create();
        vm.SetPicks(
        [
            new PickHit
            {
                FeatureType = "SeaIce",
                FeatureRef = "seaice.0007",
                ProductSpec = "S-411",
                Sections = [new IceEggCodeSection(IceEggCodeBuilder.Build("70", "[30, 40]", "[91, 87]", "[5, 4]")!)],
            },
        ]);

        using var host = ViewHost.Show(new PickReportView { DataContext = vm }, width: 420, height: 900);

        Assert.True(host.IsShown("PickReport.Sections"));
        Assert.Contains(VisibleTexts(host), t => t == Strings.Pick_EggCode_Heading);
        Assert.Contains(VisibleTexts(host), t => t == "70");
    }

    [AvaloniaFact]
    public void A_section_without_a_presenter_shows_its_rows()
    {
        var vm = TestPickReport.Create();
        vm.SetPicks(
        [
            new PickHit
            {
                FeatureType = "AcmeIceChart",
                FeatureRef = "chart.1",
                Sections = [new NoteSection()],
            },
        ]);

        using var host = ViewHost.Show(new PickReportView { DataContext = vm }, width: 420, height: 900);

        Assert.True(host.IsShown("PickReport.Section.Rows"));
        var texts = VisibleTexts(host);
        Assert.Contains("acme.note", texts);
        Assert.Contains("Analyst note", texts);
        Assert.DoesNotContain(Strings.Pick_EggCode_Heading, texts);
    }

    [AvaloniaFact]
    public void A_hit_without_sections_hides_the_list()
    {
        var vm = TestPickReport.Create();
        vm.SetPicks([new PickHit { FeatureType = "DepthArea", FeatureRef = "42" }]);

        using var host = ViewHost.Show(new PickReportView { DataContext = vm }, width: 420, height: 900);

        Assert.False(host.IsShown("PickReport.Sections"));
    }

    private static List<string> VisibleTexts(ViewHost host) =>
        [.. host.All<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Text is not null).Select(t => t.Text!)];

    private sealed record NoteSection : PickSection
    {
        public override string Kind => "acme.note";

        public override IReadOnlyList<PickAttribute> ToRows() =>
            [new PickAttribute { Code = "note", Name = "Analyst note", RawValue = "Brash ice near the entrance." }];
    }
}
