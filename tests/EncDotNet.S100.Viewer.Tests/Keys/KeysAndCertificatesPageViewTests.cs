using Avalonia.Controls;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.Views.Settings;

namespace EncDotNet.S100.Viewer.Tests.Keys;

/// <summary>The Keys &amp; certificates page's view (#845): tabs, rows and chips wired to the page.</summary>
public sealed class KeysAndCertificatesPageViewTests : IDisposable
{
    private readonly KeysTestSupport _keys = new();

    public void Dispose() => _keys.Dispose();

    [AvaloniaFact]
    public void The_page_shows_the_empty_state_then_a_row_whose_remove_asks_first()
    {
        using var page = _keys.Page();
        using var host = ViewHost.Show(new KeysAndCertificatesSettingsPage { DataContext = page });
        Assert.True(host.IsShown("Keys.Identities.EmptyImport"));

        _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile(daysLeft: 12), "secret"), "Bridge PC", use: true);
        host.Settle();

        Assert.False(host.IsShown("Keys.Identities.EmptyImport"));
        host.Click(host.Find<Button>("Keys.Row.Remove"));
        Assert.Equal("Remove Bridge PC?", Assert.Single(_keys.Dialogs.Confirms).Title);
    }

    [AvaloniaFact]
    public void The_authorities_tab_lists_the_built_in_root_with_turn_off()
    {
        using var page = _keys.Page();
        using var host = ViewHost.Show(new KeysAndCertificatesSettingsPage { DataContext = page });

        host.Click(host.Find<TabItem>("Keys.Tab.Authorities"));

        host.Click(host.Find<Button>("Keys.Row.TurnOff"));
        Assert.Empty(_keys.Trust.Anchors.Roots);
        Assert.True(host.IsShown("Keys.Row.TurnOn"));
    }
}
