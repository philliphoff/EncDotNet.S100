namespace EncDotNet.S100.Collections.Secom;

/// <summary>
/// A source's <see cref="SecomSource.Identity"/> setting resolved by
/// <see cref="SecomServerTrust.Resolve"/> (#883): the identity it presents now.
/// </summary>
/// <param name="Setting">The setting in canonical form: <c>default</c>, <c>none</c> or a reference id.</param>
/// <param name="Id">
/// The reference id it resolves to: the default's id for <c>default</c>, or
/// the reference itself; <see langword="null"/> for <c>none</c> and for a
/// default that is not set.
/// </param>
/// <param name="Identity">
/// The identity presented, or <see langword="null"/> for none, including when
/// <paramref name="Id"/> names an identity that is not registered.
/// </param>
public sealed record SecomResolvedIdentity(string Setting, string? Id, SecomClientIdentity? Identity)
{
    /// <summary>No identity: never presents a certificate or signs.</summary>
    public static SecomResolvedIdentity None { get; } = new(SecomIdentitySetting.None, null, null);

    /// <summary>True when the setting follows the default identity.</summary>
    public bool FollowsDefault => Setting == SecomIdentitySetting.Default;

    /// <summary>True when <see cref="Id"/> names an identity that is not registered (removed, or never imported here).</summary>
    public bool IsMissing => Id is not null && Identity is null;
}

/// <summary>
/// What the last TLS handshake with a host showed, for one identity (or none)
/// presented through <see cref="SecomServerTrust.CreateHandler"/>'s handlers (#883).
/// </summary>
/// <param name="Host">The host.</param>
/// <param name="CertificateRequested">True when the server asked for a client certificate.</param>
/// <param name="AcceptableIssuers">
/// The distinguished names of the CAs the server said it accepts client
/// certificates from; empty when it named none (SECOM 2.0 signed-request
/// services name none) or did not ask.
/// </param>
/// <param name="At">When the handshake was seen.</param>
public sealed record SecomHandshake(string Host, bool CertificateRequested, IReadOnlyList<string> AcceptableIssuers, DateTimeOffset At);

/// <summary>What changed in <see cref="SecomServerTrust"/>'s identities.</summary>
public enum SecomIdentityChange
{
    /// <summary>An identity was registered under a new reference id.</summary>
    Added,

    /// <summary>An identity registered under a reference id was replaced with another.</summary>
    Replaced,

    /// <summary>An identity was removed (deleted, or revoked).</summary>
    Removed,

    /// <summary>The default identity changed.</summary>
    DefaultChanged,
}

/// <summary>Raised by <see cref="SecomServerTrust.IdentitiesChanged"/>.</summary>
/// <param name="change">What changed.</param>
/// <param name="id">
/// The reference id added, replaced or removed; for
/// <see cref="SecomIdentityChange.DefaultChanged"/>, the new default (or <see langword="null"/>).
/// </param>
/// <param name="defaultId">
/// For <see cref="SecomIdentityChange.DefaultChanged"/>, the previous default;
/// otherwise the default when the change was made.
/// </param>
public sealed class SecomIdentitiesChangedEventArgs(SecomIdentityChange change, string? id, string? defaultId) : EventArgs
{
    /// <summary>What changed.</summary>
    public SecomIdentityChange Change { get; } = change;

    /// <summary>The reference id concerned (see the constructor).</summary>
    public string? Id { get; } = id;

    /// <summary>The previous default, or the default at the time (see the constructor).</summary>
    public string? DefaultId { get; } = defaultId;
}
