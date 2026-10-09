using System.Security.Cryptography;
using EncDotNet.S100.Collections.Secom;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services.Secom;

/// <summary>
/// The MCP identities the user imported (#845): references in
/// <see cref="ViewerSettings.SecomIdentities"/>, private keys in an
/// <see cref="ISecomKeyStore"/>. Any number may be stored; one at a time is in
/// use, fed to <see cref="SecomServerTrust.SetIdentity"/>. A command-line
/// identity (<c>--secom-identity</c>) overrides the stored choice for its run.
/// </summary>
internal sealed class SecomIdentityStore
{
    /// <summary>The prefix of reference ids, e.g. <c>sc-ident:4f9c2a7e</c>.</summary>
    public const string ReferencePrefix = "sc-ident:";

    private readonly ViewerSettings _settings;
    private readonly SecomServerTrust _trust;
    private readonly ISecomKeyStore _keys;
    private readonly ILogger? _logger;
    private SecomClientIdentity? _stored;

    public SecomIdentityStore(ViewerSettings settings, SecomServerTrust trust, ISecomKeyStore keys, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(keys);
        _settings = settings;
        _trust = trust;
        _keys = keys;
        _logger = logger;
        _trust.IdentityRevoked += OnIdentityRevoked;
    }

    /// <summary>Raised when the stored identities, the one in use, or a revocation changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The key store, whose name the page's note shows.</summary>
    public ISecomKeyStore Keys => _keys;

    /// <summary>The trust whose identity this store sets.</summary>
    public SecomServerTrust Trust => _trust;

    /// <summary>The stored identities, in the order they were imported.</summary>
    public IReadOnlyList<SecomIdentityReference> Identities => _settings.SecomIdentities;

    /// <summary>The id of the identity chosen for use, or <see langword="null"/>.</summary>
    public string? InUseId => _settings.SecomIdentityInUse;

    /// <summary>The identity given on the command line for this run, which wins over the stored choice.</summary>
    public SecomClientIdentity? CommandLineIdentity { get; private set; }

    /// <summary>When the command-line identity was found revoked this run, if it was.</summary>
    public DateTimeOffset? CommandLineRevokedAt { get; private set; }

    /// <summary>The reference id for a certificate thumbprint: <c>sc-ident:</c> and its first 8 hex digits.</summary>
    public static string ReferenceIdOf(string thumbprint) =>
        ReferencePrefix + thumbprint[..Math.Min(8, thumbprint.Length)].ToLowerInvariant();

    /// <summary>True when <paramref name="value"/> looks like a reference id rather than a path.</summary>
    public static bool IsReferenceId(string? value) =>
        value is not null && value.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The stored identity with <paramref name="id"/>, or <see langword="null"/>.</summary>
    public SecomIdentityReference? Find(string id) =>
        _settings.SecomIdentities.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sets the identity SECOM requests present at start: the command-line
    /// identity when there is one, otherwise the stored one in use, loaded
    /// from the key store. A stored identity the key store no longer holds is
    /// left unused (and logged).
    /// </summary>
    public void Restore(SecomClientIdentity? commandLine)
    {
        CommandLineIdentity = commandLine;
        if (commandLine is not null)
        {
            _trust.SetIdentity(commandLine);
            return;
        }

        if (InUseId is { } id && Find(id) is { RevokedAt: null } reference && Open(reference) is { } identity)
        {
            _stored = identity;
            _trust.SetIdentity(identity);
        }
    }

    /// <summary>
    /// Reads an identity file for the import check, with its key loaded so the
    /// key store can take it.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">No certificate with a private key, or the wrong password.</exception>
    public SecomClientIdentity Load(string path, string? password) =>
        SecomClientIdentity.Load(path, password, _trust.Anchors, _keys.ImportFlags);

    /// <summary>
    /// Adds <paramref name="identity"/>'s key to the key store and a reference
    /// to settings. Importing a certificate already stored renames it.
    /// </summary>
    /// <param name="identity">The identity from <see cref="Load"/>; the store takes ownership.</param>
    /// <param name="displayName">The name the user gave it.</param>
    /// <param name="use">True to use it from now on.</param>
    /// <param name="replacing">The id of an identity it replaces, which is removed; it inherits being in use.</param>
    /// <exception cref="CryptographicException">The key store refused the key.</exception>
    public SecomIdentityReference Import(SecomClientIdentity identity, string displayName, bool use, string? replacing = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var certificate = identity.Certificate;
        _keys.Add(certificate);

        var reference = Find(ReferenceIdOf(certificate.Thumbprint));
        if (reference is null)
        {
            reference = new SecomIdentityReference { Id = ReferenceIdOf(certificate.Thumbprint) };
            _settings.SecomIdentities.Add(reference);
        }

        reference.DisplayName = string.IsNullOrWhiteSpace(displayName) ? identity.Subject : displayName.Trim();
        reference.Mrn = identity.Mrn;
        reference.Subject = identity.Subject;
        reference.Issuer = identity.Issuer;
        reference.Thumbprint = certificate.Thumbprint;
        reference.NotBefore = identity.NotBefore;
        reference.NotAfter = identity.NotAfter;
        reference.Kind = KindOf(identity);
        reference.KeyStore = _keys.Handle;
        reference.RevokedAt = null;

        if (replacing is not null && !string.Equals(replacing, reference.Id, StringComparison.OrdinalIgnoreCase) && Find(replacing) is { } old)
        {
            use |= string.Equals(InUseId, old.Id, StringComparison.OrdinalIgnoreCase);
            RemoveEntry(old);
        }

        if (use)
        {
            // Present the key-store copy, as the next run will.
            _settings.SecomIdentityInUse = reference.Id;
            var stored = Open(reference);
            if (stored is not null)
                identity.Dispose();
            Present(stored ?? identity);
        }
        else
        {
            identity.Dispose();
        }

        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return reference;
    }

