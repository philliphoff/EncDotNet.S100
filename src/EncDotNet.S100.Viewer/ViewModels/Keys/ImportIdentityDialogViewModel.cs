using System.Globalization;
using System.Security.Cryptography;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Secom;

namespace EncDotNet.S100.Viewer.ViewModels.Keys;

/// <summary>
/// Import identity (#845 D): three steps. <b>File</b> unlocks a .p12 or PEM
/// with its password; <b>Check</b> shows what the certificate says and takes a
/// name; <b>Use</b> confirms the import and offers to use it. The password is
/// dropped once the file is read, and the private key never leaves the
/// <see cref="SecomIdentityStore"/>'s key store after import.
/// </summary>
internal sealed class ImportIdentityDialogViewModel : ViewModelBase
{
    private readonly SecomIdentityStore _store;
    private readonly Func<Task<string?>> _pickFile;
    private readonly string? _replacing;
    private readonly TimeProvider _time;
    private SecomClientIdentity? _loaded;
    private SecomIdentityReference? _imported;
    private int _step;
    private string? _path;
    private string _password = "";
    private string? _error;
    private string _name = "";
    private bool _useForSecom;

    public ImportIdentityDialogViewModel(SecomIdentityStore store, Func<Task<string?>> pickFile, string? replacing = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(pickFile);
        _store = store;
        _pickFile = pickFile;
        _replacing = replacing;
        _time = time ?? TimeProvider.System;
        _useForSecom = store.InUseId is null
            || (replacing is not null && string.Equals(store.InUseId, replacing, StringComparison.OrdinalIgnoreCase));

        PrimaryCommand = new RelayCommand(Primary, () => CanPrimary);
        BackCommand = new RelayCommand(Back);
        ChangeFileCommand = new RelayCommand(() => _ = PickFileAsync());
        CancelCommand = new RelayCommand(Close);
    }

    /// <summary>Raised when the dialog should close (done or cancelled).</summary>
    public event EventHandler? Closed;

    /// <summary>The step: 0 File, 1 Check, 2 Use.</summary>
    public int Step
    {
        get => _step;
        private set
        {
            if (SetProperty(ref _step, value))
                OnStepChanged();
        }
    }

    public bool IsFileStep => _step == 0;
    public bool IsCheckStep => _step == 1;
    public bool IsUseStep => _step == 2;
    public bool CanGoBack => _step == 1;
    public string Title => _replacing is null ? Strings.Keys_Import_Title : Strings.Keys_Import_ReplaceTitle;

    /// <summary>"Step 1 of 3 · File".</summary>
    public string StepLabel => string.Format(CultureInfo.CurrentCulture, Strings.Keys_Import_StepFormat, _step + 1,
        _step switch { 0 => Strings.Keys_Import_StepFile, 1 => Strings.Keys_Import_StepCheck, _ => Strings.Keys_Import_StepUse });

    public string PrimaryLabel => _step switch { 0 => Strings.Keys_Import_Unlock, 1 => Strings.Keys_Import_Import, _ => Strings.Keys_Import_Done };

    public string FileName => _path is null ? "" : Path.GetFileName(_path);

