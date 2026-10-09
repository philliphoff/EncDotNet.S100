using EncDotNet.S100.Collections.Secom;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services.Secom;

/// <summary>
/// The viewer's SECOM trust with its stores (#845), built together so the
/// trusted authorities and the identity in use are in place before any SECOM
/// client is made: the user's anchors are applied, then the command-line
/// identity or the stored one in use is presented.
/// </summary>
internal sealed class SecomCredentials
{
    public SecomCredentials(
        ViewerSettings settings,
        SecomRevocation revocation,
        ISecomKeyStore keys,
        SecomClientIdentity? commandLineIdentity,
        ILogger? logger = null)
    {
        Trust = new SecomServerTrust(revocation: revocation);
        Authorities = new TrustedAuthorityStore(settings, Trust);
        Identities = new SecomIdentityStore(settings, Trust, keys, logger);
        Authorities.Apply();
        Identities.Restore(commandLineIdentity);
    }

    /// <summary>The trust every SECOM client's handler comes from.</summary>
    public SecomServerTrust Trust { get; }

    /// <summary>The trusted authorities.</summary>
    public TrustedAuthorityStore Authorities { get; }

    /// <summary>The stored MCP identities.</summary>
    public SecomIdentityStore Identities { get; }
}
