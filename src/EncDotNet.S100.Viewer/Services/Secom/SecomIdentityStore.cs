using System.Security.Cryptography;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Secom;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services.Secom;

/// <summary>
/// The MCP identities the user imported (#845): references in
/// <see cref="ViewerSettings.SecomIdentities"/>, private keys in an
/// <see cref="ISecomKeyStore"/>. Every stored identity is registered with the
/// <see cref="SecomServerTrust"/>, so each SECOM source can present the one its
/// <see cref="EncDotNet.S100.Collections.SecomSource.Identity"/> setting names
/// (#883); one is the default, which sources set to <c>default</c> present. A
/// command-line identity (<c>--secom-identity</c>) is the default for its run.
/// </summary>
internal sealed class SecomIdentityStore
{
    /// <summary>The prefix of reference ids, e.g. <c>sc-ident:4f9c2a7e</c>.</summary>
    public const string ReferencePrefix = SecomIdentitySetting.ReferencePrefix;

    private readonly ViewerSettings _settings;
    private readonly SecomServerTrust _trust;
    private readonly ISecomKeyStore _keys;
    private readonly ILogger? _logger;

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

    /// <summary>Raised when the stored identities, the default, or a revocation changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The key store, whose name the page's note shows.</summary>
    public ISecomKeyStore Keys => _keys;

    /// <summary>The trust this store registers its identities with.</summary>
    public SecomServerTrust Trust => _trust;

    /// <summary>
    /// The Library whose SECOM sources name identities, so that replacing an
    /// identity moves them to the new one and removing it moves them to
    /// <c>none</c> (#883); <see langword="null"/> leaves sources alone.
    /// </summary>
    public CollectionLibrary? Library { get; set; }

    /// <summary>The stored identities, in the order they were imported.</summary>
    public IReadOnlyList<SecomIdentityReference> Identities => _settings.SecomIdentities;

    /// <summary>The id of the stored default identity, or <see langword="null"/> when none is set.</summary>
    public string? DefaultId => _settings.SecomDefaultIdentity;

    /// <summary>The identity given on the command line for this run, which is the default for the run.</summary>
    public SecomClientIdentity? CommandLineIdentity { get; private set; }

    /// <summary>When the command-line identity was found revoked this run, if it was.</summary>
    public DateTimeOffset? CommandLineRevokedAt { get; private set; }

    /// <summary>The reference id for a certificate thumbprint: <c>sc-ident:</c> and its first 8 hex digits.</summary>
    public static string ReferenceIdOf(string thumbprint) => SecomIdentitySetting.ReferenceIdOf(thumbprint);

    /// <summary>True when <paramref name="value"/> looks like a reference id rather than a path.</summary>
    public static bool IsReferenceId(string? value) =>
        value is not null && value.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The stored identity with <paramref name="id"/>, or <see langword="null"/>.</summary>
    public SecomIdentityReference? Find(string id) =>
        _settings.SecomIdentities.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Registers the stored identities at start, each loaded from the key store,
    /// and sets the default: the command-line identity when there is one,
    /// otherwise the stored default. A revoked identity is left out; one the
    /// key store no longer holds is left out (and logged): sources that name
    /// either present none.
    /// </summary>
    public void Restore(SecomClientIdentity? commandLine)
    {
        CommandLineIdentity = commandLine;
        foreach (var reference in _settings.SecomIdentities.Where(r => r.RevokedAt is null).ToList())
        {
            if (Open(reference) is { } identity)
                _trust.SetIdentity(reference.Id, identity);
        }

        if (commandLine is not null)
            _trust.SetIdentity(commandLine);
        else
            _trust.DefaultIdentityId = DefaultId;
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
    /// to settings, and registers it so sources can present it. Importing a
    /// certificate already stored renames it. The first identity imported
    /// becomes the default.
    /// </summary>
    /// <param name="identity">The identity from <see cref="Load"/>; the store takes ownership.</param>
    /// <param name="displayName">The name the user gave it.</param>
    /// <param name="makeDefault">True to make it the default.</param>
    /// <param name="replacing">
    /// The id of an identity it replaces, which is removed: it inherits being
    /// the default, and sources set to the old one are set to it.
    /// </param>
    /// <exception cref="CryptographicException">The key store refused the key.</exception>
    public SecomIdentityReference Import(SecomClientIdentity identity, string displayName, bool makeDefault, string? replacing = null)
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

        // Present the key-store copy, as the next run will.
        var stored = Open(reference);
        if (stored is not null)
            identity.Dispose();
        _trust.SetIdentity(reference.Id, stored ?? identity);

        makeDefault |= DefaultId is null;
        if (replacing is not null && !string.Equals(replacing, reference.Id, StringComparison.OrdinalIgnoreCase) && Find(replacing) is { } old)
        {
            makeDefault |= string.Equals(DefaultId, old.Id, StringComparison.OrdinalIgnoreCase);
            if (Library is { } library)
                SecomIdentityUsage.Reassign(library, old.Id, reference.Id);
            if (makeDefault)
                SetDefault(reference.Id);
            RemoveEntry(old);
        }
        else if (makeDefault)
        {
            SetDefault(reference.Id);
        }

        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return reference;
    }

