using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Secom;

namespace EncDotNet.S100.Viewer.ViewModels.Keys;

/// <summary>A choice of new default when the default identity is removed (#883 F2): an identity, or None.</summary>
internal sealed class NewDefaultChoice : ViewModelBase
{
    private bool _isSelected;

    public NewDefaultChoice(string? id, string label, Action<NewDefaultChoice> select)
    {
        Id = id;
        Label = label;
        SelectCommand = new RelayCommand(() => select(this));
    }

    /// <summary>The identity's reference id, or <see langword="null"/> for None.</summary>
    public string? Id { get; }

    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ICommand SelectCommand { get; }
}

/// <summary>What removing an identity does to one source (#883 F3).</summary>
/// <param name="Source">The source's name.</param>
/// <param name="Outcome">"switches to None", or "Default · switches to {new default | None}".</param>
internal sealed record RemoveIdentityEffect(string Source, string Outcome);

/// <summary>
/// The Remove identity confirm (#883 F): says how many sources use the
/// identity and what happens to each, and, when it is the default, asks for
/// a new default first. On confirm, sources set to it switch to None, the new
/// default is set, the key-store entry is deleted, and the affected sources
/// are read again.
/// </summary>
internal sealed class RemoveIdentityDialogViewModel : ViewModelBase
{
    private readonly SecomIdentityStore _store;
    private readonly SecomIdentityReference _identity;
    private readonly IReadOnlyList<SecomIdentityUser> _users;

    public RemoveIdentityDialogViewModel(SecomIdentityStore store, SecomIdentityReference identity, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(identity);
        _store = store;
        _identity = identity;
        IsDefault = string.Equals(store.DefaultId, identity.Id, StringComparison.OrdinalIgnoreCase);
        _users = store.Library is { } library
            ? SecomIdentityUsage.UsersOf(library.Collections, identity.Id, store.DefaultId)
            : [];

        // F2: the other identities that can be the default (not revoked, not ended), then None; the first preselected.
        var now = (time ?? TimeProvider.System).GetUtcNow();
        if (IsDefault)
        {
            var choices = store.Identities
                .Where(r => !string.Equals(r.Id, identity.Id, StringComparison.OrdinalIgnoreCase) && r.RevokedAt is null && r.NotAfter > now)
                .Select(r => new NewDefaultChoice(r.Id, r.DisplayName, Select))
                .Append(new NewDefaultChoice(null, Strings.Secom_Identity_None, Select))
                .ToArray();
            NewDefaults = choices;
            choices[0].IsSelected = true;
        }

        RemoveCommand = new RelayCommand(Remove);
        CancelCommand = new RelayCommand(() => Closed?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Raised when the dialog should close.</summary>
    public event EventHandler? Closed;

    /// <summary>"Remove Bridge PC?"</summary>
    public string Title => string.Format(CultureInfo.CurrentCulture, Strings.Keys_RemoveIdentity_TitleFormat, _identity.DisplayName);

    /// <summary>F1: how many sources use it; for the default, that a new default comes first.</summary>
    public string Body
    {
        get
        {
            var count = _users.Count switch
            {
                0 => Strings.Keys_RemoveIdentity_NoSources,
                1 => Strings.Keys_RemoveIdentity_OneSource,
                var n => string.Format(CultureInfo.CurrentCulture, Strings.Keys_RemoveIdentity_SourcesFormat, n),
            };
            return IsDefault ? count + " " + Strings.Keys_RemoveIdentity_DefaultSuffix : count;
        }
    }

    /// <summary>True when the identity is the default (a new one is chosen first).</summary>
    public bool IsDefault { get; }

    /// <summary>F2: the new default's choices; empty unless <see cref="IsDefault"/>.</summary>
    public IReadOnlyList<NewDefaultChoice> NewDefaults { get; } = [];

    /// <summary>F3: one line per affected source.</summary>
    public IReadOnlyList<RemoveIdentityEffect> Effects => [.. _users.Select(u => new RemoveIdentityEffect(
        u.Definition.DisplayName ?? u.Definition.ServiceUri.Host,
        u.ViaDefault
            ? string.Format(CultureInfo.CurrentCulture, Strings.Keys_RemoveIdentity_ViaDefaultFormat,
                NewDefaults.FirstOrDefault(c => c.IsSelected)?.Label ?? Strings.Secom_Identity_None)
            : Strings.Keys_RemoveIdentity_SwitchesToNone))];

    public bool HasEffects => _users.Count > 0;

    /// <summary>F4: the round-1 footnote, naming the key store.</summary>
    public string Footnote => string.Format(CultureInfo.CurrentCulture, Strings.Keys_Remove_FootnoteFormat, _store.Keys.DisplayName);

    public ICommand RemoveCommand { get; }

    public ICommand CancelCommand { get; }

    private void Select(NewDefaultChoice choice)
    {
        foreach (var other in NewDefaults)
            other.IsSelected = ReferenceEquals(other, choice);
        OnPropertyChanged(nameof(Effects));
    }

    private void Remove()
    {
        _store.Remove(_identity.Id, NewDefaults.FirstOrDefault(c => c.IsSelected)?.Id);
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
