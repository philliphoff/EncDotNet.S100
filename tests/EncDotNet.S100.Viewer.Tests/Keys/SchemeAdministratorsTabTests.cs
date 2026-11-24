using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels.Keys;

namespace EncDotNet.S100.Viewer.Tests.Keys;

/// <summary>
/// Settings → Keys &amp; certificates → Scheme administrators: the built-in
/// IHO roots, roots added from files, banners, and the trust anchors that
/// exchange sets are verified with.
/// </summary>
public sealed class SchemeAdministratorsTabTests : IDisposable
{
    private readonly KeysTestSupport _keys = new();
    private readonly SchemeAdministratorStore _store;

    public SchemeAdministratorsTabTests()
    {
        _store = new SchemeAdministratorStore(_keys.Settings);
    }

    public void Dispose() => _keys.Dispose();

    private KeysAndCertificatesViewModel Page() =>
        new(_keys.Identities, _keys.Authorities, _keys.Dialogs, _keys.Time, post: a => a(), _store);

    [Fact]
    public void By_default_the_IHO_root_is_on_and_the_test_root_is_off()
    {
        using var page = Page();

        Assert.Equal("2", page.SchemeAdministratorsCount);
        Assert.False(page.HasNoSchemeAdministratorBanner);
        Assert.False(page.HasExpiredSchemeAdministratorBanner);

        var iho = page.SchemeAdministratorRows[0];
        Assert.Equal("IHO scheme administrator", iho.Title);
        Assert.Equal(["BUILT IN"], iho.Badges.Select(b => b.Text));
        Assert.Null(iho.Status);
        Assert.False(iho.IsDimmed);
        Assert.True(iho.IsSchemeAdministrator);
        Assert.Equal("SHA-256 24:51:A0:B2 … A9:45", iho.Mono);
        Assert.StartsWith("Self-signed · valid ", iho.Sub, StringComparison.Ordinal);
        Assert.Equal(["View", "TurnOff"], iho.Actions.Select(a => a.Id));

        var test = page.SchemeAdministratorRows[1];
        Assert.Equal("IHO test scheme administrator", test.Title);
        Assert.Equal(["BUILT IN", "TEST"], test.Badges.Select(b => b.Text));
        Assert.Equal("Off", test.Status);
        Assert.True(test.IsDimmed);
        Assert.True(test.HasWarningNote);
        Assert.Equal(["View", "TurnOn"], test.Actions.Select(a => a.Id));

        var anchors = _store.TrustAnchors();
        Assert.True(anchors.AllowUntrustedCertificates);
        Assert.Equal(SchemeAdministratorRoots.IhoRootFingerprint, SchemeAdministratorRoots.Sha256Fingerprint(Assert.Single(anchors.TrustedRoots)));
    }

    [Fact]
    public void Turning_the_IHO_root_off_shows_the_nothing_on_banner_which_turns_it_back_on()
    {
        using var page = Page();

        page.SchemeAdministratorRows[0].Actions.Single(a => a.Id == "TurnOff").Command.Execute(null);

        Assert.True(page.HasNoSchemeAdministratorBanner);
        Assert.Equal("Turn on IHO", page.NoSchemeAdministratorActionLabel);
        Assert.Empty(_store.TrustAnchors().TrustedRoots);
        Assert.False(_keys.Settings.BuiltInSchemeAdministrators.Values.Single());

        page.NoSchemeAdministratorActionCommand.Execute(null);

        Assert.False(page.HasNoSchemeAdministratorBanner);
        Assert.Single(_store.TrustAnchors().TrustedRoots);
    }

    [Fact]
    public void Turning_the_test_root_on_trusts_it_too()
    {
        using var page = Page();

        page.SchemeAdministratorRows[1].Actions.Single(a => a.Id == "TurnOn").Command.Execute(null);

        Assert.False(page.SchemeAdministratorRows[1].IsDimmed);
        Assert.Equal(2, _store.TrustAnchors().TrustedRoots.Count);
        Assert.True(_store.Find(_store.TrustAnchors().TrustedRoots[1])!.IsTest);
    }

    [Fact]
    public async Task A_root_added_from_a_file_is_listed_trusted_and_removable()
    {
        using var root = KeysTestSupport.OtherRoot("Example HO Root", _keys.Time.GetUtcNow().AddYears(-1), _keys.Time.GetUtcNow().AddYears(5));
        _keys.Dialogs.AuthorityFiles.Enqueue(_keys.RootPemFile(root));
        using var page = Page();

        page.AddSchemeAdministratorCommand.Execute(null);
        await Task.Yield();
        var dialog = Assert.IsType<AddSchemeAdministratorDialogViewModel>(Assert.Single(_keys.Dialogs.Shown));
        Assert.Equal("Example HO Root", dialog.Name);
        Assert.True(dialog.CanAdd);
        Assert.False(dialog.HasError);
        var sha = Assert.Single(dialog.Certificates).Single(f => f.CanCopy);
        Assert.Equal(SchemeAdministratorRoots.Sha256Fingerprint(root), sha.Value);

        dialog.Name = "Example HO";
        dialog.AddCommand.Execute(null);

        Assert.Empty(_keys.Dialogs.Shown);
        var row = page.SchemeAdministratorRows.Single(r => r.Title == "Example HO");
        Assert.Equal(["ADDED"], row.Badges.Select(b => b.Text));
        Assert.Equal(["View", "Remove"], row.Actions.Select(a => a.Id));
        Assert.Equal("3", page.SchemeAdministratorsCount);
        Assert.Contains(_store.TrustAnchors().TrustedRoots, r => r.RawData.AsSpan().SequenceEqual(root.RawData));
        Assert.Equal(SchemeAdministratorStore.Kind, Assert.Single(_keys.Settings.SchemeAdministrators).Kind);

        row.Actions.Single(a => a.Id == "Remove").Command.Execute(null);
        var confirm = Assert.Single(_keys.Dialogs.Confirms);
        Assert.Equal("Remove Example HO?", confirm.Title);
        confirm.Confirmed();

        Assert.Equal("2", page.SchemeAdministratorsCount);
        Assert.Empty(_keys.Settings.SchemeAdministrators);
    }

