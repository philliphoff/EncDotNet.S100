using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Secom;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Keys;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Builds the Settings panel as the app does (#1398): its pages over one
/// <see cref="ViewerSettingsService"/>, with a reset workflow over fakes that
/// record what they were asked to do.
/// </summary>
internal static class TestSettings
{
    /// <summary>The panel with every page; Keys &amp; certificates only when <paramref name="keys"/> is given.</summary>
    public static SettingsViewModel Shell(ViewerSettingsService settings, KeysAndCertificatesViewModel? keys = null) =>
        new(Pages(settings, keys));

    /// <summary>The pages the app registers, in no particular order.</summary>
    public static IEnumerable<ISettingsPage> Pages(ViewerSettingsService settings, KeysAndCertificatesViewModel? keys = null)
    {
        yield return new AdvancedSettingsPageViewModel(settings, Reset(settings: null).Workflow, new ShadUI.DialogManager());
        yield return new GeneralSettingsPageViewModel(settings);
        yield return new AppearanceSettingsPageViewModel(settings);
        yield return new ChartSettingsPageViewModel(settings, new TideStationListViewModel(TideTestLists.Missing()));
        yield return new VesselsSettingsPageViewModel(settings);
        yield return new IntegrationsSettingsPageViewModel(settings);
        if (keys is not null)
            yield return keys;
    }

    /// <summary>A reset workflow over recording fakes and an identity store on <paramref name="settings"/> (a temporary one when null).</summary>
    public static ResetFakes Reset(ViewerSettings? settings, ISecomKeyStore? keys = null)
    {
        settings ??= new ViewerSettings { SettingsFilePath = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json") };
        var trust = new SecomServerTrust(new SecomTrustAnchors([]), TimeProvider.System);
        var identities = new SecomIdentityStore(settings, trust, keys ?? new InMemorySecomKeyStore());
        var log = new List<string>();
        var maintenance = new FakeDataMaintenance(log);
        var control = new FakeApplicationControl(log);
        return new ResetFakes(new ResetApplicationDataWorkflow(maintenance, control, identities), log);
    }

    /// <summary>The workflow and the calls its fakes saw, in order.</summary>
    internal sealed record ResetFakes(ResetApplicationDataWorkflow Workflow, List<string> Calls);

    private sealed class FakeDataMaintenance(List<string> log) : IDataMaintenanceService
    {
        public IReadOnlyList<string> CacheDirectories => [];
        public void ClearCaches() => log.Add(nameof(ClearCaches));
        public void ResetAll() => log.Add(nameof(ResetAll));
    }

    private sealed class FakeApplicationControl(List<string> log) : IApplicationControlService
    {
        public void Restart() => log.Add(nameof(Restart));
    }
}
