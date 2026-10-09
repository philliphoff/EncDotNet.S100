using System.Security.Cryptography.X509Certificates;

namespace EncDotNet.S100.Viewer.Services.Secom;

/// <summary>
/// Where imported MCP identities keep their private keys (#845). The viewer's
/// settings hold only a reference (the thumbprint) to an entry here.
/// </summary>
internal interface ISecomKeyStore
{
    /// <summary>The handle settings record for entries in this store, e.g. <c>x509:CurrentUser/My</c>.</summary>
    string Handle { get; }

    /// <summary>How the key-store note names this store, e.g. "the Keychain".</summary>
    string DisplayName { get; }

    /// <summary>How an identity file should be loaded for <see cref="Add"/>: the flags <c>SecomClientIdentity.Load</c> is given for a key this store will take.</summary>
    X509KeyStorageFlags ImportFlags { get; }

    /// <summary>Adds a certificate and its private key.</summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The store refused it.</exception>
    void Add(X509Certificate2 certificate);

    /// <summary>The certificate with its private key, or <see langword="null"/> when the store no longer holds it. The caller owns the result.</summary>
    X509Certificate2? Find(string thumbprint);

    /// <summary>Deletes the certificate and its private key; does nothing when it is not there.</summary>
    void Remove(string thumbprint);
}