    [Fact]
    public void A_PEM_bundle_adds_each_root_under_its_common_name()
    {
        using var a = KeysTestSupport.OtherRoot("Root A", _keys.Time.GetUtcNow().AddYears(-1), _keys.Time.GetUtcNow().AddYears(5));
        using var b = KeysTestSupport.OtherRoot("Root B", _keys.Time.GetUtcNow().AddYears(-1), _keys.Time.GetUtcNow().AddYears(5));
        var path = Path.Combine(_keys.Folder, "bundle.pem");
        File.WriteAllText(path, a.ExportCertificatePem() + "\n" + b.ExportCertificatePem());

        var dialog = new AddSchemeAdministratorDialogViewModel(_store, _keys.Authorities, path, _keys.Time);
        Assert.True(dialog.IsBundle);
        Assert.False(dialog.ShowsName);
        Assert.Equal("Add 2 roots", dialog.AddLabel);
        dialog.AddCommand.Execute(null);

        Assert.Equal(["Root A", "Root B"], _store.Entries.Where(e => !e.BuiltIn).Select(e => e.Name));
    }

    [Fact]
    public void A_signer_certificate_is_refused_as_a_root()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Data Server", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        using var leaf = request.CreateSelfSigned(_keys.Time.GetUtcNow().AddDays(-1), _keys.Time.GetUtcNow().AddYears(1));

        var dialog = new AddSchemeAdministratorDialogViewModel(_store, _keys.Authorities, _keys.RootPemFile(leaf), _keys.Time);

        Assert.False(dialog.CanAdd);
        Assert.StartsWith("Data Server isn't a certificate authority", dialog.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_root_already_listed_or_expired_is_refused()
    {
        using var iho = SchemeAdministratorRoots.LoadIhoRoot();
        var duplicate = new AddSchemeAdministratorDialogViewModel(_store, _keys.Authorities, _keys.RootPemFile(iho), _keys.Time);
        Assert.False(duplicate.CanAdd);
        Assert.EndsWith("is already in the list.", duplicate.Error, StringComparison.Ordinal);

        using var old = KeysTestSupport.OtherRoot("Old Root", _keys.Time.GetUtcNow().AddYears(-3), _keys.Time.GetUtcNow().AddDays(-1));
        var expired = new AddSchemeAdministratorDialogViewModel(_store, _keys.Authorities, _keys.RootPemFile(old), _keys.Time);
        Assert.False(expired.CanAdd);
        Assert.StartsWith("This certificate has expired", expired.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_is_not_a_certificate_is_refused()
    {
        var path = Path.Combine(_keys.Folder, "notes.pem");
        File.WriteAllText(path, "not a certificate");

        var dialog = new AddSchemeAdministratorDialogViewModel(_store, _keys.Authorities, path, _keys.Time);

        Assert.False(dialog.CanAdd);
        Assert.Equal("This file isn't a certificate. Use a .cer, .crt, .pem or .der file.", dialog.Error);
    }

    [Fact]
    public void A_root_that_expires_while_on_shows_the_banner_and_marks_the_tab()
    {
        using var root = KeysTestSupport.OtherRoot("Short Root", _keys.Time.GetUtcNow().AddYears(-1), _keys.Time.GetUtcNow().AddDays(10));
        _store.Add([root], "Short Root", _keys.Time.GetUtcNow());
        using var page = Page();

        _keys.Time.Advance(TimeSpan.FromDays(11));
        page.Refresh();

        Assert.True(page.HasExpiredSchemeAdministratorBanner);
        Assert.StartsWith("Short Root expired ", page.ExpiredSchemeAdministratorBannerBody, StringComparison.Ordinal);
        Assert.Equal("!", page.SchemeAdministratorsCount);
        Assert.Equal("Expired", page.SchemeAdministratorRows.Single(r => r.Title == "Short Root").Status);
        Assert.False(page.HasNoSchemeAdministratorBanner);
    }

    [Fact]
    public void The_tab_sits_between_trusted_authorities_and_system_IDs()
    {
        using var page = Page();

        page.SelectedTab = KeysTab.SchemeAdministrators;

        Assert.Equal(2, page.SelectedTabIndex);
        Assert.Equal(KeysTab.SystemIds, (KeysTab)3);
    }
}
