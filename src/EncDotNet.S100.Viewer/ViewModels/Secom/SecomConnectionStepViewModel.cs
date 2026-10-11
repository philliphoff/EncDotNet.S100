using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Secom;

namespace EncDotNet.S100.Viewer.ViewModels.Secom;

/// <summary>An action button in a SECOM status block ("Choose identity…", "Try again").</summary>
/// <param name="Label">The button text.</param>
/// <param name="AutomationId">The automation id.</param>
/// <param name="Command">What it does.</param>
internal sealed record SecomBlockAction(string Label, string AutomationId, ICommand Command);

/// <summary>
/// Step 2 of the Add online catalogue wizard for a SECOM service (#883 C): the
/// service card, the Identity picker and the status block for how the service
/// reads with the chosen identity, re-probed whenever the identity changes.
/// A service that can't be read can still be added ("Add and wait").
/// </summary>
internal sealed class SecomConnectionStepViewModel : ViewModelBase
{
    private readonly KnownCatalogueSource _source;
    private readonly SecomIdentityDirectory _identities;
    private readonly Func<Uri, string?, CancellationToken, Task<SecomProbeResult>> _probe;
    private readonly Action<Keys.KeysTab>? _openKeys;
    private readonly HashSet<string> _refused = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _probing;
    private SecomProbeResult? _result;
    private bool _isProbing;

    /// <summary>Creates the step for <paramref name="source"/>.</summary>
    /// <param name="source">The SECOM service chosen in step 1.</param>
    /// <param name="identities">The identities to choose from.</param>
    /// <param name="probe">Probes the service with an identity setting.</param>
    /// <param name="openKeys">Opens Settings → Keys &amp; certificates on a tab (the wizard closes first).</param>
    public SecomConnectionStepViewModel(
        KnownCatalogueSource source,
        SecomIdentityDirectory identities,
        Func<Uri, string?, CancellationToken, Task<SecomProbeResult>> probe,
        Action<Keys.KeysTab>? openKeys = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(probe);
        _source = source;
        _identities = identities;
        _probe = probe;
        _openKeys = openKeys;
        // C2: a new source starts on Default.
        Picker = new IdentityPickerViewModel(identities, openKeys is null ? null : () => openKeys(Keys.KeysTab.Identities))
        {
            Setting = SecomIdentitySetting.Default,
        };
        Picker.Chosen += (_, _) => _ = ProbeAsync();
        TryAgainCommand = new AsyncRelayCommand(ProbeAsync);
    }

    /// <summary>Raised when the state changes, so the wizard's footer follows.</summary>
    public event EventHandler? StateChanged;

    /// <summary>The service's name, e.g. "KHRA S-124 navigational warnings".</summary>
    public string ServiceName => _source.Name;

    /// <summary>"SECOM v2 · " before the host.</summary>
    public string ProtocolText => Strings.Secom_Connection_Protocol;

    /// <summary>The service's host, shown in mono.</summary>
    public string Host => _source.CatalogUri.Host;

    /// <summary>The service's MRN from the registry, if known.</summary>
    public string? Mrn => _source.ServiceMrn;

    public bool HasMrn => Mrn is not null;

    /// <summary>The products the service is known for (its directory entry): what "Add and wait" scopes to.</summary>
    public IReadOnlyList<string> Products => _source.Product is { } product ? [product] : [];

    /// <summary>The Identity picker.</summary>
    public IdentityPickerViewModel Picker { get; }

    /// <summary>The identity setting chosen.</summary>
    public string IdentitySetting => Picker.Setting;

