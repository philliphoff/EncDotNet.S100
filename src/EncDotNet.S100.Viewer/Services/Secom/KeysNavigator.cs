using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Keys;

namespace EncDotNet.S100.Viewer.Services.Secom;

/// <summary>
/// Opens Settings → Keys &amp; certificates on a tab: the way from a SECOM
/// certificate problem (a directory chip, a Library source, a notification)
/// to where it is fixed (#880).
/// </summary>
internal sealed class KeysNavigator(Func<SettingsViewModel> settings, Func<MainViewModel> main)
{
    /// <summary>Shows Settings → Keys &amp; certificates on <paramref name="tab"/>.</summary>
    public void Open(KeysTab tab)
    {
        settings().ShowKeysAndCertificates(tab);
        main().SelectTab("Settings");
    }

    /// <summary>
    /// The tab that fixes a SECOM problem with <paramref name="code"/> (a
    /// <see cref="SecomDiagnosticCodes"/> value): <see cref="KeysTab.Authorities"/>
    /// for an untrusted server, <see cref="KeysTab.Identities"/> for the rest;
    /// <see langword="null"/> when no tab fixes it.
    /// </summary>
    public static KeysTab? TabFor(string? code) => code switch
    {
        SecomDiagnosticCodes.NeedsCertificate or SecomDiagnosticCodes.NeedsSignedRequest or SecomDiagnosticCodes.CertificateRefused => KeysTab.Identities,
        SecomDiagnosticCodes.ServerUntrusted => KeysTab.Authorities,
        _ => null,
    };

    /// <summary>The link text for <paramref name="code"/>: "Set up an identity…", "Manage identities…" or "Trusted authorities…".</summary>
    public static string? ActionText(string? code) => code switch
    {
        SecomDiagnosticCodes.NeedsCertificate or SecomDiagnosticCodes.NeedsSignedRequest => Resources.Strings.Keys_Fix_SetUpIdentity,
        SecomDiagnosticCodes.CertificateRefused => Resources.Strings.Keys_Fix_ManageIdentities,
        SecomDiagnosticCodes.ServerUntrusted => Resources.Strings.Keys_Fix_TrustedAuthorities,
        _ => null,
    };
}
