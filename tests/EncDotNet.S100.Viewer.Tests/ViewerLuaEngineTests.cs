using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Viewer.Services.LazyLoading;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The viewer's Lua engine setting.</summary>
public sealed class ViewerLuaEngineTests
{
    private static ViewerSettings NewSettings() =>
        new() { SettingsFilePath = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json") };

    [Fact]
    public void Default_IsLuaCSharp()
    {
        Assert.SkipWhen(ViewerLuaEngine.IsPinnedByEnvironment, "S100_LUA_ENGINE pins the engine.");
        var settings = NewSettings();

        Assert.Null(settings.LuaEngine);
        Assert.Equal(LuaEngineKind.LuaCSharp, settings.ResolveLuaEngine());
        Assert.Equal(LuaEngineKind.LuaCSharp, new SettingsViewModel(settings).SelectedLuaEngine);
    }

    [Theory]
    [InlineData("moonsharp", LuaEngineKind.MoonSharp)]
    [InlineData("luacsharp", LuaEngineKind.LuaCSharp)]
    [InlineData("unknown", LuaEngineKind.LuaCSharp)]
    public void ResolveLuaEngine_ReadsThePersistedName(string value, LuaEngineKind expected)
    {
        Assert.SkipWhen(ViewerLuaEngine.IsPinnedByEnvironment, "S100_LUA_ENGINE pins the engine.");
        var settings = NewSettings();
        settings.LuaEngine = value;

        Assert.Equal(expected, settings.ResolveLuaEngine());
    }

    [Fact]
    public void SelectingAnEngine_PersistsItsName()
    {
        Assert.SkipWhen(ViewerLuaEngine.IsPinnedByEnvironment, "S100_LUA_ENGINE pins the engine.");
        var settings = NewSettings();
        var vm = new SettingsViewModel(settings);

        vm.SelectedLuaEngine = LuaEngineKind.MoonSharp;

        Assert.Equal("moonsharp", settings.LuaEngine);
        Assert.Equal(LuaEngineKind.MoonSharp, settings.ResolveLuaEngine());
        Assert.True(vm.LuaEngineEditable);
    }

    [Fact]
    public void Create_BuildsTheSelectedEngine()
    {
        Assert.IsType<Scripting.LuaCSharp.LuaCSharpLuaEngine>(ViewerLuaEngine.Create(LuaEngineKind.LuaCSharp));
        Assert.IsType<Scripting.MoonSharp.MoonSharpLuaEngine>(ViewerLuaEngine.Create(LuaEngineKind.MoonSharp));
    }

    /// <summary>
    /// The portrayal cache keys hash the engine's assembly, so the warmer's
    /// signature must change with the engine too, or a cell warmed on one
    /// engine would read as warm on the other.
    /// </summary>
    [Fact]
    public void WarmSignature_ChangesWithTheEngine()
    {
        Assert.SkipWhen(ViewerLuaEngine.IsPinnedByEnvironment, "S100_LUA_ENGINE pins the engine.");
        var luaCSharp = NewSettings();
        var moonSharp = NewSettings();
        moonSharp.LuaEngine = "moonsharp";

        Assert.NotEqual(
            PortrayalWarmSignature.Of(MapPresentationState.Default, luaCSharp),
            PortrayalWarmSignature.Of(MapPresentationState.Default, moonSharp));
    }
}
