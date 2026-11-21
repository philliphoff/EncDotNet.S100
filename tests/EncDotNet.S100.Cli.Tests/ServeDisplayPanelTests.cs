using System.Net;
using EncDotNet.S100.Cli.Web;
using EncDotNet.S100.Cli.Web.Panels;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the <c>tiles serve</c> web page's Display panel (issue #1579):
/// the layer choices and counts, the TileJSON a layer and palette pick, the
/// palette and layer in the URL hash, the notes, and the rendered panel for
/// datasets and for a built tile set.
/// </summary>
public sealed class ServeDisplayPanelTests
{
    private static ServedDataset Dataset(string id, string product, string? layer) =>
        new(id, id + ".000", product, null, null, null, null, [], null, null, layer);

    private static readonly ServedDataset[] Solent =
    [
        Dataset("101GB0040242B", "S-101", "chart"),
        Dataset("101GB0040242C", "S-101", "chart"),
        Dataset("101GB0050242A", "S-101", "chart"),
        Dataset("111UK_Solent", "S-111", "S111"),
    ];

    [Fact]
    public void Layers_list_all_layers_then_each_with_its_datasets_and_products()
    {
        var choices = ServeDisplay.LayerChoices(Solent, ["chart", "S111"]);

        Assert.Equal([null, "chart", "S111"], choices.Select(c => c.Id));
        Assert.Equal(["All layers", "chart", "S111"], choices.Select(c => c.Label));
        Assert.Equal([4, 3, 1], choices.Select(c => c.Count));
        Assert.Equal(["S-101", "S-111"], choices[0].Products);
        Assert.Equal(["S-101"], choices[1].Products);
    }

    [Fact]
    public void Without_layers_there_is_nothing_to_choose() =>
        Assert.Empty(ServeDisplay.LayerChoices(Solent, []));

    [Theory]
    [InlineData(null, "day", null, "tiles.json")]
    [InlineData(null, "night", null, "night/tiles.json")]
    [InlineData("chart", "day", null, "layers/chart/tiles.json")]
    [InlineData("S111", "dusk", "2021-04-01T10:00:00Z", "layers/S111/dusk/tiles.json?t=2021-04-01T10%3A00%3A00Z")]
    [InlineData("a b", null, null, "layers/a%20b/tiles.json")]
    public void The_chart_uses_the_layer_and_palette_tile_json(string? layer, string? palette, string? time, string expected) =>
        Assert.Equal(expected, ServeDisplay.TileJsonUrl(layer, palette, "day", time));

