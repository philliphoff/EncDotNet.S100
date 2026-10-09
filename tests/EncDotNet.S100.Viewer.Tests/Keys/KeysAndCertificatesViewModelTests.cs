using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Services.Secom;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Keys;

namespace EncDotNet.S100.Viewer.Tests.Keys;

/// <summary>
/// Settings → Keys &amp; certificates (#845): rows, statuses, banners, tab
/// counts, the Settings status line, and the Import / Remove / authority flows.
/// </summary>
public sealed class KeysAndCertificatesViewModelTests : IDisposable
{
    private readonly KeysTestSupport _keys = new();

    public void Dispose() => _keys.Dispose();

    [Fact]
    public void With_no_identity_the_identities_tab_is_empty_and_settings_shows_no_status()
    {
        using var page = _keys.Page();

        Assert.True(page.IsIdentitiesEmpty);
        Assert.Equal("0", page.IdentitiesCount);
        Assert.False(page.HasStatus);
        Assert.Equal("0", page.IdentitiesCount);
    }

    [Fact]
    public void An_identity_ending_in_12_days_is_amber_on_its_row_and_in_settings()
    {
        _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile(daysLeft: 12), "secret"), "Bridge PC", use: true);
        using var page = _keys.Page();

        var row = Assert.Single(page.IdentityRows);
        Assert.Equal("Ends in 12 days", row.Status);
        Assert.Equal(KeysTone.Warning, row.StatusTone);
        Assert.Contains(row.Badges, b => b.Text == "IN USE");
        Assert.StartsWith("sc-ident:", row.Reference, StringComparison.Ordinal);
        Assert.Equal("Bridge PC's certificate ends in 12 days", page.StatusText);
        Assert.True(page.StatusIsWarning);
        Assert.Equal("1", page.IdentitiesCount);
    }

    [Fact]
    public void An_identity_that_ended_is_red()
    {
        _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile(daysLeft: 12), "secret"), "Bridge PC", use: true);
        using var page = _keys.Page();

        _keys.Time.Advance(TimeSpan.FromDays(13));
        page.Refresh();

        Assert.Equal("Ended", page.IdentityRows[0].Status);
        Assert.Equal("Bridge PC's certificate has ended", page.StatusText);
        Assert.True(page.StatusIsDestructive);
        Assert.Equal("!", page.IdentitiesCount);
    }

    [Fact]
    public void A_revoked_identity_shows_the_banner_and_offers_another()
    {
        var bridge = _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile("bridge"), "secret"), "Bridge PC", use: false);
        var dev = _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile("dev"), "secret"), "Dev laptop", use: true);
        dev.RevokedAt = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        _keys.Identities.Use(null);
        using var page = _keys.Page();

        Assert.True(page.HasRevokedBanner);
        Assert.Equal("Dev laptop's certificate was revoked", page.RevokedBannerTitle);
        Assert.Contains("Example MCP Root Certificate reported it revoked on 2026-10-05", page.RevokedBannerBody, StringComparison.Ordinal);
        Assert.Equal("Revoked", page.IdentityRows.Single(r => r.Id == dev.Id).Status);
        Assert.Equal(bridge.Id, Assert.Single(page.AlternativeIdentities).Id);
        Assert.True(page.StatusIsDestructive);
        Assert.Equal("!", page.IdentitiesCount);
    }

    [Fact]
    public void A_command_line_identity_is_a_temporary_row_without_actions()
    {
        var store = new SecomIdentityStore(_keys.Settings, _keys.Trust, _keys.Keys);
        store.Restore(SecomClientIdentity.Load(_keys.IdentityFile("cli"), "secret", _keys.Anchors));
        using var page = new KeysAndCertificatesViewModel(store, _keys.Authorities, _keys.Dialogs, _keys.Time, a => a());

        var row = Assert.Single(page.IdentityRows);
        Assert.Equal("From command line", row.Title);
        Assert.False(row.HasActions);
        Assert.False(row.HasReference);
    }

    [Fact]
    public void Remove_asks_first_then_deletes_the_key_and_stops_using_it()
    {
        _keys.Identities.Import(_keys.Identities.Load(_keys.IdentityFile(), "secret"), "Bridge PC", use: true);
        using var page = _keys.Page();

        page.IdentityRows[0].Actions.Single(a => a.Id == "Remove").Command.Execute(null);
        var (title, message, confirmed) = Assert.Single(_keys.Dialogs.Confirms);
        Assert.Equal("Remove Bridge PC?", title);
        Assert.Contains("in use", message, StringComparison.Ordinal);
        Assert.Equal(1, _keys.Keys.Count);

        confirmed();

        Assert.Null(_keys.Trust.Identity);
        Assert.Equal(0, _keys.Keys.Count);
        Assert.True(page.IsIdentitiesEmpty);
    }

    [Fact]
    public async Task Import_unlocks_checks_and_uses_the_identity()
    {
        using var page = _keys.Page();
        var dialog = new ImportIdentityDialogViewModel(_keys.Identities, () => Task.FromResult<string?>(_keys.IdentityFile(daysLeft: 12)), time: _keys.Time);
        Assert.True(await dialog.PickFileAsync());
        Assert.Equal("Step 1 of 3 · File", dialog.StepLabel);

        dialog.Password = "wrong";
        dialog.Primary();
        Assert.True(dialog.IsFileStep);
        Assert.True(dialog.HasError);
        Assert.Empty(_keys.Settings.SecomIdentities);

        dialog.Password = "secret";
        dialog.Primary();
        Assert.True(dialog.IsCheckStep);
        Assert.Equal("", dialog.Password);
        Assert.Equal("bridge-pc", dialog.Name);
        Assert.Contains(dialog.CheckFields, f => f.Label == "Chains to" && f.Value == "Example MCP · trusted" && f.IsSuccess);
        Assert.Contains(dialog.CheckFields, f => f.Label == "Valid" && f.IsWarning && f.Value.EndsWith("ends in 12 days", StringComparison.Ordinal));
        Assert.Contains(dialog.CheckFields, f => f.Label == "Key" && f.Value == "Elliptic curve P-384");

        dialog.Name = "Bridge PC";
        dialog.Primary();
        Assert.True(dialog.IsUseStep);
        Assert.True(dialog.UseForSecom);  // nothing was in use
        Assert.Null(_keys.Trust.Identity);

        dialog.Primary();
        Assert.Equal("Bridge PC", Assert.Single(page.IdentityRows).Title);
        Assert.NotNull(_keys.Trust.Identity);
    }

    [Fact]
    public async Task Import_shows_vessel_attributes_and_refuses_an_ended_certificate()
    {
        var dialog = new ImportIdentityDialogViewModel(_keys.Identities, () => Task.FromResult<string?>(_keys.IdentityFile(daysLeft: -1,
            attributes: new Dictionary<string, string>
            {
                ["2.25.291283622413876360871493815653100799259"] = "9876543",
                ["2.25.328433707816814908768060331477217690907"] = "440123456",
                ["2.25.323100633285601570573910217875371967771"] = "KR",
            })), time: _keys.Time);
        await dialog.PickFileAsync();
        dialog.Password = "secret";

        dialog.Primary();

        Assert.Contains(dialog.CheckFields, f => f.Label == "Vessel" && f.Value == "IMO 9876543 · MMSI 440123456 · flag KR");
        Assert.True(dialog.HasBlocker);
        Assert.False(dialog.CanPrimary);
    }

    [Fact]
    public void The_built_in_root_can_be_turned_off_but_not_removed()
    {
        using var page = _keys.Page();
        var builtIn = Assert.Single(page.AuthorityRows);
        Assert.Contains(builtIn.Badges, b => b.Text == "BUILT IN");
        Assert.DoesNotContain(builtIn.Actions, a => a.Id == "Remove");
        Assert.StartsWith("SHA-256 ", builtIn.Mono, StringComparison.Ordinal);

        builtIn.Actions.Single(a => a.Id == "TurnOff").Command.Execute(null);

        Assert.Empty(_keys.Trust.Anchors.Roots);
        Assert.Equal("Off", page.AuthorityRows[0].Status);
        page.AuthorityRows[0].Actions.Single(a => a.Id == "TurnOn").Command.Execute(null);
        Assert.Single(_keys.Trust.Anchors.Roots);
    }

    [Fact]
    public void An_authority_added_from_a_pem_file_is_trusted_and_can_be_removed()
    {
        using var other = KeysTestSupport.OtherRoot("Partner root", _keys.Time.GetUtcNow().AddYears(-1), _keys.Time.GetUtcNow().AddYears(2));
        using var page = _keys.Page();
        _keys.Dialogs.AuthorityFiles.Enqueue(_keys.RootPemFile(other));

        page.AddAuthorityCommand.Execute(null);
        var dialog = Assert.IsType<AddAuthorityDialogViewModel>(Assert.Single(_keys.Dialogs.Shown));
        Assert.Equal("Partner root", dialog.Name);
        dialog.Name = "Partner";
        dialog.AddCommand.Execute(null);

        Assert.Empty(_keys.Dialogs.Shown);
        Assert.Equal(2, _keys.Trust.Anchors.Roots.Count);
        var added = page.AuthorityRows.Single(r => r.Title == "Partner");
        Assert.Equal("2", page.AuthoritiesCount);

        added.Actions.Single(a => a.Id == "Remove").Command.Execute(null);
        Assert.Single(_keys.Dialogs.Confirms).Confirmed();
        Assert.Single(_keys.Trust.Anchors.Roots);
    }

    [Fact]
    public void An_expired_authority_shows_the_banner()
    {
        using var expired = KeysTestSupport.OtherRoot("Old root", _keys.Time.GetUtcNow().AddYears(-3), _keys.Time.GetUtcNow().AddDays(-7));
        _keys.Authorities.Add("Old root", expired.ExportCertificatePem(), _keys.Time.GetUtcNow());
        using var page = _keys.Page();

        Assert.True(page.HasExpiredAuthorityBanner);
        Assert.StartsWith("Old root expired ", page.ExpiredAuthorityBannerBody, StringComparison.Ordinal);
        Assert.Equal("Expired", page.AuthorityRows.Single(r => r.Title == "Old root").Status);
        Assert.Equal("!", page.AuthoritiesCount);
    }

    [Fact]
    public void Settings_can_open_keys_and_certificates_on_a_tab()
    {
        using var page = _keys.Page();
        var settings = new SettingsViewModel(_keys.Settings, keysAndCertificates: page);

        settings.ShowKeysAndCertificates(KeysTab.Authorities);

        Assert.Equal(SettingsCategory.KeysAndCertificates, settings.SelectedCategory);
        Assert.Equal(KeysTab.Authorities, page.SelectedTab);
    }
}
