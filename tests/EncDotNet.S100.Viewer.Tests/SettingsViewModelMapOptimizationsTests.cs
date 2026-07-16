using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers the Settings → Map rendering-optimization knobs: each maps to a
/// <see cref="RenderingOptimizations"/> flag, defaults on (the "best" set), and
/// persists through <see cref="ViewerSettings"/>.
/// </summary>
public class SettingsViewModelMapOptimizationsTests
{
    private static ViewerSettings NewSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        return new ViewerSettings { SettingsFilePath = path };
    }

    [Fact]
    public void Defaults_AllOn_WhenMissing()
    {
        var vm = new SettingsViewModel(NewSettings());

        Assert.True(vm.VectorSnapshotEnabled);
        Assert.True(vm.VectorSnapshotPrebuildEnabled);
        Assert.True(vm.VectorPathCacheEnabled);
        Assert.True(vm.GeometrySimplificationEnabled);
    }

    [Fact]
    public void Reads_PersistedFalse()
    {
        var s = NewSettings();
        s.VectorSnapshotEnabled = false;
        s.VectorSnapshotPrebuildEnabled = false;
        s.VectorPathCacheEnabled = false;
        s.GeometrySimplificationEnabled = false;

        var vm = new SettingsViewModel(s);

        Assert.False(vm.VectorSnapshotEnabled);
        Assert.False(vm.VectorSnapshotPrebuildEnabled);
        Assert.False(vm.VectorPathCacheEnabled);
        Assert.False(vm.GeometrySimplificationEnabled);
    }

    [Fact]
    public void Toggling_Persists_AndReloads()
    {
        var s = NewSettings();
        var vm = new SettingsViewModel(s);
        var reloads = 0;
        vm.MarinerChanged += () => reloads++;

        vm.VectorSnapshotEnabled = false;
        vm.GeometrySimplificationEnabled = false;

        Assert.False(s.VectorSnapshotEnabled);
        Assert.False(s.GeometrySimplificationEnabled);
        Assert.Equal(2, reloads);
        Assert.True(File.Exists(s.SettingsFilePath));

        File.Delete(s.SettingsFilePath);
    }

    [Fact]
    public void Toggling_UpdatesRenderingOptimizations_WhenNotEnvPinned()
    {
        // These assertions are only meaningful when the matching env var is not
        // explicitly pinning the flag (the perf A/B harness), in which case the
        // setter intentionally has no effect.
        var s = NewSettings();
        var vm = new SettingsViewModel(s);

        if (!RenderingOptimizations.VectorPathCacheEnvExplicit)
        {
            vm.VectorPathCacheEnabled = false;
            Assert.False(RenderingOptimizations.VectorPathCacheEnabled);
            vm.VectorPathCacheEnabled = true;
            Assert.True(RenderingOptimizations.VectorPathCacheEnabled);
        }

        if (!RenderingOptimizations.GeometrySimplificationEnvExplicit)
        {
            vm.GeometrySimplificationEnabled = false;
            Assert.False(RenderingOptimizations.GeometrySimplificationEnabled);
            vm.GeometrySimplificationEnabled = true;
            Assert.True(RenderingOptimizations.GeometrySimplificationEnabled);
        }

        if (File.Exists(s.SettingsFilePath)) File.Delete(s.SettingsFilePath);
    }
}
