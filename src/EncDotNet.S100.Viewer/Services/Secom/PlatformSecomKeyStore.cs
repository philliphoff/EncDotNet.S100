using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services.Secom;

/// <summary>
/// The current user's personal certificate store (<see cref="StoreName.My"/>,
/// <see cref="StoreLocation.CurrentUser"/>): the login Keychain on macOS, the
/// Windows certificate store, and on Linux .NET's own store, PKCS#12 files under
/// <c>~/.dotnet/corefx/cryptography/x509stores/my</c> protected only by the
/// account's file permissions (Linux has no system key store .NET uses). The
/// key-store note says which.
/// </summary>
internal sealed class PlatformSecomKeyStore : ISecomKeyStore
{
    /// <inheritdoc />
    public string Handle => "x509:CurrentUser/My";

    /// <inheritdoc />
    public string DisplayName =>
        OperatingSystem.IsMacOS() ? Strings.Keys_Store_MacOS
        : OperatingSystem.IsWindows() ? Strings.Keys_Store_Windows
        : Strings.Keys_Store_Linux;

    /// <inheritdoc />
    // Windows keeps a key it is told to persist; macOS and Linux copy the key
    // into the store, which needs it exportable while it is loaded.
    public X509KeyStorageFlags ImportFlags => OperatingSystem.IsWindows()
        ? X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet
        : X509KeyStorageFlags.Exportable;

    /// <inheritdoc />
    public void Add(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);
    }

    /// <inheritdoc />
    public X509Certificate2? Find(string thumbprint)
    {
        ArgumentException.ThrowIfNullOrEmpty(thumbprint);
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            X509Certificate2? withKey = null;
            foreach (var certificate in found)
            {
                if (withKey is null && certificate.HasPrivateKey)
                    withKey = certificate;
                else
                    certificate.Dispose();
            }

            return withKey;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Remove(string thumbprint)
    {
        ArgumentException.ThrowIfNullOrEmpty(thumbprint);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
        foreach (var certificate in store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false))
        {
            using (certificate)
                store.Remove(certificate);
        }
    }
}

/// <summary>
/// Keys held in memory only, for runs whose settings are not saved
/// (<c>--ephemeral</c>, read-only settings) and for tests: nothing outlives the process.
/// </summary>
internal sealed class InMemorySecomKeyStore : ISecomKeyStore
{
    private readonly Dictionary<string, byte[]> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _transfer = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <inheritdoc />
    public string Handle => "memory";

    /// <inheritdoc />
    public string DisplayName => Strings.Keys_Store_Memory;

    /// <inheritdoc />
    public X509KeyStorageFlags ImportFlags => X509KeyStorageFlags.Exportable;

    /// <summary>How many entries the store holds (for tests).</summary>
    public int Count
    {
        get
        {
            lock (_entries)
                return _entries.Count;
        }
    }

    /// <inheritdoc />
    public void Add(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var bytes = certificate.Export(X509ContentType.Pkcs12, _transfer);
        lock (_entries)
            _entries[certificate.Thumbprint] = bytes;
    }

    /// <inheritdoc />
    public X509Certificate2? Find(string thumbprint)
    {
        byte[]? bytes;
        lock (_entries)
            _entries.TryGetValue(thumbprint, out bytes);
        return bytes is null ? null : X509CertificateLoader.LoadPkcs12(bytes, _transfer, X509KeyStorageFlags.Exportable);
    }

    /// <inheritdoc />
    public void Remove(string thumbprint)
    {
        lock (_entries)
            _entries.Remove(thumbprint);
    }
}
