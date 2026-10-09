using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Secom;

namespace EncDotNet.S100.Viewer.ViewModels.Keys;

/// <summary>
/// Add a trusted authority from a PEM file (#845 F1): shows the root it holds
/// and asks for the name things that chain to it are reported under.
/// </summary>
internal sealed class AddAuthorityDialogViewModel : ViewModelBase
{
    private readonly TrustedAuthorityStore _store;
    private readonly string? _pem;
    private readonly TimeProvider _time;
    private string _name = "";
    private string? _error;

    public AddAuthorityDialogViewModel(TrustedAuthorityStore store, string path, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrEmpty(path);
        _store = store;
        _time = time ?? TimeProvider.System;
        FileName = Path.GetFileName(path);

        try
        {
            _pem = File.ReadAllText(path);
            var root = TrustedAuthorityStore.Read("_", _pem).Roots[0].Certificate;
            _name = root.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            Fields =
            [
                new(Strings.Keys_Field_Subject, root.Subject),
                new(Strings.Keys_Field_Valid, string.Format(CultureInfo.CurrentCulture, Strings.Keys_ValidRangeFormat,
                    CertificateText.Date(new DateTimeOffset(root.NotBefore.ToUniversalTime(), TimeSpan.Zero)),
                    CertificateText.Date(new DateTimeOffset(root.NotAfter.ToUniversalTime(), TimeSpan.Zero)))),
                new(Strings.Keys_Field_Sha256, CertificateText.Fingerprint(root), IsMono: true),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _pem = null;
            _error = Strings.Keys_AddAuthority_CannotRead;
        }

        AddCommand = new RelayCommand(Add, () => CanAdd);
        CancelCommand = new RelayCommand(() => Closed?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Raised when the dialog should close.</summary>
    public event EventHandler? Closed;

    public string FileName { get; }
    public IReadOnlyList<KeysField> Fields { get; } = [];
    public bool HasCertificate => _pem is not null;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? ""))
                (AddCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => _error is not null;
    public bool CanAdd => _pem is not null && !string.IsNullOrWhiteSpace(_name);

    public ICommand AddCommand { get; }
    public ICommand CancelCommand { get; }

    private void Add()
    {
        if (_pem is null)
            return;
        try
        {
            _store.Add(_name, _pem, _time.GetUtcNow());
        }
        catch (InvalidDataException)
        {
            Error = Strings.Keys_AddAuthority_CannotRead;
            return;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }
}
