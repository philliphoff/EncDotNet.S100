using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers the Settings → "Render subsystem" knobs (issue #331):
/// the A/B subsystem switch, the tiled scene mode, and the "B" tiled-pipeline
/// optimization knobs. Each mirrors a <see cref="RenderingOptimizations"/>
/// value, defaults to the "best" set, persists through <see cref="ViewerSettings"/>,
/// and is shown disabled (the matching <c>*Editable</c> flag is false) when an
/// explicit environment variable pins it.
/// </summary>
public class SettingsViewModelRenderSubsystemTests
{
    private static ViewerSettings NewSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        return new ViewerSettings { SettingsFilePath = path };
    }

    [Fact]
    public void Defaults_BestSet_WhenMissing()
    {
        if (RenderingOptimizations.RenderSubsystemEnvExplicit
            || RenderingOptimizations.SceneModeEnvExplicit)
        {
            return; // pinned by env; the default is not observable here
        }

        var vm = new SettingsViewModel(NewSettings());

        Assert.Equal(RenderSubsystemKind.TiledScene, vm.SelectedRenderSubsystem);
        Assert.Equal(VectorSceneMode.Tiled, vm.SelectedSceneMode);
        Assert.True(vm.TiledSceneSelected);
        Assert.True(vm.TilePredictionEnabled);
        Assert.True(vm.TileGpuResidencyEnabled);
        Assert.True(vm.TileDiskCacheEnabled);
    }

    [Fact]
    public void Reads_PersistedValues()
    {
        if (RenderingOptimizations.RenderSubsystemEnvExplicit
            || RenderingOptimizations.SceneModeEnvExplicit)
        {
            return;
        }

        var s = NewSettings();
        s.RenderSubsystem = nameof(RenderSubsystemKind.TiledScene);
        s.VectorSceneMode = nameof(VectorSceneMode.Single);

        var vm = new SettingsViewModel(s);

        Assert.Equal(RenderSubsystemKind.TiledScene, vm.SelectedRenderSubsystem);
        Assert.True(vm.TiledSceneSelected);
        Assert.False(vm.TiledModeActive); // single scene mode, not tiled
        Assert.Equal(VectorSceneMode.Single, vm.SelectedSceneMode);

        // Restore the global renderer flags so cross-test ordering is unaffected.
        vm.SelectedRenderSubsystem = RenderSubsystemKind.TiledScene;
        vm.SelectedSceneMode = VectorSceneMode.Tiled;
    }

    [Fact]
    public void SwitchingSubsystem_Persists_PushesToRenderer_AndReRenders()
    {
        if (RenderingOptimizations.RenderSubsystemEnvExplicit)
        {
            return; // pinned by env; the setter is intentionally a no-op
        }

        var s = NewSettings();
        var vm = new SettingsViewModel(s);
        var reloads = 0;
        vm.MarinerChanged += () => reloads++;

        try
        {
            vm.SelectedRenderSubsystem = RenderSubsystemKind.Mapsui;

            Assert.Equal(nameof(RenderSubsystemKind.Mapsui), s.RenderSubsystem);
            Assert.Equal(RenderSubsystemKind.Mapsui, RenderingOptimizations.RenderSubsystem);
            Assert.True(vm.MapsuiSelected);
            Assert.Equal(1, reloads);
            Assert.True(File.Exists(s.SettingsFilePath));
        }
        finally
        {
            vm.SelectedRenderSubsystem = RenderSubsystemKind.TiledScene;
            if (File.Exists(s.SettingsFilePath)) File.Delete(s.SettingsFilePath);
        }
    }

    [Fact]
    public void TiledModeActive_RequiresTiledSceneArm_AndTiledSceneMode()
    {
        if (RenderingOptimizations.RenderSubsystemEnvExplicit
            || RenderingOptimizations.SceneModeEnvExplicit)
        {
            return;
        }

        var vm = new SettingsViewModel(NewSettings());
        try
        {
            vm.SelectedRenderSubsystem = RenderSubsystemKind.Mapsui;
            Assert.False(vm.TiledModeActive); // Standard arm

            vm.SelectedRenderSubsystem = RenderSubsystemKind.TiledScene;
            Assert.True(vm.TiledModeActive); // Tiled arm + tiled scene mode (default)

            vm.SelectedSceneMode = VectorSceneMode.Single;
            Assert.False(vm.TiledModeActive); // Tiled arm + single
        }
        finally
        {
            vm.SelectedSceneMode = VectorSceneMode.Tiled;
            vm.SelectedRenderSubsystem = RenderSubsystemKind.TiledScene;
        }
    }

    [Fact]
    public void TileKnobs_Persist_AndClampThroughRenderingOptimizations()
    {
        if (RenderingOptimizations.TileGutterDipEnvExplicit
            || RenderingOptimizations.TileBudgetMbEnvExplicit)
        {
            return;
        }

        var s = NewSettings();
        var vm = new SettingsViewModel(s);
        var originalGutter = vm.TileGutterDip;
        var originalBudget = vm.TileBudgetMb;

        try
        {
            vm.TileGutterDip = RenderingOptimizations.MaxTileGutterDip + 1000.0;
            Assert.Equal(RenderingOptimizations.MaxTileGutterDip, vm.TileGutterDip);
            Assert.Equal(RenderingOptimizations.MaxTileGutterDip, s.TileGutterDip);

            vm.TileBudgetMb = 128.0;
            Assert.Equal(128.0, RenderingOptimizations.TileBudgetMb);
            Assert.Equal(128.0, s.TileBudgetMb);
        }
        finally
        {
            vm.TileGutterDip = originalGutter;
            vm.TileBudgetMb = originalBudget;
            if (File.Exists(s.SettingsFilePath)) File.Delete(s.SettingsFilePath);
        }
    }

    [Fact]
    public void EditableFlags_TrackEnvPinning()
    {
        var vm = new SettingsViewModel(NewSettings());

        Assert.Equal(!RenderingOptimizations.RenderSubsystemEnvExplicit, vm.RenderSubsystemEditable);
        Assert.Equal(!RenderingOptimizations.SceneModeEnvExplicit, vm.SceneModeEditable);
        Assert.Equal(!RenderingOptimizations.TilePredictionEnvExplicit, vm.TilePredictionEditable);
        Assert.Equal(!RenderingOptimizations.TileDiskCacheEnvExplicit, vm.TileDiskCacheEditable);
        Assert.Equal(!RenderingOptimizations.TileGpuResidencyEnvExplicit, vm.TileGpuResidencyEditable);
        Assert.Equal(!RenderingOptimizations.TileGutterDipEnvExplicit, vm.TileGutterDipEditable);
        Assert.Equal(!RenderingOptimizations.TileBudgetMbEnvExplicit, vm.TileBudgetMbEditable);
        Assert.Equal(!RenderingOptimizations.TileDiskMbEnvExplicit, vm.TileDiskMbEditable);
        Assert.Equal(!RenderingOptimizations.TileGpuBudgetMbEnvExplicit, vm.TileGpuBudgetMbEditable);
    }
}
