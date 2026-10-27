using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui.Styles;

namespace EncDotNet.S100.Pipelines.Tests.DynamicSources;

public class PortrayedDynamicFeatureRendererTests
{
    private const string Svg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.2\" width=\"4mm\" height=\"4mm\" viewBox=\"-2 -2 4 4\">" +
        "<circle class=\"fTSTINK\" cx=\"0\" cy=\"0\" r=\"1\"/>" +
        "<circle class=\"pivotPoint layout\" fill=\"none\" cx=\"0\" cy=\"0\" r=\"1\"/></svg>";

    private sealed class TestPortrayal : IDynamicFeaturePortrayal
    {
        public ApplicationPalette Palette { get; } = new("Test", new Dictionary<string, PaletteColors>
        {
            ["TSTINK"] = PaletteColors.FromHex("#102030", "#405060", "#708090"),
            ["TSTHALO"] = PaletteColors.FromHex("#FFFFFF", "#000000", "#000000"),
        });

        public Func<string, string?> SymbolProvider { get; } = name => name == "TSTSYM" ? Svg : null;

        public bool CanPortray(DynamicFeature feature) => feature.Kind == "test.station";

        public IReadOnlyList<DrawingInstruction> Portray(DynamicFeature feature)
        {
            var builder = new InstructionBuilder { ScaleMinimum = 1_000_000 };
            return
            [
                builder.Point(feature.Id, "TSTSYM", rotation: 45, rotationCrs: SymbolRotationCrs.Geographic),
                builder.Text(feature.Id, "1.2 m", new TextInstructionStyle { FontColor = "TSTINK", HaloColor = "TSTHALO", HaloWidth = 0.32 }),
            ];
        }
    }

    private static DynamicFeature Feature(string kind = "test.station") => new()
    {
        Id = "test:1",
        Kind = kind,
        GeometryType = GeometryType.Point,
        Coordinates = [new GeoPosition(42.0, -70.0)],
        LastUpdated = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void CanRender_follows_the_portrayal()
    {
        var renderer = new PortrayedDynamicFeatureRenderer(new TestPortrayal());

        Assert.True(renderer.CanRender(Feature()));
        Assert.False(renderer.CanRender(Feature("vessel.ais.cargo")));
    }

    [Fact]
    public void Render_draws_the_symbol_and_label_as_self_styled_features()
    {
        var renderer = new PortrayedDynamicFeatureRenderer(new TestPortrayal());

        var features = renderer.Render(Feature()).ToList();

        Assert.All(features, f => Assert.Equal("test:1", f[MapsuiDisplayListRenderer.FeatureRefKey]));
        var image = features.SelectMany(f => f.Styles).OfType<ImageStyle>().Single();
        Assert.StartsWith("svg-content://", image.Image!.Source, StringComparison.Ordinal);
        Assert.Contains("#102030", image.Image.Source, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(45, image.SymbolRotation);
        Assert.True(image.RotateWithMap);
        Assert.True(image.MaxVisible < double.MaxValue, "SCAMIN should map to a visibility limit.");

        var labelled = features.Single(f => f.Styles.OfType<LabelStyle>().Any());
        var label = labelled.Styles.OfType<LabelStyle>().Single();
        Assert.Equal("1.2 m", label.GetLabelText(labelled));
        Assert.NotNull(label.Halo);
    }

    [Fact]
    public void Render_uses_the_current_palette()
    {
        var palette = PaletteType.Day;
        var renderer = new PortrayedDynamicFeatureRenderer(new TestPortrayal(), () => palette);

        palette = PaletteType.Night;
        var image = renderer.Render(Feature()).SelectMany(f => f.Styles).OfType<ImageStyle>().Single();

        Assert.Contains("#708090", image.Image!.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_host_reports_a_sources_plane_and_priority()
    {
        var host = new FakeOverlayLayerHost();
        using var sut = new S100DynamicSourceHost(host, marshal: a => a(), coalesceWindow: TimeSpan.Zero);

        sut.Register(new FakeDynamicFeatureSource("obs", new DynamicSourceMetadata
        {
            DisplayName = "Observations",
            Plane = S98DisplayPlane.OtherChartOverlays,
            WithinPlanePriority = 100,
        }));
        sut.Register(new FakeDynamicFeatureSource("ais", new DynamicSourceMetadata { DisplayName = "AIS" }));

        var obs = sut.Sources.Single(s => s.Id == "obs");
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, obs.Plane);
        Assert.Equal(100, obs.WithinPlanePriority);
        Assert.Equal(S98DisplayPlane.DynamicArrows, sut.Sources.Single(s => s.Id == "ais").Plane);
    }
}