    /// <summary>True while the service is being probed.</summary>
    public bool IsProbing
    {
        get => _isProbing;
        private set
        {
            if (SetProperty(ref _isProbing, value))
            {
                OnPropertyChanged(nameof(HasState));
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>The state, once probed.</summary>
    public SecomConnectionState? State => _result is null ? null : SecomConnection.FromProbe(_result);

    /// <summary>True when the state is known and not being re-probed.</summary>
    public bool HasState => State is not null && !IsProbing;

    /// <summary>True when the service reads as things stand: the footer offers Continue, otherwise Add and wait.</summary>
    public bool IsReadable => State is { } state && SecomConnection.IsReadable(state);

    /// <summary>The footer's primary label: "Continue" or "Add and wait".</summary>
    public string PrimaryLabel => IsReadable ? Strings.Secom_Connection_Continue : Strings.Secom_Connection_AddAndWait;

    /// <summary>The directory tag: the vocabulary word.</summary>
    public string? TagText => IsProbing ? Strings.Library_Reachability_Checking : State is { } state ? SecomConnection.Word(state) : null;

    public SecomTone Tone => State is { } state ? SecomConnection.Tone(state) : SecomTone.Neutral;

    public bool IsSuccess => HasState && Tone == SecomTone.Success;

    public bool IsWarning => HasState && Tone == SecomTone.Warning;

    public bool IsDestructive => HasState && Tone == SecomTone.Destructive;

    public bool IsNeutral => !HasState || Tone == SecomTone.Neutral;

    /// <summary>The status block's title (C3).</summary>
    public string? Title => State is { } state ? SecomConnection.Word(state) : null;

    /// <summary>The status block's body (C3).</summary>
    public string? Body { get; private set; }

    /// <summary>"Presented:", "Signed by:" or "Reason:", for the quote line; null for none.</summary>
    public string? NoteLabel { get; private set; }

    /// <summary>The quote line's value: an MRN or a reason.</summary>
    public string? Note { get; private set; }

    public bool HasNote => Note is not null;

    /// <summary>The block's actions, right-aligned.</summary>
    public IReadOnlyList<SecomBlockAction> Actions { get; private set; } = [];

    public bool HasActions => Actions.Count > 0;

    /// <summary>For a waiting state: "Add and wait adds … It lists automatically once …"; null when readable.</summary>
    public string? WaitNote { get; private set; }

    public bool HasWaitNote => WaitNote is not null;

    /// <summary>Probes again with the current setting.</summary>
    public ICommand TryAgainCommand { get; }

    /// <summary>Probes the service with the chosen identity (C4), cancelling any probe still running.</summary>
    public async Task ProbeAsync()
    {
        _probing?.Cancel();
        var probing = _probing = new CancellationTokenSource();
        IsProbing = true;
        RaiseAll();
        SecomProbeResult result;
        try
        {
            result = await _probe(_source.CatalogUri, Picker.Setting, probing.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (probing.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or TaskCanceledException)
        {
            result = new SecomProbeResult(SecomReachability.Unreachable, ex.Message);
        }

        if (!ReferenceEquals(probing, _probing))
            return;
        _result = result;
        if (result is { Reachability: SecomReachability.CertificateRefused, IdentityId: { } refused })
            _refused.Add(refused);
        IsProbing = false;
        Build();
        RaiseAll();
    }

    /// <summary>Re-reads the identities (one was imported or removed while the wizard was open).</summary>
    public void RefreshIdentities()
    {
        Picker.Refresh();
        Build();
        RaiseAll();
    }

    private void Build()
    {
        var state = State;
        var provider = SecomConnection.ProviderName(_source.Provider);
        var identity = _result?.IdentityId is { } id ? _identities.Find(id) : _identities.Resolve(Picker.Setting);
        var name = identity?.Name ?? _result?.Identity ?? string.Empty;
        var mrn = _result?.Identity ?? identity?.Mrn;
        var hasIdentities = _identities.HasIdentities;
        var asks = state is { } s && SecomConnection.AsksForIdentity(s);

        // B3: hints only when the service asks; the Default item takes its identity's.
        Picker.Hints = asks ? Hint : null;
        var acceptable = asks
            ? _identities.Entries.Where(e => e.Identity is { } i && Hint(e.Id, i) is SecomIdentityHint.IssuerAccepted or SecomIdentityHint.None).ToArray()
            : [];

        (Body, NoteLabel, Note, Actions) = state switch
        {
            SecomConnectionState.Readable => (Strings.Secom_Block_ReadableBody, null, null, []),
            SecomConnectionState.ReadableWithCertificate => (
                string.Format(CultureInfo.CurrentCulture, Strings.Secom_Block_ReadableWithCertificateBodyFormat, provider, name, _source.Product ?? ServiceName),
                Strings.Secom_Block_Presented, mrn, []),
            SecomConnectionState.NeedsCertificate => hasIdentities
                ? (string.Format(CultureInfo.CurrentCulture, Strings.Secom_Block_NeedsCertificateBodyFormat, ServiceName, acceptable.Length, provider), null, null,
                    [acceptable.Length == 1 ? Use(acceptable[0]) : ChooseIdentity()])
                : (string.Format(CultureInfo.CurrentCulture, Strings.Secom_Block_NeedsCertificateNoIdentitiesBodyFormat, ServiceName), null, null, [ImportIdentity()]),
            SecomConnectionState.NeedsSignedRequest => hasIdentities
                ? (string.Format(CultureInfo.CurrentCulture, Strings.Secom_Block_NeedsSignedRequestBodyFormat, ServiceName), null, null, [ChooseIdentity()])
                : (string.Format(CultureInfo.CurrentCulture, Strings.Secom_Block_NeedsSignedRequestNoIdentitiesBodyFormat, ServiceName), null, null, [ImportIdentity()]),
            SecomConnectionState.CertificateRefused => (
                string.Format(CultureInfo.CurrentCulture, Strings.Secom_Block_CertificateRefusedBodyFormat, provider, name),
                Strings.Secom_Block_Presented, mrn, [ChooseAnother()]),
            SecomConnectionState.SignedRequestRefused => (
                string.Format(CultureInfo.CurrentCulture, Strings.Secom_Block_SignedRequestRefusedBodyFormat, provider, name),
                Strings.Secom_Block_SignedBy, mrn, [ChooseAnother()]),
            SecomConnectionState.ServerUntrusted => (
                Strings.Secom_Block_ServerUntrustedBody,
                Strings.Secom_Block_Reason,
                SecomConnection.UntrustedReason(_result?.ServerTrust?.Outcome, _result?.ServerTrust?.Anchor) ?? _result?.Detail,
                SecomConnection.TrustCanFix(_result?.ServerTrust?.Outcome) && _openKeys is not null ? [TrustedAuthorities()] : []),
            SecomConnectionState.NotReachable => (
                string.Format(CultureInfo.CurrentCulture, Strings.Secom_Block_NotReachableBodyFormat, Host), null, null,
                [new SecomBlockAction(Strings.Wizard_TryAgain, "Wizard.Connection.TryAgain", TryAgainCommand)]),
            _ => ((string?)null, (string?)null, (string?)null, (IReadOnlyList<SecomBlockAction>)[]),
        };

        // C5: what Add and wait waits for.
        var fix = state switch
        {
            SecomConnectionState.NeedsCertificate or SecomConnectionState.NeedsSignedRequest =>
                hasIdentities ? Strings.Secom_Wait_ChooseIdentity : Strings.Secom_Wait_ImportIdentity,
            SecomConnectionState.CertificateRefused or SecomConnectionState.SignedRequestRefused =>
                string.Format(CultureInfo.CurrentCulture, Strings.Secom_Wait_AcceptedIdentityFormat, provider),
            SecomConnectionState.ServerUntrusted => Strings.Secom_Wait_TrustServer,
            SecomConnectionState.NotReachable => Strings.Secom_Wait_Reachable,
            _ => null,
        };
        WaitNote = fix is null ? null : string.Format(CultureInfo.CurrentCulture, Strings.Secom_Wait_NoteFormat, ServiceName, fix);
    }

    private SecomIdentityHint Hint(string id, SecomClientIdentity identity) =>
        SecomIdentityHints.Judge(
            id, identity, _identities.Anchors, _result?.Handshake?.AcceptableIssuers ?? [], _result?.ServerTrust?.Anchor, _refused, _source.ServiceMrn);

    private SecomBlockAction ChooseIdentity() =>
        new(Strings.Secom_Action_ChooseIdentity, "Wizard.Connection.ChooseIdentity", Picker.OpenCommand);

    private SecomBlockAction ChooseAnother() =>
        new(Strings.Secom_Action_ChooseAnotherIdentity, "Wizard.Connection.ChooseAnother", Picker.OpenCommand);

    private SecomBlockAction Use(SecomIdentityEntry entry) =>
        new(string.Format(CultureInfo.CurrentCulture, Strings.Secom_Action_UseFormat, entry.Name), "Wizard.Connection.Use", new RelayCommand(() =>
        {
            // The user's explicit choice of the one acceptable identity.
            Picker.Setting = entry.Id;
            _ = ProbeAsync();
        }));

    private SecomBlockAction ImportIdentity() =>
        new(Strings.Secom_Action_ImportIdentity, "Wizard.Connection.Import", new RelayCommand(() => _openKeys?.Invoke(Keys.KeysTab.Identities), () => _openKeys is not null));

    private SecomBlockAction TrustedAuthorities() =>
        new(Strings.Keys_Fix_TrustedAuthorities, "Wizard.Connection.TrustedAuthorities", new RelayCommand(() => _openKeys?.Invoke(Keys.KeysTab.Authorities)));

    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(State), nameof(HasState), nameof(IsReadable), nameof(PrimaryLabel), nameof(TagText), nameof(Tone),
            nameof(IsSuccess), nameof(IsWarning), nameof(IsDestructive), nameof(IsNeutral), nameof(Title), nameof(Body),
            nameof(NoteLabel), nameof(Note), nameof(HasNote), nameof(Actions), nameof(HasActions), nameof(WaitNote),
            nameof(HasWaitNote), nameof(IdentitySetting),
        })
        {
            OnPropertyChanged(name);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