    /// <summary>
    /// Uses the stored identity <paramref name="id"/> from now on, or (with
    /// <see langword="null"/>) none. A command-line identity stays in effect for
    /// its run; the choice applies from the next.
    /// </summary>
    /// <returns>The identity now presented, or <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">The id is unknown, revoked, or its key is no longer in the key store.</exception>
    public SecomClientIdentity? Use(string? id)
    {
        if (id is null)
        {
            _settings.SecomIdentityInUse = null;
            Present(null);
        }
        else
        {
            var reference = Find(id) ?? throw new InvalidOperationException($"No stored identity has the reference {id}.");
            if (reference.RevokedAt is not null)
                throw new InvalidOperationException($"{reference.DisplayName}'s certificate was revoked.");
            var identity = Open(reference) ?? throw new InvalidOperationException($"The key store no longer holds {reference.DisplayName}'s key.");
            _settings.SecomIdentityInUse = reference.Id;
            Present(identity);
        }

        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return _trust.Identity;
    }

    /// <summary>Deletes the stored identity <paramref name="id"/> and its key; if it was in use, SECOM requests connect anonymously.</summary>
    public void Remove(string id)
    {
        if (Find(id) is not { } reference)
            return;

        RemoveEntry(reference);
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes every stored identity's key (before the settings are reset).</summary>
    public void RemoveAll()
    {
        foreach (var reference in _settings.SecomIdentities.ToList())
            RemoveEntry(reference);
    }

    /// <summary>
    /// Checks the identity in use for revocation (at start, on opening the
    /// page, and once a day). A revoked one is recorded and stops being used.
    /// </summary>
    public SecomRevocationResult CheckRevocation(bool fetch = true) => _trust.CheckIdentityRevocation(fetch);

    /// <summary>What a certificate identifies, from its MRN, MCP attributes and issuer.</summary>
    internal static string KindOf(SecomClientIdentity identity)
    {
        var mrn = identity.Mrn ?? "";
        if (identity.Issuer.Contains("test", StringComparison.OrdinalIgnoreCase) || mrn.Contains(":mcc-test:", StringComparison.OrdinalIgnoreCase))
            return "Test";
        if (identity.ImoNumber is not null || identity.Mmsi is not null || mrn.Contains(":vessel:", StringComparison.OrdinalIgnoreCase))
            return "Vessel";
        if (mrn.Contains(":org:", StringComparison.OrdinalIgnoreCase))
            return "Organisation";
        if (mrn.Contains(":service:", StringComparison.OrdinalIgnoreCase))
            return "Service";
        if (mrn.Contains(":user:", StringComparison.OrdinalIgnoreCase))
            return "Person";
        return "Device";
    }

    private void RemoveEntry(SecomIdentityReference reference)
    {
        if (string.Equals(InUseId, reference.Id, StringComparison.OrdinalIgnoreCase))
        {
            _settings.SecomIdentityInUse = null;
            Present(null);
        }

        try
        {
            _keys.Remove(reference.Thumbprint);
        }
        catch (CryptographicException ex)
        {
            _logger?.LogWarning(ex, "The key of SECOM identity {Id} could not be deleted from the key store", reference.Id);
        }

        _settings.SecomIdentities.Remove(reference);
    }

    /// <summary>Presents <paramref name="identity"/> unless a command-line identity overrides it for this run.</summary>
    private void Present(SecomClientIdentity? identity)
    {
        _stored = identity;
        if (CommandLineIdentity is null)
            _trust.SetIdentity(identity);
    }

    private SecomClientIdentity? Open(SecomIdentityReference reference)
    {
        var certificate = _keys.Find(reference.Thumbprint);
        if (certificate is null)
        {
            _logger?.LogWarning("The key store no longer holds SECOM identity {Id}", reference.Id);
            return null;
        }

        try
        {
            return SecomClientIdentity.FromCertificate(certificate, _trust.Anchors);
        }
        catch (InvalidDataException ex)
        {
            certificate.Dispose();
            _logger?.LogWarning(ex, "SECOM identity {Id} could not be opened", reference.Id);
            return null;
        }
    }

    private void OnIdentityRevoked(object? sender, SecomIdentityRevokedEventArgs e)
    {
        var thumbprint = e.Identity.Certificate.Thumbprint;
        var reference = _settings.SecomIdentities.FirstOrDefault(r => string.Equals(r.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase));
        if (reference is not null)
        {
            reference.RevokedAt = e.Result.RevokedAt ?? DateTimeOffset.UtcNow;
            if (string.Equals(InUseId, reference.Id, StringComparison.OrdinalIgnoreCase))
                _settings.SecomIdentityInUse = null;
            _settings.Save();
        }

        if (ReferenceEquals(e.Identity, _stored))
            _stored = null;
        if (ReferenceEquals(e.Identity, CommandLineIdentity))
            CommandLineRevokedAt = e.Result.RevokedAt ?? DateTimeOffset.UtcNow;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
