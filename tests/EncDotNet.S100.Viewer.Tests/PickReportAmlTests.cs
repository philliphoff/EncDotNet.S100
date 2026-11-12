using Avalonia.Controls;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Object Information for NATO Additional Military Layers features (#1372,
/// design decision 13): the provenance strip replaces the spec, the class
/// line says whether the class is AML, AML-only attributes are marked, a
/// feature the map doesn't draw says so, and no AML hit feeds the Depth card.
/// The cell is synthetic.
/// </summary>
[Collection(nameof(ProductRegistryDefaultCollection))]
public sealed class PickReportAmlTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pick-aml-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AmlDatasetProcessor Lbo() => new(
        SyntheticAmlCell.Write(_dir, "UKL6UT00.000", new SyntheticAmlCell.Options
        {
            ProductSpecification = 53,
            Edition = "2.1",
            SecurityMarkingCode = 507,
            ExtraPoints =
            [
                new("OBSTRN", ("CATOBS", "7"), ("sonsig", "504")),
                new("contct", ("mulcon", "2")),
            ],
        }),
        AmlNotDrawnPortrayal.Instance);

    // As PickService.BuildHit makes it.
    private static PickHit Hit(AmlDatasetProcessor processor, string featureRef)
    {
        var info = processor.GetFeatureInfo(featureRef)!;
        return new PickHit
        {
            FeatureType = info.FeatureType,
            FeatureTypeName = info.FeatureTypeName,
            FeatureRef = info.FeatureRef,
            DatasetFileName = "UKL6UT00.000",
            ProductSpec = processor.Spec.Name,
            Attributes = info.Attributes,
            DrawingPriority = info.DrawingPriority,
            OwningProcessor = processor,
            Aml = AmlPickProvenance.From(processor, info),
        };
    }

    [AvaloniaFact]
    public void A_standard_class_with_AML_attributes_shows_the_strip_the_marks_and_not_drawn()
    {
        var processor = Lbo();
        var vm = TestPickReport.Create();
        vm.SetPicks([Hit(processor, "10")]);

        Assert.True(vm.HasAml);
        Assert.Equal(processor.Info.TagText, vm.Aml!.TagText);
        Assert.Equal("UNCLASSIFIED", vm.Aml.Marking);
        Assert.Equal("Obstruction", vm.PrimaryLabel);
        Assert.Equal("OBSTRN · S-57 class with AML attributes", vm.IdentityCaption);
        Assert.Equal("Not drawn. Picked from its position.", vm.AmlNotDrawnNote);
        Assert.Null(vm.PortrayalNote);
        Assert.Equal("Additional Military Layers, not a chart.", vm.NavigationNotice);
        Assert.Equal([null, "AML"], vm.Attributes.Select(a => a.Mark));
        Assert.Equal(["Category of obstruction", "Sonar Signal Strength"], vm.Attributes.Select(a => a.DisplayName));
        Assert.Equal(["Foul ground", "Strong"], vm.Attributes.Select(a => a.DisplayText));

        using var host = ViewHost.Show(new PickReportView { DataContext = vm }, width: 420, height: 900);
        Assert.True(host.IsShown("PickReport.AmlProvenance"));
        Assert.Equal(processor.Info.DataSetName, host.Find<SelectableTextBlock>("PickReport.AmlCell").Text);
        Assert.Equal("UNCLASSIFIED", host.Find<AmlMarkingChip>("PickReport.AmlMarking").DisplayText);
        Assert.Equal("Not drawn. Picked from its position.", host.Find<TextBlock>("PickReport.AmlNotDrawn").Text);
        var marks = host.FindAll<Border>("PickReport.Attribute.Mark");
        Assert.Single(marks, m => m.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void An_AML_class_is_named_as_one_and_carries_no_marks()
    {
        var vm = TestPickReport.Create();
        vm.SetPicks([Hit(Lbo(), "11")]);

        Assert.Equal("Contact", vm.PrimaryLabel);
        Assert.Equal("contct · AML class", vm.IdentityCaption);
        Assert.All(vm.Attributes, a => Assert.Null(a.Mark));
    }

    [Fact]
    public void A_drawn_AML_feature_has_no_not_drawn_note()
    {
        var hit = Hit(Lbo(), "10");
        var drawn = new PickHit
        {
            FeatureType = hit.FeatureType,
            FeatureTypeName = hit.FeatureTypeName,
            FeatureRef = hit.FeatureRef,
            ProductSpec = hit.ProductSpec,
            Attributes = hit.Attributes,
            DrawingPriority = 18,
            Aml = new AmlPickProvenance
            {
                TagText = hit.Aml!.TagText,
                TagToolTip = hit.Aml.TagToolTip,
                CellName = hit.Aml.CellName,
                Marking = hit.Aml.Marking,
                IsAmlClass = false,
                Drawn = true,
            },
        };
        var vm = TestPickReport.Create();
        vm.SetPicks([drawn]);

        Assert.Null(vm.AmlNotDrawnNote);
        Assert.False(vm.HasAmlNotDrawnNote);
    }

    [Fact]
    public void Other_products_have_no_strip_and_keep_the_spec_caption()
    {
        var vm = TestPickReport.Create();
        vm.SetPick("DepthArea", "Depth Area", "300", "test.000", "S-101", []);

        Assert.False(vm.HasAml);
        Assert.Null(vm.AmlNotDrawnNote);
        Assert.Equal("ID 300 · S-101 · test.000", vm.IdentityCaption);
    }

    [Fact]
    public void AML_hits_never_reach_the_Depth_card()
    {
        var aml = Hit(Lbo(), "10");
        var chart = new PickHit { FeatureType = "DepthArea", FeatureRef = "1", ProductSpec = "S-101" };

        Assert.Equal([chart], PickService.WithoutAml([aml, chart]));

        IReadOnlyList<PickHit> charts = [chart];
        Assert.Same(charts, PickService.WithoutAml(charts));
    }
}
