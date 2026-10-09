using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Viewer.Services.Secom;

/// <summary>A trust anchor as the Trusted authorities tab lists it (#845).</summary>
/// <param name="Id">The root's SHA-256 fingerprint (hex).</param>
/// <param name="Name">The anchor name (e.g. "MCP MCC"), reported for what chains to it.</param>
/// <param name="Root">The root certificate.</param>
/// <param name="BuiltIn">True for a root shipped with SoundCharts: it can be turned off but not removed.</param>
/// <param name="Enabled">False when the user turned a built-in root off.</param>
/// <param name="AddedAt">When the user added it from a file; <see langword="null"/> for a built-in root.</param>
internal sealed record TrustedAuthority(string Id, string Name, X509Certificate2 Root, bool BuiltIn, bool Enabled, DateTimeOffset? AddedAt);

/// <summary>
/// The anchors SECOM trusts (#845): the built-in roots, less any the user
/// turned off, and roots the user added from PEM files. Every change replaces
/// <see cref="SecomServerTrust.Anchors"/>, which SECOM handlers, signature
/// checks and identities all read.
/// </summary>
internal sealed class TrustedAuthorityStore
{
    private readonly ViewerSettings _settings;
    private readonly SecomServerTrust _trust;
    private readonly SecomTrustAnchors _builtIn;

    public TrustedAuthorityStore(ViewerSettings settings, SecomServerTrust trust, SecomTrustAnchors? builtIn = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(trust);
        _settings = settings;
        _trust = trust;
        _builtIn = builtIn ?? SecomTrustAnchors.BuiltIn;
    }

    /// <summary>Raised after the list or the anchors change.</summary>
    public event EventHandler? Changed;

    /// <summary>Built-in roots first, then those added from files, oldest first.</summary>
    public IReadOnlyList<TrustedAuthority> Authorities
    {
        get
        {
            var list = _builtIn.Roots
                .Select(r => new TrustedAuthority(IdOf(r.Certificate), r.Name, r.Certificate, BuiltIn: true,
                    Enabled: !_settings.DisabledBuiltInAuthorities.Contains(IdOf(r.Certificate), StringComparer.OrdinalIgnoreCase), AddedAt: null))
                .ToList();
            foreach (var added in _settings.TrustedAuthorities)
            {
                if (Parse(added) is { Roots.Count: > 0 } anchors)
                    list.Add(new TrustedAuthority(added.Id, added.Name, anchors.Roots[0].Certificate, BuiltIn: false, Enabled: true, added.AddedAt));
            }

            return list;
        }
    }

    /// <summary>The root's SHA-256 fingerprint, upper-case hex.</summary>
    public static string IdOf(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData));

    /// <summary>Sets <see cref="SecomServerTrust.Anchors"/> from the current choices.</summary>
    public void Apply()
    {
        var roots = new List<SecomTrustAnchor>();
        var intermediates = new List<X509Certificate2>(_builtIn.Intermediates);
        roots.AddRange(_builtIn.Roots.Where(r => !_settings.DisabledBuiltInAuthorities.Contains(IdOf(r.Certificate), StringComparer.OrdinalIgnoreCase)));
        foreach (var added in _settings.TrustedAuthorities)
        {
            if (Parse(added) is not { } anchors)
                continue;
            roots.AddRange(anchors.Roots);
            intermediates.AddRange(anchors.Intermediates);
        }

        _trust.Anchors = new SecomTrustAnchors(roots, intermediates);
    }

    /// <summary>Turns a built-in root on or off.</summary>
    public void SetEnabled(string id, bool enabled)
    {
        var disabled = _settings.DisabledBuiltInAuthorities;
        disabled.RemoveAll(d => string.Equals(d, id, StringComparison.OrdinalIgnoreCase));
        if (!enabled)
            disabled.Add(id);
        Commit();
    }

    /// <summary>
    /// Reads a PEM bundle for the add check: its roots (self-signed) and
    /// intermediates under <paramref name="name"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The text holds no root certificate.</exception>
    public static SecomTrustAnchors Read(string name, string pem)
    {
        SecomTrustAnchors anchors;
        try
        {
            anchors = SecomTrustAnchors.FromPem(name, pem);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }

        return anchors.Roots.Count > 0
            ? anchors
            : throw new InvalidDataException("The file holds no root (self-signed) certificate.");
    }

    /// <summary>Adds the roots in <paramref name="pem"/> as a trusted authority named <paramref name="name"/>.</summary>
    /// <exception cref="InvalidDataException">The text holds no root certificate.</exception>
    public TrustedAuthorityReference Add(string name, string pem, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var anchors = Read(name.Trim(), pem);
        var id = IdOf(anchors.Roots[0].Certificate);
        _settings.TrustedAuthorities.RemoveAll(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        var reference = new TrustedAuthorityReference { Id = id, Name = name.Trim(), Kind = "Secom", Pem = pem, AddedAt = now };
        _settings.TrustedAuthorities.Add(reference);
        Commit();
        return reference;
    }

    /// <summary>Removes an authority the user added; built-in roots are only turned off.</summary>
    public void Remove(string id)
    {
        if (_settings.TrustedAuthorities.RemoveAll(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)) > 0)
            Commit();
    }

    private void Commit()
    {
        _settings.Save();
        Apply();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static SecomTrustAnchors? Parse(TrustedAuthorityReference reference)
    {
        try
        {
            return SecomTrustAnchors.FromPem(reference.Name, reference.Pem);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
