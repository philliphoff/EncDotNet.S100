using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Keys;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Settings → Advanced's Reset all settings and Clear caches (#1398).</summary>
public sealed class ResetApplicationDataWorkflowTests : IDisposable
{
    private readonly KeysTestSupport _keys = new();

    public void Dispose() => _keys.Dispose();

    [Fact]
    public void Clearing_the_caches_keeps_the_identities_and_restarts()
    {
        _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile(), "secret"), "Bridge PC", makeDefault: true);
        var reset = TestSettings.Reset(_keys.Settings, _keys.Keys);

        reset.Workflow.Run(ResetApplicationData.ClearCaches);

        Assert.Equal(["ClearCaches", "Restart"], reset.Calls);
        Assert.Single(_keys.Settings.SecomIdentities);
        Assert.Equal(1, _keys.Keys.Count);
    }

    [Fact]
    public void Resetting_all_removes_the_identities_keys_first_then_resets_and_restarts()
    {
        _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile(), "secret"), "Bridge PC", makeDefault: true);
        var reset = TestSettings.Reset(_keys.Settings, _keys.Keys);

        reset.Workflow.Run(ResetApplicationData.ResetAll);

        Assert.Equal(["ResetAll", "Restart"], reset.Calls);
        Assert.Empty(_keys.Settings.SecomIdentities);
        Assert.Equal(0, _keys.Keys.Count);
    }
}