    /// <summary>
    /// Makes the stored identity <paramref name="id"/> the default, or (with
    /// <see langword="null"/>) leaves no default; sources set to <c>default</c>
    /// are read again. A command-line identity stays the default for its run;
    /// the choice applies from the next.
    /// </summary>
    /// <returns>The default identity now presented, or <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">The id is unknown, revoked, or its key is no longer in the key store.</exception>
    public SecomClientIdentity? MakeDefault(string? id)
    {
        if (id is not null)
        {
            var reference = Find(id) ?? throw new InvalidOperationException($"No stored identity has the reference {id}.");
            if (reference.RevokedAt is not null)
                throw new InvalidOperationException($"{reference.DisplayName}'s certificate was revoked.");
            if (!_trust.Identities.ContainsKey(reference.Id))
            {
                var identity = Open(reference) ?? throw new InvalidOperationException($"The key store no longer holds {reference.DisplayName}'s key.");
                _trust.SetIdentity(reference.Id, identity);
            }

            id = reference.Id;
        }

        SetDefault(id);
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return _trust.Identity;
    }

    /// <summary>
    /// Deletes the stored identity <paramref name="id"/> and its key (#883 F4):
    /// sources set to it are set to <c>none</c>, and, when it is the default,
    /// <paramref name="newDefault"/> becomes the default (none when <see langword="null"/>).
    /// Every affected source is read again.
    /// </summary>
    public void Remove(string id, string? newDefault = null)
    {
        if (Find(id) is not { } reference)
            return;

        if (string.Equals(DefaultId, reference.Id, StringComparison.OrdinalIgnoreCase))
        {
            var next = newDefault is null ? null : Find(newDefault);
            SetDefault(next is { RevokedAt: null } && !string.Equals(next.Id, reference.Id, StringComparison.OrdinalIgnoreCase) ? next.Id : null);
        }

        if (Library is { } library)
            SecomIdentityUsage.Reassign(library, reference.Id, SecomIdentitySetting.None);
        RemoveEntry(reference);
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes every stored identity's key (before the settings are reset).</summary>
    public void RemoveAll()
    {
        foreach (var reference in _settings.SecomIdentities.ToList())
            RemoveEntry(reference);
        SetDefault(null);
    }

    /// <summary>
    /// Checks every registered identity for revocation (at start, on opening
    /// the page, and once a day). A revoked one is recorded and stops being presented.
    /// </summary>
    public IReadOnlyDictionary<string, SecomRevocationResult> CheckRevocation(bool fetch = true) => _trust.CheckIdentityRevocation(fetch);

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
        if (string.Equals(DefaultId, reference.Id, StringComparison.OrdinalIgnoreCase))
            SetDefault(null);
        _trust.SetIdentity(reference.Id, null);

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

    /// <summary>Stores the default, and makes it the trust's unless a command-line identity is the default for this run.</summary>
    private void SetDefault(string? id)
    {
        _settings.SecomDefaultIdentity = id;
        if (CommandLineIdentity is null)
            _trust.DefaultIdentityId = id;
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
            if (string.Equals(DefaultId, reference.Id, StringComparison.OrdinalIgnoreCase))
                SetDefault(null);
            _settings.Save();
        }

        if (ReferenceEquals(e.Identity, CommandLineIdentity))
            CommandLineRevokedAt = e.Result.RevokedAt ?? DateTimeOffset.UtcNow;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
