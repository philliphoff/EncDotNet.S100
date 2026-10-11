using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Secom;

namespace EncDotNet.S100.Viewer.ViewModels.Secom;

/// <summary>A field in a SECOM source's Connection details: "Reads as", "Datasets", "Setting", "Reason".</summary>
/// <param name="Label">The field's label.</param>
/// <param name="Value">Its value.</param>
/// <param name="IsMono">True to show it in mono (the stored setting).</param>
/// <param name="Tone">The tone of a vocabulary word, or neutral.</param>
internal sealed record SecomDetailField(string Label, string Value, bool IsMono = false, SecomTone Tone = SecomTone.Neutral)
{
    public bool IsSuccess => Tone == SecomTone.Success;

    public bool IsWarning => Tone == SecomTone.Warning;

    public bool IsDestructive => Tone == SecomTone.Destructive;
}

/// <summary>
/// A SECOM source's details in the Library (#883 D3, D4): its Connection
/// group, with the Identity picker, what it presented and the outcome, the
/// fields (reads as, datasets, the stored setting, why its server isn't
/// trusted), and the row's action repeated. Choosing an identity updates the
/// source's setting, which re-reads that source only.
/// </summary>
internal sealed class SecomSourceDetailsViewModel : ViewModelBase
{
    private readonly SecomIdentityDirectory _identities;
    private readonly Action<Keys.KeysTab>? _openKeys;
    private readonly Func<Guid, CollectionSource, bool> _update;
    private readonly TimeProvider _time;
    private LibraryCollection? _collection;
    private LibrarySource? _source;
    private (Guid Source, DateTimeOffset At)? _rereading;

    /// <summary>Creates the details.</summary>
    /// <param name="identities">The identities to choose from.</param>
    /// <param name="update">Replaces a source's definition in its collection (<see cref="CollectionLibrary.UpdateSource"/>).</param>
    /// <param name="openKeys">Opens Settings → Keys &amp; certificates on a tab.</param>
    /// <param name="time">The clock.</param>
    public SecomSourceDetailsViewModel(
        SecomIdentityDirectory identities, Func<Guid, CollectionSource, bool> update, Action<Keys.KeysTab>? openKeys = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(update);
        _identities = identities;
        _update = update;
        _openKeys = openKeys;
        _time = time ?? TimeProvider.System;
        Picker = new IdentityPickerViewModel(identities, openKeys is null ? null : () => openKeys(Keys.KeysTab.Identities));
        Picker.Chosen += (_, setting) => Choose(setting);
        ActionCommand = new RelayCommand(RunAction, () => ActionKind is not null);
    }

    /// <summary>The source shown, or null.</summary>
    public Guid? SourceId => _source?.Id;

    /// <summary>The source's name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The service's host, in mono.</summary>
    public string Host { get; private set; } = string.Empty;

    /// <summary>The Identity picker.</summary>
    public IdentityPickerViewModel Picker { get; }

    /// <summary>What it presented when last read, and the outcome (D3); null before it was read.</summary>
    public string? PresentationLine { get; private set; }

    public SecomTone PresentationTone { get; private set; }

    public bool HasPresentationLine => PresentationLine is not null;

    public bool PresentationIsSuccess => PresentationTone == SecomTone.Success;

    public bool PresentationIsWarning => PresentationTone == SecomTone.Warning;

    public bool PresentationIsDestructive => PresentationTone == SecomTone.Destructive;

    /// <summary>"Re-read with SoundCharts organisation · 36 datasets", after a change of identity read fine (D4).</summary>
    public string? RereadNote { get; private set; }

    public bool HasRereadNote => RereadNote is not null;

    /// <summary>Reads as, Datasets, Setting and (for an untrusted server) Reason.</summary>
    public IReadOnlyList<SecomDetailField> Fields { get; private set; } = [];

    /// <summary>The row's action, repeated (not "Manage identities…").</summary>
    internal SecomFix? ActionKind { get; private set; }

    public string? ActionText => ActionKind switch
    {
        SecomFix.ChooseIdentity => Strings.Secom_Action_ChooseIdentity,
        SecomFix.ChooseAnotherIdentity => Strings.Secom_Action_ChooseAnotherIdentity,
        SecomFix.SetUpIdentity => Strings.Keys_Fix_SetUpIdentity,
        SecomFix.TrustedAuthorities => Strings.Keys_Fix_TrustedAuthorities,
        _ => null,
    };

    public bool HasAction => ActionText is not null;

    public ICommand ActionCommand { get; }

    /// <summary>Shows <paramref name="source"/> of <paramref name="collection"/>, or (with nulls) nothing.</summary>
    public void Show(LibraryCollection? collection, LibrarySource? source)
    {
        _collection = collection;
        _source = source?.Definition is SecomSource ? source : null;
        Refresh();
    }

