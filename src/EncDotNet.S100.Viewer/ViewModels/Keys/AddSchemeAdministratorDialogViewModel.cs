using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Secom;

namespace EncDotNet.S100.Viewer.ViewModels.Keys;

/// <summary>
/// Add a Scheme Administrator root from a DER or PEM file: shows each root it
/// holds, with its full SHA-256 to check against the one the Scheme
/// Administrator publishes, and asks for the name badges report.
/// </summary>
internal sealed class AddSchemeAdministratorDialogViewModel : ViewModelBase
{
    private readonly SchemeAdministratorStore _store;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<X509Certificate2> _roots = [];
    private readonly bool _blocked;
    private string _name = "";

    public AddSchemeAdministratorDialogViewModel(
        SchemeAdministratorStore store,
        TrustedAuthorityStore? secomAuthorities,
        string path,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentException.ThrowIfNullOrEmpty(path);
        _store = store;
        _time = time;
        FileName = Path.GetFileName(path);

        try
        {
            _roots = SchemeAdministratorRoots.LoadFile(path);
        }
        catch (SchemeAdministratorRootException ex)
        {
            Error = ex.Error switch
            {
                SchemeAdministratorRootError.Unreadable => Strings.Keys_AddSchemeAdmin_CannotRead,
                SchemeAdministratorRootError.NoCertificate => Strings.Keys_AddSchemeAdmin_NoCertificate,
                SchemeAdministratorRootError.NotCertificateAuthority => string.Format(
                    CultureInfo.CurrentCulture, Strings.Keys_AddSchemeAdmin_NotCaFormat, ex.CertificateName ?? ex.CertificateSubject),
                _ => Strings.Keys_AddSchemeAdmin_NotCertificate,
            };
            _blocked = true;
        }

        var now = time.GetUtcNow();
        var certificates = new List<IReadOnlyList<KeysField>>();
        foreach (var root in _roots)
        {
            certificates.Add(FieldsOf(root));
            var name = root.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            if (_store.Contains(root))
            {
                Error ??= string.Format(CultureInfo.CurrentCulture, Strings.Keys_AddSchemeAdmin_DuplicateFormat, name);
                _blocked = true;
            }
            else if (root.NotAfter.ToUniversalTime() <= now.UtcDateTime)
            {
                Error ??= Strings.Keys_AddSchemeAdmin_Expired;
                _blocked = true;
            }
            else if (root.NotBefore.ToUniversalTime() > now.UtcDateTime)
            {
                Warning ??= string.Format(CultureInfo.CurrentCulture, Strings.Keys_AddSchemeAdmin_NotYetValidFormat,
                    CertificateText.Date(new DateTimeOffset(root.NotBefore.ToUniversalTime(), TimeSpan.Zero)));
            }
            else if (secomAuthorities?.Authorities.Any(a => a.Root.RawData.AsSpan().SequenceEqual(root.RawData)) == true)
            {
                Warning ??= string.Format(CultureInfo.CurrentCulture, Strings.Keys_AddSchemeAdmin_SecomDuplicateFormat, name);
            }
        }

        Certificates = certificates;
        if (_roots.Count == 1)
            _name = _roots[0].GetNameInfo(X509NameType.SimpleName, forIssuer: false);

        AddCommand = new RelayCommand(Add, () => CanAdd);
        CancelCommand = new RelayCommand(() => Closed?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Raised when the dialog should close.</summary>
    public event EventHandler? Closed;

    public string FileName { get; }

    /// <summary>One field card per certificate in the file.</summary>
    public IReadOnlyList<IReadOnlyList<KeysField>> Certificates { get; }

    public bool HasCertificates => _roots.Count > 0;

    /// <summary>A single root is named here; a bundle's roots take their common names.</summary>
    public bool ShowsName => _roots.Count == 1;

    public bool IsBundle => _roots.Count > 1;

    public string BundleText => string.Format(CultureInfo.CurrentCulture, Strings.Keys_AddSchemeAdmin_BundleFormat, _roots.Count);

    public string AddLabel => IsBundle
        ? string.Format(CultureInfo.CurrentCulture, Strings.Keys_AddSchemeAdmin_AddBundleFormat, _roots.Count)
        : Strings.Keys_AddSchemeAdmin_Add;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? ""))
            {
                OnPropertyChanged(nameof(CanAdd));
                (AddCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>What stops the add, or <see langword="null"/>.</summary>
    public string? Error { get; }

    public bool HasError => Error is not null;

    /// <summary>A caution that doesn't stop the add, or <see langword="null"/>.</summary>
    public string? Warning { get; }

    public bool HasWarning => Warning is not null && !HasError;

    public bool CanAdd => !_blocked && _roots.Count > 0 && (IsBundle || !string.IsNullOrWhiteSpace(_name));

    public ICommand AddCommand { get; }
    public ICommand CancelCommand { get; }

    /// <summary>The fields the dialog and the View dialog show for a root.</summary>
    internal static IReadOnlyList<KeysField> FieldsOf(X509Certificate2 root) =>
    [
        new(Strings.Keys_Field_Subject, root.Subject),
        new(Strings.Keys_Field_IssuedBy, root.Subject == root.Issuer ? Strings.Keys_SchemeAdmin_SelfSigned : root.Issuer),
        new(Strings.Keys_Field_Valid, string.Format(CultureInfo.CurrentCulture, Strings.Keys_ValidRangeFormat,
            CertificateText.Date(new DateTimeOffset(root.NotBefore.ToUniversalTime(), TimeSpan.Zero)),
            CertificateText.Date(new DateTimeOffset(root.NotAfter.ToUniversalTime(), TimeSpan.Zero)))),
        new(Strings.Keys_Field_Sha256, SchemeAdministratorRoots.Sha256Fingerprint(root), IsMono: true, CanCopy: true),
        new(Strings.Keys_Field_Key, CertificateText.KeyDescription(root)),
    ];

    private void Add()
    {
        if (!CanAdd)
            return;
        _store.Add(_roots, IsBundle ? null : _name, _time.GetUtcNow());
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
