using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// A Scheme Administrator root as the Scheme administrators tab lists it.
/// </summary>
/// <param name="Id">The root's SHA-256 fingerprint (upper-case hex).</param>
/// <param name="Name">The name badges report for what chains to it.</param>
/// <param name="Root">The root certificate.</param>
/// <param name="BuiltIn">True for a root shipped with SoundCharts: it can be turned off but not removed.</param>
/// <param name="IsTest">True for the IHO testing root: data it verifies is test data.</param>
/// <param name="Enabled">Whether exchange sets are verified against it.</param>
/// <param name="AddedAt">When the user added it from a file; <see langword="null"/> for a built-in root.</param>
internal sealed record SchemeAdministrator(
    string Id, string Name, X509Certificate2 Root, bool BuiltIn, bool IsTest, bool Enabled, DateTimeOffset? AddedAt);

/// <summary>
/// The S-100 Part 15 Scheme Administrator roots exchange sets are verified
/// against: the built-in IHO root (on by default), the IHO testing root (off by
/// default), and roots the user added from files. Kept apart from the SECOM
/// trusted authorities, so a SECOM root never makes chart data verified.
/// </summary>
internal sealed class SchemeAdministratorStore
{
    /// <summary>The <see cref="TrustedAuthorityReference.Kind"/> of a user-added root.</summary>
    public const string Kind = "SchemeAdministrator";

    private readonly ViewerSettings _settings;
    private readonly IReadOnlyList<BuiltInRoot> _builtIn;
    private IReadOnlyList<SchemeAdministrator>? _entries;

    public SchemeAdministratorStore(ViewerSettings settings)
        : this(settings, DefaultBuiltIns())
    {
    }

    internal SchemeAdministratorStore(ViewerSettings settings, IReadOnlyList<BuiltInRoot> builtIn)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(builtIn);
        _settings = settings;
        _builtIn = builtIn;
    }

    /// <summary>Raised after the list changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Built-in roots first, then those added from files, oldest first.</summary>
    public IReadOnlyList<SchemeAdministrator> Entries => _entries ??= Build();

    /// <summary>Whether the build includes the IHO root.</summary>
    public bool HasIhoRoot => _builtIn.Any(b => !b.IsTest);

    /// <summary>Whether any root is on.</summary>
    public bool AnyEnabled => Entries.Any(e => e.Enabled);

    /// <summary>
    /// The trust anchors to verify exchange sets with: the roots that are on.
    /// Untrusted signers are allowed, so they show as unverified rather than
    /// failing.
    /// </summary>
    public TrustAnchorOptions TrustAnchors() => new()
    {
        TrustedRoots = Entries.Where(e => e.Enabled).Select(e => e.Root).ToList(),
        AllowUntrustedCertificates = true,
    };

    /// <summary>The entry for <paramref name="root"/>, or <see langword="null"/>.</summary>
    public SchemeAdministrator? Find(X509Certificate2 root) =>
        Entries.FirstOrDefault(e => ReferenceEquals(e.Root, root) || e.Root.RawData.AsSpan().SequenceEqual(root.RawData));

    /// <summary>The root's SHA-256 fingerprint, upper-case hex.</summary>
    public static string IdOf(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData));

    /// <summary>Whether a root with this fingerprint is already listed.</summary>
    public bool Contains(X509Certificate2 certificate) =>
        Entries.Any(e => string.Equals(e.Id, IdOf(certificate), StringComparison.OrdinalIgnoreCase));

    /// <summary>Turns a built-in root on or off.</summary>
    public void SetEnabled(string id, bool enabled)
    {
        _settings.BuiltInSchemeAdministrators[id] = enabled;
        Commit();
    }

    /// <summary>
    /// Adds <paramref name="roots"/>, each as its own entry. With one root,
    /// <paramref name="name"/> names it; otherwise each takes its common name.
    /// </summary>
    public void Add(IReadOnlyList<X509Certificate2> roots, string? name, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(roots);
        foreach (var root in roots)
        {
            var id = IdOf(root);
            var rootName = roots.Count == 1 && !string.IsNullOrWhiteSpace(name)
                ? name.Trim()
                : root.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            _settings.SchemeAdministrators.RemoveAll(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
            _settings.SchemeAdministrators.Add(new TrustedAuthorityReference
            {
                Id = id,
                Name = rootName,
                Kind = Kind,
                Pem = root.ExportCertificatePem(),
                AddedAt = now,
            });
        }

        Commit();
    }

    /// <summary>Removes a root the user added; built-in roots are only turned off.</summary>
    public void Remove(string id)
    {
        if (_settings.SchemeAdministrators.RemoveAll(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)) > 0)
            Commit();
    }

    private void Commit()
    {
        _settings.Save();
        _entries = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<SchemeAdministrator> Build()
    {
        var list = new List<SchemeAdministrator>();
        foreach (var builtIn in _builtIn)
        {
            var id = IdOf(builtIn.Root);
            var enabled = _settings.BuiltInSchemeAdministrators.TryGetValue(id, out var on) ? on : !builtIn.IsTest;
            list.Add(new SchemeAdministrator(id, builtIn.Name, builtIn.Root, BuiltIn: true, builtIn.IsTest, enabled, AddedAt: null));
        }

        foreach (var added in _settings.SchemeAdministrators)
        {
            X509Certificate2 root;
            try
            {
                root = SchemeAdministratorRoots.Load(System.Text.Encoding.ASCII.GetBytes(added.Pem), added.Name)[0];
            }
            catch (SchemeAdministratorRootException)
            {
                continue;
            }

            if (list.Any(e => string.Equals(e.Id, IdOf(root), StringComparison.OrdinalIgnoreCase)))
                continue;
            list.Add(new SchemeAdministrator(added.Id, added.Name, root, BuiltIn: false, IsTest: false, Enabled: true, added.AddedAt));
        }

        return list;
    }

    private static IReadOnlyList<BuiltInRoot> DefaultBuiltIns() =>
    [
        new(Resources.Strings.Keys_SchemeAdmin_IhoName, SchemeAdministratorRoots.LoadIhoRoot(), IsTest: false),
        new(Resources.Strings.Keys_SchemeAdmin_IhoTestName, SchemeAdministratorRoots.LoadIhoTestingRoot(), IsTest: true),
    ];

    /// <summary>A root shipped with SoundCharts.</summary>
    internal sealed record BuiltInRoot(string Name, X509Certificate2 Root, bool IsTest);
}
