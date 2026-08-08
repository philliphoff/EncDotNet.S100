using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Mutable;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Verifies the presentation-mutating tools drive the shared
/// <see cref="IPresentationController"/> by transforming the immutable
/// <see cref="MapPresentationState"/> — the collapse of the viewer's three
/// bespoke setters onto one state seam (#560).
/// </summary>
public class MutablePresentationToolsTests
{
    // ---- set_palette ----------------------------------------------------

    [Fact]
    public async Task SetPalette_AppliesPaletteAndReturnsPrevious()
    {
        var host = new FakeController();
        var tool = new SetPaletteTool(Accessor(host));

        var result = await tool.InvokeAsync(new SetPaletteRequest("Night"));

        var value = AssertOk(result);
        Assert.Equal("Night", value.Palette);
        Assert.Equal("Day", value.Previous);
        Assert.Equal(PaletteType.Night, host.Current.Palette);
        Assert.Equal(1, host.ApplyCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Purple")]
    [InlineData("0")] // numeric coupling is rejected on purpose
    public async Task SetPalette_RejectsInvalidValue(string palette)
    {
        var host = new FakeController();
        var tool = new SetPaletteTool(Accessor(host));

        var result = await tool.InvokeAsync(new SetPaletteRequest(palette));

        Assert.IsType<InvalidArgument>(AssertErr(result));
        Assert.Equal(0, host.ApplyCount);
    }

    [Fact]
    public async Task SetPalette_ReportsHostNotReadyWhenControllerUnattached()
    {
        var tool = new SetPaletteTool(NullAccessor<IPresentationController>());

        var result = await tool.InvokeAsync(new SetPaletteRequest("Night"));

        Assert.IsType<HostNotReady>(AssertErr(result));
    }

    // ---- set_display_category ------------------------------------------

    [Fact]
    public async Task SetDisplayCategory_AppliesCategoryAndPreservesPalette()
    {
        var host = new FakeController();
        // Apply a palette first, then a category: the category change must not
        // clobber the palette — proving the WithX transforms compose.
        await new SetPaletteTool(Accessor(host)).InvokeAsync(new SetPaletteRequest("Dusk"));

        var result = await new SetDisplayCategoryTool(Accessor(host))
            .InvokeAsync(new SetDisplayCategoryRequest("DisplayBase"));

        var value = AssertOk(result);
        Assert.Equal("DisplayBase", value.DisplayCategory);
        Assert.Equal("Standard", value.Previous);
        Assert.Equal(EcdisDisplayCategory.DisplayBase, host.Current.EcdisDisplay.Category);
        Assert.Equal(PaletteType.Dusk, host.Current.Palette); // preserved
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("1")]
    public async Task SetDisplayCategory_RejectsInvalidValue(string category)
    {
        var host = new FakeController();

        var result = await new SetDisplayCategoryTool(Accessor(host))
            .InvokeAsync(new SetDisplayCategoryRequest(category));

        Assert.IsType<InvalidArgument>(AssertErr(result));
    }

    // ---- set_display_mode ----------------------------------------------

    [Fact]
    public async Task SetDisplayMode_AppliesFriendlyTokenForDefaultS411Spec()
    {
        var host = new FakeController();

        var result = await new SetDisplayModeTool(Accessor(host))
            .InvokeAsync(new SetDisplayModeRequest("ice-sod"));

        var value = AssertOk(result);
        Assert.Equal("S-411", value.Spec);
        Assert.Equal(S411DisplayModes.StageOfDevelopmentModeId, value.Mode);
        Assert.Null(value.Previous);
        Assert.False(value.Provisional);
        Assert.Equal(
            S411DisplayModes.StageOfDevelopmentModeId,
            host.Current.EcdisDisplay.ActiveDisplayModes["S-411"]);
    }

    [Fact]
    public async Task SetDisplayMode_FlagsProvisionalNavigationalAndReturnsPrevious()
    {
        var host = new FakeController();
        await new SetDisplayModeTool(Accessor(host)).InvokeAsync(new SetDisplayModeRequest("ice-sod"));

        var result = await new SetDisplayModeTool(Accessor(host))
            .InvokeAsync(new SetDisplayModeRequest("ice-navigational"));

        var value = AssertOk(result);
        Assert.Equal(S411DisplayModes.NavigationalModeId, value.Mode);
        Assert.Equal(S411DisplayModes.StageOfDevelopmentModeId, value.Previous);
        Assert.True(value.Provisional);
    }

    [Fact]
    public async Task SetDisplayMode_RejectsSpecWithoutSelectableModes()
    {
        var host = new FakeController();

        var result = await new SetDisplayModeTool(Accessor(host))
            .InvokeAsync(new SetDisplayModeRequest("ice-sod", "S-101"));

        var error = Assert.IsType<InvalidArgument>(AssertErr(result));
        Assert.Equal("spec", error.Parameter);
    }

    [Fact]
    public async Task SetDisplayMode_RejectsUnknownMode()
    {
        var host = new FakeController();

        var result = await new SetDisplayModeTool(Accessor(host))
            .InvokeAsync(new SetDisplayModeRequest("teal"));

        var error = Assert.IsType<InvalidArgument>(AssertErr(result));
        Assert.Equal("mode", error.Parameter);
    }

    // ---- helpers --------------------------------------------------------

    private static ICapabilityAccessor<IPresentationController> Accessor(IPresentationController c)
        => new StaticCapabilityAccessor<IPresentationController>(c);

    private static ICapabilityAccessor<T> NullAccessor<T>() where T : class => new NullCapabilityAccessor<T>();

    private static TValue AssertOk<TValue>(ToolResult<TValue> result)
    {
        Assert.True(result.TryGetValue(out var value), "expected a success result");
        return value!;
    }

    private static ToolError AssertErr<TValue>(ToolResult<TValue> result)
    {
        Assert.True(result.TryGetError(out var error), "expected an error result");
        return error!;
    }

    private sealed class NullCapabilityAccessor<T> : ICapabilityAccessor<T> where T : class
    {
        public T? Current => null;
    }

    private sealed class FakeController : IPresentationController
    {
        public MapPresentationState Current { get; private set; } = MapPresentationState.Default;

        public int ApplyCount { get; private set; }

        public Task SetPresentationAsync(
            MapPresentationState presentation, CancellationToken cancellationToken = default)
        {
            Current = presentation;
            ApplyCount++;
            return Task.CompletedTask;
        }
    }
}