    /// <summary>Re-reads the source's state and the identities.</summary>
    public void Refresh()
    {
        if (_source is not { Definition: SecomSource secom } source || _collection is null)
        {
            Fields = [];
            RaiseAll();
            return;
        }

        Name = LibraryNodeText.SourceName(source, _collection);
        Host = secom.ServiceUri.Host;
        Picker.Setting = secom.Identity;

        var presentation = source.Index?.Presentation;
        var state = SecomConnection.FromPresentation(presentation);
        var asks = presentation?.ServiceAsks == true || state is { } s && SecomConnection.AsksForIdentity(s);
        Picker.Hints = asks && presentation is not null ? (id, identity) => presentation.HintFor(id, identity, _identities.Anchors) : null;
        Picker.Refresh();

        (PresentationLine, PresentationTone) = Presentation(presentation);
        var count = source.Index?.Items.Count ?? 0;
        var datasets = count switch
        {
            0 => Strings.Secom_Row_NoDatasetsYet,
            1 => Strings.Secom_Row_OneDataset,
            _ => string.Format(CultureInfo.CurrentCulture, Strings.Secom_Row_DatasetsFormat, count),
        };

        var fields = new List<SecomDetailField>();
        if (state is { } known)
            fields.Add(new SecomDetailField(Strings.Secom_Details_ReadsAs, SecomConnection.Word(known), Tone: SecomConnection.Tone(known)));
        fields.Add(new SecomDetailField(Strings.Secom_Details_Datasets, datasets));
        fields.Add(new SecomDetailField(Strings.Secom_Details_Setting, string.Format(CultureInfo.InvariantCulture, Strings.Secom_Details_SettingFormat, secom.Identity), IsMono: true));
        if (state == SecomConnectionState.ServerUntrusted && SecomConnection.UntrustedReason(presentation?.ServerTrust, presentation?.ServerAnchor) is { } reason)
            fields.Add(new SecomDetailField(Strings.Secom_Details_Reason, reason));
        Fields = fields;

        var code = source.Index is { } index ? LibraryNodeText.SecomCode(index) : null;
        ActionKind = code switch
        {
            SecomDiagnosticCodes.NeedsCertificate or SecomDiagnosticCodes.NeedsSignedRequest =>
                _identities.HasIdentities ? SecomFix.ChooseIdentity : SecomFix.SetUpIdentity,
            SecomDiagnosticCodes.CertificateRefused or SecomDiagnosticCodes.SignedRequestRefused => SecomFix.ChooseAnotherIdentity,
            SecomDiagnosticCodes.ServerUntrusted when SecomConnection.TrustCanFix(presentation?.ServerTrust) => SecomFix.TrustedAuthorities,
            _ => null,
        };

        // D4: once the source has been read again after a change of identity, and reads.
        RereadNote = _rereading is { } rereading && rereading.Source == source.Id && source.Index is { } read
            && read.IndexedAt >= rereading.At && state is { } readState && SecomConnection.IsReadable(readState)
                ? string.Format(CultureInfo.CurrentCulture, Strings.Secom_Details_RereadFormat,
                    _identities.Resolve(secom.Identity)?.Name ?? Strings.Secom_Details_NoCertificate, datasets)
                : null;
        RaiseAll();
    }

    /// <summary>Opens the picker ("Choose identity…" from the row).</summary>
    public void OpenPicker() => Picker.IsOpen = true;

    private (string?, SecomTone) Presentation(SecomPresentation? presentation)
    {
        if (presentation is null)
            return (null, SecomTone.Neutral);
        var name = presentation.IdentityId is { } id ? _identities.Find(id)?.Name ?? presentation.Presented ?? id : presentation.Presented ?? string.Empty;
        return presentation.Outcome switch
        {
            SecomPresentationOutcome.Refused => (
                string.Format(CultureInfo.CurrentCulture, Strings.Secom_Presented_RefusedFormat, name,
                    presentation.At.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.CurrentCulture)),
                SecomTone.Destructive),
            SecomPresentationOutcome.Accepted => (string.Format(CultureInfo.CurrentCulture, Strings.Secom_Presented_AcceptedFormat, name), SecomTone.Success),
            SecomPresentationOutcome.NotPresented => (Strings.Secom_Presented_NonePresented, SecomTone.Warning),
            SecomPresentationOutcome.Stopped => (Strings.Secom_Presented_Stopped, SecomTone.Warning),
            SecomPresentationOutcome.NotAsked => (Strings.Secom_Presented_NotAsked, SecomTone.Neutral),
            _ => (null, SecomTone.Neutral),
        };
    }

    /// <summary>Saves the chosen setting on the source: the Library re-reads that source only (A5).</summary>
    private void Choose(string setting)
    {
        if (_source is not { Definition: SecomSource secom } || _collection is null
            || string.Equals(secom.Identity, setting, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _rereading = (secom.Id, _time.GetUtcNow());
        _update(_collection.Id, secom with { Identity = setting });
    }

    private void RunAction()
    {
        switch (ActionKind)
        {
            case SecomFix.ChooseIdentity or SecomFix.ChooseAnotherIdentity:
                OpenPicker();
                break;
            case SecomFix.SetUpIdentity:
                _openKeys?.Invoke(Keys.KeysTab.Identities);
                break;
            case SecomFix.TrustedAuthorities:
                _openKeys?.Invoke(Keys.KeysTab.Authorities);
                break;
        }
    }

    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(SourceId), nameof(Name), nameof(Host), nameof(PresentationLine), nameof(PresentationTone), nameof(HasPresentationLine),
            nameof(PresentationIsSuccess), nameof(PresentationIsWarning), nameof(PresentationIsDestructive), nameof(RereadNote),
            nameof(HasRereadNote), nameof(Fields), nameof(ActionText), nameof(HasAction),
        })
        {
            OnPropertyChanged(name);
        }

        ((RelayCommand)ActionCommand).NotifyCanExecuteChanged();
    }
}