    [Fact]
    public void Palette_and_layer_round_trip_through_the_hash_with_defaults_left_out()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["dataset"] = "101GB0040242B" };

        ServeDisplay.WriteHash(parameters, "night", "day", "S111");
        Assert.Equal("dataset=101GB0040242B&layer=S111&palette=night", ServeHash.FormatParameters(parameters));

        var (_, read) = ServeHash.Parse("#13/50.775/-1.235&" + ServeHash.FormatParameters(parameters));
        Assert.Equal("night", ServeDisplay.PaletteFromHash(read, ["day", "dusk", "night"]));
        Assert.Equal("S111", ServeDisplay.LayerFromHash(read, ["chart", "S111"]));

        ServeDisplay.WriteHash(parameters, "day", "day", null);
        Assert.Equal("dataset=101GB0040242B", ServeHash.FormatParameters(parameters));
    }

    [Fact]
    public void A_hash_naming_a_palette_or_layer_not_served_is_ignored()
    {
        var (_, read) = ServeHash.Parse("#palette=NIGHT&layer=tides");

        // Palettes match without case (links typed by hand); layer ids are exact.
        Assert.Equal("night", ServeDisplay.PaletteFromHash(read, ["day", "dusk", "night"]));
        Assert.Null(ServeDisplay.PaletteFromHash(read, []));
        Assert.Null(ServeDisplay.LayerFromHash(read, ["chart", "S111"]));
    }

    [Theory]
    [InlineData("day", PageAppearance.Auto, "Chrome follows your browser.")]
    [InlineData("day", PageAppearance.Dark, "Chrome follows the Page setting.")]
    [InlineData("dusk", PageAppearance.Auto, "Dusk also darkens this page.")]
    [InlineData("dusk", PageAppearance.Light, "Page: Light keeps this page light.")]
    [InlineData("night", PageAppearance.Light, "Night also dims this page for the bridge.")]
    public void The_palette_note_says_what_it_does_to_the_page(string palette, PageAppearance page, string note) =>
        Assert.Equal(note, ServeDisplay.PaletteNote(palette, page));

    [Fact]
    public async Task The_panel_has_palette_layers_basemap_time_page_and_the_later_row()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(DisplayPanel.Palettes)] = new[] { "day", "dusk", "night" },
            [nameof(DisplayPanel.Palette)] = "night",
            [nameof(DisplayPanel.Layers)] = ServeDisplay.LayerChoices(Solent, ["chart", "S111"]),
            [nameof(DisplayPanel.Layer)] = "chart",
            [nameof(DisplayPanel.HasTimes)] = true,
            [nameof(DisplayPanel.LocalZone)] = "Europe/London (BST)",
        });

        foreach (var label in new[] { "Chart palette", "Layers", "Basemap", "Times", "Page" })
            Assert.Contains($">{label}<", html, StringComparison.Ordinal);

        // The palettes with their icons, Night chosen, and its note (D17, D18).
        Assert.Contains("aria-checked=\"true\"", html, StringComparison.Ordinal);
        Assert.Matches("role=\"radio\" class=\"segment\" aria-checked=\"true\"[^>]*><svg[^>]*>.*?</svg><span>Night</span>", html);
        Assert.Contains("Night also dims this page for the bridge.", html, StringComparison.Ordinal);
        Assert.Contains("The Night palette keeps night chrome, whatever this says.", html, StringComparison.Ordinal);

        // Layers with counts, the chosen one checked.
        Assert.Matches("<span class=\"radio-label\">All layers</span>\\s*<span class=\"radio-count\">4</span>", html);
        Assert.Matches("<span class=\"radio-label\">chart</span>\\s*<span class=\"radio-count\">3 · S-101</span>", html);
        Assert.Matches("value=\"chart\" checked", html);

        // Muted by default (Q4), and the placeholder (D17).
        Assert.Matches("aria-checked=\"true\"[^>]*><span>Muted</span>", html);
        Assert.Contains("Needs the internet.", html, StringComparison.Ordinal);
        Assert.Contains("Mariner settings", html, StringComparison.Ordinal);
        Assert.Contains(">Later<", html, StringComparison.Ordinal);

        // Times names the browser's zone (D20), Local chosen.
        Assert.Matches("aria-checked=\"true\"[^>]*><span>Local</span>", html);
        Assert.Contains("Local is your browser’s time zone, Europe/London (BST).", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_basemap_that_failed_to_load_shows_none_and_says_so()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(DisplayPanel.Basemap)] = Basemap.None,
            [nameof(DisplayPanel.BasemapFailed)] = true,
        });

        Assert.Matches("aria-checked=\"true\"[^>]*><span>None</span>", html);
        Assert.Contains("OpenStreetMap didn't load, so the map shows the chart only.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_built_tile_set_keeps_basemap_and_page_only()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            [nameof(DisplayPanel.HasDatasets)] = false,
            [nameof(DisplayPanel.Palettes)] = new[] { "day", "dusk", "night" },
            [nameof(DisplayPanel.Layers)] = ServeDisplay.LayerChoices(Solent, ["chart", "S111"]),
        });

        Assert.Contains(">Basemap<", html, StringComparison.Ordinal);
        Assert.Contains(">Page<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Chart palette", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Layers<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Mariner settings", html, StringComparison.Ordinal);
    }

    private static async Task<string> RenderAsync(Dictionary<string, object?> parameters)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<DisplayPanel>(ParameterView.FromDictionary(parameters))).ToHtmlString());
        return WebUtility.HtmlDecode(html);
    }
}
