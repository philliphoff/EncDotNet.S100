using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Secom;

namespace EncDotNet.S100.Viewer.ViewModels.Keys;

/// <summary>The tabs of the Keys &amp; certificates page, in their order.</summary>
internal enum KeysTab
{
    /// <summary>MCP identities (certificate and private key).</summary>
    Identities,

    /// <summary>Roots trusted to vouch for servers and signers.</summary>
    Authorities,

    /// <summary>Part 15 system IDs (layout only this round).</summary>
    SystemIds,
}

/// <summary>
/// Settings → Keys &amp; certificates (#845): the MCP identities SoundCharts
/// presents, the authorities it trusts, and (later) the system IDs that open
/// protected charts. Rows, statuses and banners are recomputed from the two
/// stores whenever they change, when the page is shown, and once a day.
/// </summary>
internal sealed class KeysAndCertificatesViewModel : ViewModelBase, IDisposable
{
    /// <summary>A certificate ending within this many days is flagged.</summary>
    public const int EndingSoonDays = 30;

    private readonly SecomIdentityStore _identities;
    private readonly TrustedAuthorityStore _authorities;
    private readonly IKeysDialogs? _dialogs;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ITimer _daily;
    private KeysTab _selectedTab;
    private DateOnly _lastRefreshDay;

