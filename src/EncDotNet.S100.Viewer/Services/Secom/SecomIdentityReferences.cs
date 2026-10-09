using Avalonia.Threading;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Mcp.Tools.Library;

namespace EncDotNet.S100.Viewer.Services.Secom;

/// <summary>
/// The stored identities as <c>set_secom_identity</c> sees them (#845 H3): a
/// reference id chooses one as the Keys &amp; certificates page's Use does.
/// Calls run on the UI thread, where the store and the page live.
/// </summary>
internal sealed class SecomIdentityReferences(SecomIdentityStore store) : ISecomIdentityReferences
{
    /// <inheritdoc />
    public SecomClientIdentity Use(string referenceId) =>
        Dispatcher.UIThread.Invoke(() =>
        {
            if (store.CommandLineIdentity is not null)
                throw new InvalidOperationException("--secom-identity sets the identity for this run; choose a stored one in a run without it.");
            return store.Use(referenceId) ?? throw new InvalidOperationException($"No stored identity has the reference {referenceId}.");
        });

    /// <inheritdoc />
    public string? ReferenceOf(SecomClientIdentity identity) =>
        store.Identities.FirstOrDefault(r => string.Equals(r.Thumbprint, identity.Certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))?.Id;
}