    /// <summary>"PKCS #12 · contains a certificate and a private key", or the PEM equivalent.</summary>
    public string FileKind => _path is null ? "" : IsPemFile(_path) ? Strings.Keys_Import_PemKind : Strings.Keys_Import_Pkcs12Kind;

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value ?? ""))
                Error = null;
        }
    }

    /// <summary>Why the file could not be read, shown under the password.</summary>
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

    /// <summary>The certificate's facts for the Check step.</summary>
    public IReadOnlyList<KeysField> CheckFields { get; private set; } = [];

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? ""))
                (PrimaryCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Why the certificate cannot be imported (it has ended), or <see langword="null"/>.</summary>
    public string? Blocker { get; private set; }
    public bool HasBlocker => Blocker is not null;

    public string KeyStoreNote => string.Format(CultureInfo.CurrentCulture, Strings.Keys_Import_KeyStoreNoteFormat, _store.Keys.DisplayName);

    public string ImportedTitle => _imported is null ? "" : string.Format(CultureInfo.CurrentCulture, Strings.Keys_Import_ImportedFormat, _imported.DisplayName);
    public string Reference => _imported?.Id ?? "";

    /// <summary>The Use step's choice: use it for SECOM services that ask for a certificate.</summary>
    public bool UseForSecom
    {
        get => _useForSecom;
        set => SetProperty(ref _useForSecom, value);
    }

    public bool CanPrimary => _step switch
    {
        0 => _path is not null,
        1 => _loaded is not null && Blocker is null && !string.IsNullOrWhiteSpace(_name),
        _ => true,
    };

    public ICommand PrimaryCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand ChangeFileCommand { get; }
    public ICommand CancelCommand { get; }

    /// <summary>Asks for the file; false when the user cancelled the picker before any file was chosen.</summary>
    public async Task<bool> PickFileAsync()
    {
        var path = await _pickFile();
        if (path is null)
            return _path is not null;
        _path = path;
        Error = null;
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(FileKind));
        (PrimaryCommand as RelayCommand)?.NotifyCanExecuteChanged();
        return true;
    }

    /// <summary>Unlock, Import or Done, by step.</summary>
    public void Primary()
    {
        switch (_step)
        {
            case 0: Unlock(); break;
            case 1: Import(); break;
            default: Finish(); break;
        }
    }

    private void Unlock()
    {
        if (_path is null)
            return;
        try
        {
            _loaded = _store.Load(_path, _password.Length == 0 ? null : _password);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException)
        {
            Error = ex is FileNotFoundException ? Strings.Keys_Import_FileMissing : Strings.Keys_Import_CannotRead;
            return;
        }

        Password = "";
        Error = null;
        var now = _time.GetUtcNow();
        CheckFields = BuildCheck(_loaded, now);
        Blocker = _loaded.NotAfter <= now ? Strings.Keys_Import_Ended : null;
        _name = _loaded.Subject;
        Step = 1;
    }

    private void Import()
    {
        if (_loaded is null || Blocker is not null)
            return;
        try
        {
            // Not used yet: the Use step decides.
            _imported = _store.Import(_loaded, _name, use: false, _replacing);
        }
        catch (CryptographicException)
        {
            Blocker = string.Format(CultureInfo.CurrentCulture, Strings.Keys_Import_StoreRefusedFormat, _store.Keys.DisplayName);
            OnPropertyChanged(nameof(Blocker));
            OnPropertyChanged(nameof(HasBlocker));
            return;
        }

        _loaded = null;
        Step = 2;
    }

    private void Finish()
    {
        if (_imported is not null)
        {
            var inUse = string.Equals(_store.InUseId, _imported.Id, StringComparison.OrdinalIgnoreCase);
            if (_useForSecom && !inUse)
                _store.Use(_imported.Id);
            else if (!_useForSecom && inUse)
                _store.Use(null);
        }

        Close();
    }

    private void Back()
    {
        if (_step != 1)
            return;
        _loaded?.Dispose();
        _loaded = null;
        Blocker = null;
        Step = 0;
    }

    private void Close()
    {
        _loaded?.Dispose();
        _loaded = null;
        _password = "";
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnStepChanged()
    {
        OnPropertyChanged(string.Empty);
        (PrimaryCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private static bool IsPemFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".pem" or ".crt" or ".cer" or ".key";

    private static List<KeysField> BuildCheck(SecomClientIdentity identity, DateTimeOffset now)
    {
        var fields = new List<KeysField> { new(Strings.Keys_Field_Mrn, identity.Mrn ?? identity.Subject, IsMono: true) };

        var vessel = new List<string>();
        if (identity.ImoNumber is { } imo)
            vessel.Add(string.Format(CultureInfo.CurrentCulture, Strings.Keys_Vessel_ImoFormat, imo));
        if (identity.Mmsi is { } mmsi)
            vessel.Add(string.Format(CultureInfo.CurrentCulture, Strings.Keys_Vessel_MmsiFormat, mmsi));
        if (identity.FlagState is { } flag)
            vessel.Add(string.Format(CultureInfo.CurrentCulture, Strings.Keys_Vessel_FlagFormat, flag));
        if (vessel.Count > 0)
            fields.Add(new(Strings.Keys_Field_Vessel, string.Join(" · ", vessel)));

        fields.Add(new(Strings.Keys_Field_IssuedBy, identity.Issuer));
        fields.Add(identity.Anchor is { } anchor
            ? new(Strings.Keys_Field_ChainsTo, string.Format(CultureInfo.CurrentCulture, Strings.Keys_Import_ChainsToFormat, anchor), Tone: KeysTone.Success, ShowsCheck: true)
            : new(Strings.Keys_Field_ChainsTo, Strings.Keys_Import_NotTrusted, Tone: KeysTone.Warning));

        var range = string.Format(CultureInfo.CurrentCulture, Strings.Keys_ValidRangeFormat, CertificateText.Date(identity.NotBefore), CertificateText.Date(identity.NotAfter));
        var days = CertificateText.DaysUntil(identity.NotAfter, now);
        fields.Add(identity.NotAfter <= now
            ? new(Strings.Keys_Field_Valid, range + " · " + Strings.Keys_Status_Ended, Tone: KeysTone.Destructive)
            : days <= KeysAndCertificatesViewModel.EndingSoonDays
                ? new(Strings.Keys_Field_Valid, range + " · " + CertificateText.EndsIn(days).ToLower(CultureInfo.CurrentCulture), Tone: KeysTone.Warning)
                : new(Strings.Keys_Field_Valid, range));
        fields.Add(new(Strings.Keys_Field_Key, CertificateText.KeyDescription(identity.Certificate)));
        return fields;
    }
}