    public KeysAndCertificatesViewModel(
        SecomIdentityStore identities,
        TrustedAuthorityStore authorities,
        IKeysDialogs? dialogs = null,
        TimeProvider? time = null,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(authorities);
        _identities = identities;
        _authorities = authorities;
        _dialogs = dialogs;
        _time = time ?? TimeProvider.System;
        _post = post ?? (a => Avalonia.Threading.Dispatcher.UIThread.Post(a));

        ImportIdentityCommand = new RelayCommand(() => _ = ImportAsync(replacing: null));
        AddAuthorityCommand = new RelayCommand(() => _ = AddAuthorityAsync());
        UseIdentityCommand = new RelayCommand<string>(id => Use(id));
        SelectTabCommand = new RelayCommand<string>(tab =>
        {
            if (Enum.TryParse<KeysTab>(tab, out var parsed))
                SelectedTab = parsed;
        });

        _identities.Changed += OnStoreChanged;
        _identities.Trust.IdentityChanged += OnStoreChanged;
        _authorities.Changed += OnStoreChanged;

        // Recompute "ends in N days" when the date turns (H4).
        _daily = _time.CreateTimer(_ => _post(OnTick), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        Refresh();
    }

    /// <summary>The tab shown.</summary>
    public KeysTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
                OnPropertyChanged(nameof(SelectedTabIndex));
        }
    }

    /// <summary><see cref="SelectedTab"/> as a tab index.</summary>
    public int SelectedTabIndex
    {
        get => (int)_selectedTab;
        set
        {
            if (Enum.IsDefined((KeysTab)value))
                SelectedTab = (KeysTab)value;
        }
    }

    public ObservableCollection<KeysRow> IdentityRows { get; } = [];
    public ObservableCollection<KeysRow> AuthorityRows { get; } = [];

    /// <summary>Identities to switch to from the revoked banner: (id, name).</summary>
    public ObservableCollection<KeysRowAction> AlternativeIdentities { get; } = [];

    public string IdentitiesCount { get; private set; } = "0";
    public bool IdentitiesHasError { get; private set; }
    public string AuthoritiesCount { get; private set; } = "0";
    public bool AuthoritiesHasError { get; private set; }

    public bool HasIdentityRows => IdentityRows.Count > 0;
    public bool IsIdentitiesEmpty => IdentityRows.Count == 0;

    public bool HasRevokedBanner { get; private set; }
    public string RevokedBannerTitle { get; private set; } = "";
    public string RevokedBannerBody { get; private set; } = "";
    public bool HasAlternativeIdentities => AlternativeIdentities.Count > 0;

    public bool HasExpiredAuthorityBanner { get; private set; }
    public string ExpiredAuthorityBannerBody { get; private set; } = "";

    /// <summary>Where private keys stay, for the Identities note (C6).</summary>
    public string IdentitiesNote => string.Format(CultureInfo.CurrentCulture, Strings.Keys_Identities_NoteFormat, _identities.Keys.DisplayName);

    /// <summary>The line Settings shows when the identity needs attention (A2), or <see langword="null"/>.</summary>
    public string? StatusText { get; private set; }
    public bool HasStatus => StatusText is not null;
    public bool StatusIsDestructive { get; private set; }
    public bool StatusIsWarning => HasStatus && !StatusIsDestructive;

    public ICommand ImportIdentityCommand { get; }
    public ICommand AddAuthorityCommand { get; }
    public ICommand UseIdentityCommand { get; }
    public ICommand SelectTabCommand { get; }

    /// <summary>Raised after <see cref="Refresh"/> rebuilt the page.</summary>
    public event EventHandler? Refreshed;

    /// <summary>
    /// The page was shown: recompute, and check the identity in use for
    /// revocation in the background (H4).
    /// </summary>
    public void OnShown()
    {
        Refresh();
        _ = CheckRevocationAsync();
    }

    /// <summary>Checks the identity in use for revocation off the UI thread (nothing to do without one, or without a CRL checker).</summary>
    public Task CheckRevocationAsync() =>
        _identities.Trust is { Identity: not null, Revocation: not null }
            ? Task.Run(() => _identities.CheckRevocation()).ContinueWith(_ => _post(Refresh), TaskScheduler.Default)
            : Task.CompletedTask;

    /// <summary>Recomputes every row, count, banner and the status line.</summary>
    public void Refresh()
    {
        var now = _time.GetUtcNow();
        _lastRefreshDay = DateOnly.FromDateTime(now.LocalDateTime);
        BuildIdentities(now);
        BuildAuthorities(now);
        OnPropertyChanged(string.Empty);
        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes every stored identity and its key, before all settings are reset.</summary>
    public void RemoveAllIdentities() => _identities.RemoveAll();

    public void Dispose()
    {
        _daily.Dispose();
        _identities.Changed -= OnStoreChanged;
        _identities.Trust.IdentityChanged -= OnStoreChanged;
        _authorities.Changed -= OnStoreChanged;
    }

    private void OnStoreChanged(object? sender, EventArgs e) => _post(Refresh);

    private void OnTick()
    {
        var now = _time.GetUtcNow();
        if (DateOnly.FromDateTime(now.LocalDateTime) == _lastRefreshDay)
            return;
        Refresh();
        _ = CheckRevocationAsync();
    }

    private void BuildIdentities(DateTimeOffset now)
    {
        IdentityRows.Clear();
        AlternativeIdentities.Clear();
        var trust = _identities.Trust;
        var inUse = trust.Identity;

        // A run-only identity (command line, or a path given to the MCP server) is shown but not stored (H2).
        if (inUse is not null && !_identities.Identities.Any(r => string.Equals(r.Thumbprint, inUse.Certificate.Thumbprint, StringComparison.OrdinalIgnoreCase)))
            IdentityRows.Add(TemporaryRow(inUse, fromCommandLine: ReferenceEquals(inUse, _identities.CommandLineIdentity), now));
        else if (_identities.CommandLineIdentity is { } revokedCli && _identities.CommandLineRevokedAt is not null && inUse is null)
            IdentityRows.Add(TemporaryRow(revokedCli, fromCommandLine: true, now));

        SecomIdentityReference? revoked = null;
        foreach (var reference in _identities.Identities)
        {
            var isInUse = inUse is not null && string.Equals(reference.Thumbprint, inUse.Certificate.Thumbprint, StringComparison.OrdinalIgnoreCase);
            var chosen = string.Equals(_identities.InUseId, reference.Id, StringComparison.OrdinalIgnoreCase);
            IdentityRows.Add(StoredRow(reference, isInUse, chosen, now));
            if (reference.RevokedAt is not null && (revoked is null || reference.RevokedAt > revoked.RevokedAt))
                revoked = reference;
            if (!isInUse && reference.RevokedAt is null && reference.NotAfter > now)
                AlternativeIdentities.Add(new KeysRowAction(reference.Id, reference.DisplayName, UseIdentityCommand));
        }

        HasRevokedBanner = revoked is not null;
        if (revoked is not null)
        {
            RevokedBannerTitle = string.Format(CultureInfo.CurrentCulture, Strings.Keys_Revoked_TitleFormat, revoked.DisplayName);
            RevokedBannerBody = string.Format(CultureInfo.CurrentCulture, Strings.Keys_Revoked_BodyFormat,
                revoked.Issuer ?? Strings.Keys_UnknownIssuer, CertificateText.Date(revoked.RevokedAt!.Value));
        }

        IdentitiesHasError = revoked is not null || IdentityRows.Any(r => r.StatusTone == KeysTone.Destructive);
        IdentitiesCount = IdentitiesHasError ? "!" : IdentityRows.Count.ToString(CultureInfo.InvariantCulture);
        BuildStatus(inUse, revoked, now);
    }

    private void BuildStatus(SecomClientIdentity? inUse, SecomIdentityReference? revoked, DateTimeOffset now)
    {
        StatusText = null;
        StatusIsDestructive = false;
        if (revoked is not null)
        {
            StatusText = string.Format(CultureInfo.CurrentCulture, Strings.Keys_Revoked_TitleFormat, revoked.DisplayName);
            StatusIsDestructive = true;
            return;
        }

        if (inUse is null)
            return;
        var name = _identities.Identities.FirstOrDefault(r => string.Equals(r.Thumbprint, inUse.Certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? inUse.Subject;
        var days = CertificateText.DaysUntil(inUse.NotAfter, now);
        if (inUse.NotAfter <= now)
        {
            StatusText = string.Format(CultureInfo.CurrentCulture, Strings.Keys_StatusLine_EndedFormat, name);
            StatusIsDestructive = true;
        }
        else if (days <= EndingSoonDays)
        {
            StatusText = string.Format(CultureInfo.CurrentCulture, Strings.Keys_StatusLine_EndsFormat, name, CertificateText.EndsIn(days).ToLower(CultureInfo.CurrentCulture));
        }
    }

    private KeysRow StoredRow(SecomIdentityReference reference, bool isInUse, bool chosen, DateTimeOffset now)
    {
        var (status, tone) = reference.RevokedAt is not null
            ? (Strings.Keys_Status_Revoked, KeysTone.Destructive)
            : ValidityStatus(reference.NotBefore, reference.NotAfter, now);

        var badges = new List<KeysBadge> { KindBadge(reference.Kind) };
        if (isInUse)
            badges.Add(new KeysBadge(Strings.Keys_Badge_InUse, KeysTone.Success));

        var usage = isInUse ? Strings.Keys_Usage_InUse
            : chosen && _identities.CommandLineIdentity is not null ? Strings.Keys_Usage_Overridden
            : Strings.Keys_Usage_NotInUse;
        var sub = string.Format(CultureInfo.CurrentCulture, Strings.Keys_Identity_SubFormat,
            reference.Issuer ?? Strings.Keys_UnknownIssuer, CertificateText.Date(reference.NotAfter), usage);

        var actions = new List<KeysRowAction>
        {
            new("Details", Strings.Keys_Action_Details, new RelayCommand(() => ShowDetails(reference))),
            new("Replace", Strings.Keys_Action_Replace, new RelayCommand(() => _ = ImportAsync(reference.Id))),
        };
        if (isInUse || chosen)
            actions.Add(new("StopUsing", Strings.Keys_Action_StopUsing, new RelayCommand(() => Use(null))));
        else if (reference.RevokedAt is null && reference.NotAfter > now)
            actions.Add(new("Use", Strings.Keys_Action_Use, new RelayCommand(() => Use(reference.Id))));
        actions.Add(new("Remove", Strings.Keys_Action_Remove, new RelayCommand(() => ConfirmRemove(reference, isInUse)), IsDestructive: true));

        return new KeysRow
        {
            Id = reference.Id,
            Title = reference.DisplayName,
            Badges = badges,
            Status = status,
            StatusTone = tone,
            StatusIsRevoked = reference.RevokedAt is not null,
            Mono = reference.Mrn ?? reference.Subject,
            Sub = sub,
            Reference = reference.Id,
            Actions = actions,
        };
    }

    private KeysRow TemporaryRow(SecomClientIdentity identity, bool fromCommandLine, DateTimeOffset now)
    {
        var revoked = fromCommandLine && _identities.CommandLineRevokedAt is not null;
        var (status, tone) = revoked ? (Strings.Keys_Status_Revoked, KeysTone.Destructive) : ValidityStatus(identity.NotBefore, identity.NotAfter, now);
        return new KeysRow
        {
            Id = "temporary",
            Title = fromCommandLine ? Strings.Keys_Identity_FromCommandLine : Strings.Keys_Identity_FromMcp,
            Badges = [new KeysBadge(Strings.Keys_Badge_ThisRun, KeysTone.Neutral)],
            Status = status,
            StatusTone = tone,
            StatusIsRevoked = revoked,
            Mono = identity.Mrn ?? identity.Subject,
            Sub = string.Format(CultureInfo.CurrentCulture, Strings.Keys_Identity_TemporarySubFormat, identity.Issuer, CertificateText.Date(identity.NotAfter)),
        };
    }

    private static (string? Status, KeysTone Tone) ValidityStatus(DateTimeOffset notBefore, DateTimeOffset notAfter, DateTimeOffset now)
    {
        if (notAfter <= now)
            return (Strings.Keys_Status_Ended, KeysTone.Destructive);
        if (notBefore > now)
            return (Strings.Keys_Status_NotYetValid, KeysTone.Warning);
        var days = CertificateText.DaysUntil(notAfter, now);
        return days <= EndingSoonDays ? (CertificateText.EndsIn(days), KeysTone.Warning) : (null, KeysTone.Neutral);
    }

    private static KeysBadge KindBadge(string kind) => kind switch
    {
        "Vessel" => new KeysBadge(Strings.Keys_Kind_Vessel, KeysTone.Accent),
        "Organisation" => new KeysBadge(Strings.Keys_Kind_Organisation, KeysTone.Neutral),
        "Test" => new KeysBadge(Strings.Keys_Kind_Test, KeysTone.Warning),
        "Service" => new KeysBadge(Strings.Keys_Kind_Service, KeysTone.Neutral),
        "Person" => new KeysBadge(Strings.Keys_Kind_Person, KeysTone.Neutral),
        _ => new KeysBadge(Strings.Keys_Kind_Device, KeysTone.Neutral),
    };

    private void BuildAuthorities(DateTimeOffset now)
    {
        AuthorityRows.Clear();
        TrustedAuthority? expired = null;
        foreach (var authority in _authorities.Authorities)
        {
            var root = authority.Root;
            var notAfter = new DateTimeOffset(root.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            var notBefore = new DateTimeOffset(root.NotBefore.ToUniversalTime(), TimeSpan.Zero);
            var isExpired = notAfter <= now;
            if (isExpired && authority.Enabled)
                expired ??= authority;

            var badges = new List<KeysBadge>();
            if (authority.BuiltIn)
                badges.Add(new KeysBadge(Strings.Keys_Badge_BuiltIn, KeysTone.Neutral));
            badges.Add(new KeysBadge(Strings.Keys_Badge_Secom, KeysTone.Neutral));
            if (root.Subject.Contains("test", StringComparison.OrdinalIgnoreCase))
                badges.Add(new KeysBadge(Strings.Keys_Kind_Test, KeysTone.Warning));

            var origin = authority.BuiltIn ? Strings.Keys_Authority_BuiltInOrigin
                : string.Format(CultureInfo.CurrentCulture, Strings.Keys_Authority_AddedOriginFormat, CertificateText.Date(authority.AddedAt ?? now));
            var sub = string.Format(CultureInfo.CurrentCulture, Strings.Keys_Authority_SubFormat,
                CertificateText.Organisation(root), CertificateText.Date(notBefore), CertificateText.Date(notAfter), origin);

            var actions = new List<KeysRowAction> { new("View", Strings.Keys_Action_View, new RelayCommand(() => ShowDetails(authority))) };
            if (authority.BuiltIn)
            {
                actions.Add(authority.Enabled
                    ? new("TurnOff", Strings.Keys_Action_TurnOff, new RelayCommand(() => _authorities.SetEnabled(authority.Id, false)))
                    : new("TurnOn", Strings.Keys_Action_TurnOn, new RelayCommand(() => _authorities.SetEnabled(authority.Id, true))));
            }
            else
            {
                actions.Add(new("Remove", Strings.Keys_Action_Remove, new RelayCommand(() => ConfirmRemove(authority)), IsDestructive: true));
            }

            AuthorityRows.Add(new KeysRow
            {
                Id = authority.Id,
                Title = authority.Name,
                Badges = badges,
                Status = isExpired ? Strings.Keys_Status_Expired : authority.Enabled ? null : Strings.Keys_Status_Off,
                StatusTone = isExpired ? KeysTone.Destructive : KeysTone.Neutral,
                Mono = CertificateText.ShortFingerprint(root),
                Sub = sub,
                Actions = actions,
                IsDimmed = !authority.Enabled,
                IsAuthority = true,
            });
        }

        HasExpiredAuthorityBanner = expired is not null;
        ExpiredAuthorityBannerBody = expired is null ? "" : string.Format(CultureInfo.CurrentCulture, Strings.Keys_ExpiredAuthority_BodyFormat,
            expired.Name, CertificateText.Date(new DateTimeOffset(expired.Root.NotAfter.ToUniversalTime(), TimeSpan.Zero)));
        AuthoritiesHasError = expired is not null;
        AuthoritiesCount = AuthoritiesHasError ? "!" : AuthorityRows.Count.ToString(CultureInfo.InvariantCulture);
    }

    private void Use(string? id)
    {
        try
        {
            _identities.Use(id);
        }
        catch (InvalidOperationException)
        {
            // The row's state was stale (revoked or removed meanwhile); the refresh shows why.
            Refresh();
        }
    }

    private async Task ImportAsync(string? replacing)
    {
        if (_dialogs is null)
            return;
        var dialog = new ImportIdentityDialogViewModel(_identities, _dialogs.PickIdentityFileAsync, replacing, _time);
        dialog.Closed += (_, _) => _dialogs.Close(dialog);
        if (!await dialog.PickFileAsync())
            return;
        _dialogs.Show(dialog, 440);
    }

    private async Task AddAuthorityAsync()
    {
        if (_dialogs is null || await _dialogs.PickAuthorityFileAsync() is not { } path)
            return;
        var dialog = new AddAuthorityDialogViewModel(_authorities, path, _time);
        dialog.Closed += (_, _) => _dialogs.Close(dialog);
        _dialogs.Show(dialog, 480);
    }

    private void ConfirmRemove(SecomIdentityReference reference, bool isInUse)
    {
        var body = isInUse ? Strings.Keys_Remove_InUseBody : Strings.Keys_Remove_NotInUseBody;
        var footnote = string.Format(CultureInfo.CurrentCulture, Strings.Keys_Remove_FootnoteFormat, _identities.Keys.DisplayName);
        _dialogs?.Confirm(
            string.Format(CultureInfo.CurrentCulture, Strings.Keys_Remove_TitleFormat, reference.DisplayName),
            body + "\n\n" + footnote,
            Strings.Keys_Remove_Confirm,
            () => _identities.Remove(reference.Id));
    }

    private void ConfirmRemove(TrustedAuthority authority) =>
        _dialogs?.Confirm(
            string.Format(CultureInfo.CurrentCulture, Strings.Keys_RemoveAuthority_TitleFormat, authority.Name),
            Strings.Keys_RemoveAuthority_Body,
            Strings.Keys_RemoveAuthority_Confirm,
            () => _authorities.Remove(authority.Id));

    private void ShowDetails(SecomIdentityReference reference)
    {
        if (_dialogs is null)
            return;
        var fields = new List<KeysField>
        {
            new(Strings.Keys_Field_Name, reference.DisplayName),
            new(Strings.Keys_Field_Mrn, reference.Mrn ?? "—", IsMono: true),
            new(Strings.Keys_Field_Subject, reference.Subject ?? "—"),
            new(Strings.Keys_Field_IssuedBy, reference.Issuer ?? Strings.Keys_UnknownIssuer),
            new(Strings.Keys_Field_Valid, string.Format(CultureInfo.CurrentCulture, Strings.Keys_ValidRangeFormat,
                CertificateText.Date(reference.NotBefore), CertificateText.Date(reference.NotAfter))),
            new(Strings.Keys_Field_Thumbprint, reference.Thumbprint, IsMono: true),
            new(Strings.Keys_Field_Reference, reference.Id, IsMono: true),
            new(Strings.Keys_Field_KeyStore, _identities.Keys.DisplayName),
        };
        if (reference.RevokedAt is { } revokedAt)
            fields.Add(new(Strings.Keys_Field_Revoked, CertificateText.Date(revokedAt), Tone: KeysTone.Destructive));
        ShowDetails(reference.DisplayName, fields);
    }

    private void ShowDetails(TrustedAuthority authority)
    {
        var root = authority.Root;
        ShowDetails(authority.Name,
        [
            new(Strings.Keys_Field_Subject, root.Subject),
            new(Strings.Keys_Field_Organisation, CertificateText.Organisation(root)),
            new(Strings.Keys_Field_Valid, string.Format(CultureInfo.CurrentCulture, Strings.Keys_ValidRangeFormat,
                CertificateText.Date(new DateTimeOffset(root.NotBefore.ToUniversalTime(), TimeSpan.Zero)),
                CertificateText.Date(new DateTimeOffset(root.NotAfter.ToUniversalTime(), TimeSpan.Zero)))),
            new(Strings.Keys_Field_Sha256, CertificateText.Fingerprint(root), IsMono: true),
            new(Strings.Keys_Field_Key, CertificateText.KeyDescription(root)),
            new(Strings.Keys_Field_Origin, authority.BuiltIn ? Strings.Keys_Authority_BuiltInOrigin
                : string.Format(CultureInfo.CurrentCulture, Strings.Keys_Authority_AddedOriginFormat, CertificateText.Date(authority.AddedAt ?? _time.GetUtcNow()))),
        ]);
    }

    private void ShowDetails(string title, IReadOnlyList<KeysField> fields)
    {
        var dialog = new CertificateDetailsDialogViewModel(title, fields);
        dialog.Closed += (_, _) => _dialogs!.Close(dialog);
        _dialogs!.Show(dialog, 560);
    }
}
