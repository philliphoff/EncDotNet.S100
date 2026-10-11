using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Secom;

namespace EncDotNet.S100.Viewer.ViewModels.Secom;

/// <summary>
/// The Identity picker (#883 part B), shared by the add flow's Connection step
/// and a Library source's details: the closed value, a menu of <c>Default</c>,
/// <c>None</c> and each identity (with hints when the service asks), and
/// "Manage identities…". It only proposes: <see cref="Chosen"/> is raised when
/// the user picks an item, and nothing is ever picked for them.
/// </summary>
internal sealed class IdentityPickerViewModel : ViewModelBase
{
    private readonly SecomIdentityDirectory _directory;
    private readonly Action? _manage;
    private string _setting = SecomIdentitySetting.Default;
    private Func<string, SecomClientIdentity, SecomIdentityHint>? _hints;
    private bool _isOpen;

    /// <summary>Creates a picker over <paramref name="directory"/>.</summary>
    /// <param name="directory">The identities to choose from.</param>
    /// <param name="manage">Opens Settings → Keys &amp; certificates → Identities ("Manage identities…").</param>
    public IdentityPickerViewModel(SecomIdentityDirectory directory, Action? manage = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        _directory = directory;
        _manage = manage;
        OpenCommand = new RelayCommand(() => IsOpen = true);
        ManageCommand = new RelayCommand(() =>
        {
            IsOpen = false;
            _manage?.Invoke();
        }, () => _manage is not null);
        Refresh();
    }

    /// <summary>Raised with the setting the user picked (<c>default</c>, <c>none</c> or a reference id).</summary>
    public event EventHandler<string>? Chosen;

    /// <summary>The setting shown: <c>default</c>, <c>none</c> or a reference id.</summary>
    public string Setting
    {
        get => _setting;
        set
        {
            var canonical = SecomIdentitySetting.TryParse(value, out var parsed) ? parsed : SecomIdentitySetting.Default;
            if (SetProperty(ref _setting, canonical))
                Refresh();
        }
    }

    /// <summary>
    /// The hint for each identity, when the service asks for a certificate or a
    /// signed request; <see langword="null"/> shows none.
    /// </summary>
    public Func<string, SecomClientIdentity, SecomIdentityHint>? Hints
    {
        get => _hints;
        set
        {
            _hints = value;
            Refresh();
        }
    }

    /// <summary>True while the menu is open.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }

    /// <summary>The closed value's name (B1).</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The closed value's sub-line (B1).</summary>
    public string Sub { get; private set; } = string.Empty;

    /// <summary>True when the closed value presents no certificate (a globe icon rather than an id card).</summary>
    public bool IsAnonymous { get; private set; }

    /// <summary>The menu items, in order: Default, None, each identity.</summary>
    public ObservableCollection<IdentityPickerItemViewModel> Items { get; } = [];

    /// <summary>Opens the menu.</summary>
    public ICommand OpenCommand { get; }

    /// <summary>Opens the Identities tab.</summary>
    public ICommand ManageCommand { get; }

    /// <summary>The hint the setting resolves to (the Default item takes its identity's).</summary>
    public SecomIdentityHint HintFor(string setting) =>
        _directory.Resolve(setting) is { Identity: { } identity } entry && _hints is { } hints ? hints(entry.Id, identity) : SecomIdentityHint.None;

    /// <summary>Re-reads the identities, the default and the hints.</summary>
    public void Refresh()
    {
        Name = _directory.ClosedName(_setting);
        Sub = _directory.ClosedSub(_setting);
        IsAnonymous = _directory.Resolve(_setting) is null;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Sub));
        OnPropertyChanged(nameof(IsAnonymous));

        Items.Clear();
        var entries = _directory.Entries;
        if (entries.Count == 0)
        {
            // B4: only None, and the way to import one.
            Items.Add(Item(SecomIdentitySetting.None, Strings.Secom_Picker_None, Strings.Secom_Picker_NoIdentitiesSub, SecomIdentityHint.None, selected: true));
            return;
        }

        Items.Add(Item(SecomIdentitySetting.Default, _directory.DefaultName(), Strings.Secom_Picker_DefaultSub, HintFor(SecomIdentitySetting.Default)));
        Items.Add(Item(SecomIdentitySetting.None, Strings.Secom_Picker_None, Strings.Secom_Picker_NoneSub, SecomIdentityHint.None));
        foreach (var entry in entries)
            Items.Add(Item(entry.Id, entry.Name, _directory.ItemSub(entry), HintFor(entry.Id)));
    }

    private IdentityPickerItemViewModel Item(string setting, string name, string sub, SecomIdentityHint hint, bool? selected = null) =>
        new(setting, name, sub, hint, selected ?? string.Equals(setting, _setting, StringComparison.OrdinalIgnoreCase), Choose)
        {
            // B3: an identity from an issuer the service doesn't accept can't be chosen; Default stays choosable.
            IsEnabled = hint != SecomIdentityHint.IssuerNotAccepted || setting == SecomIdentitySetting.Default,
        };

    private void Choose(string setting)
    {
        IsOpen = false;
        Setting = setting;
        Chosen?.Invoke(this, _setting);
    }
}

/// <summary>One item of the Identity picker's menu.</summary>
internal sealed class IdentityPickerItemViewModel
{
    public IdentityPickerItemViewModel(string setting, string name, string sub, SecomIdentityHint hint, bool isSelected, Action<string> choose)
    {
        Setting = setting;
        Name = name;
        Sub = sub;
        Hint = hint;
        IsSelected = isSelected;
        ChooseCommand = new RelayCommand(() => choose(setting), () => IsEnabled);
    }

    /// <summary>The setting the item chooses.</summary>
    public string Setting { get; }

    public string Name { get; }

    public string Sub { get; }

    public SecomIdentityHint Hint { get; }

    /// <summary>"Issuer accepted", "Refused here", "Issuer not accepted", or null.</summary>
    public string? HintText => Hint switch
    {
        SecomIdentityHint.IssuerAccepted => Strings.Secom_Hint_IssuerAccepted,
        SecomIdentityHint.RefusedHere => Strings.Secom_Hint_RefusedHere,
        SecomIdentityHint.IssuerNotAccepted => Strings.Secom_Hint_IssuerNotAccepted,
        _ => null,
    };

    public bool HasHint => HintText is not null;

    public bool HintIsSuccess => Hint == SecomIdentityHint.IssuerAccepted;

    public bool HintIsDestructive => Hint == SecomIdentityHint.RefusedHere;

    public bool HintIsMuted => Hint == SecomIdentityHint.IssuerNotAccepted;

    /// <summary>True for the setting shown (marked ✓).</summary>
    public bool IsSelected { get; }

    /// <summary>False for an identity whose issuer the service doesn't accept.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>"Default · Bridge PC, Issuer accepted": the item's accessible help.</summary>
    public string HelpText => HasHint ? $"{Sub} · {HintText}" : Sub;

    public ICommand ChooseCommand { get; }
}
