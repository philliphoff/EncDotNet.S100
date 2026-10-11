using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// Reads SECOM sources again when what they present, or were refused for,
/// may have changed (#880, #883):
/// <list type="bullet">
/// <item>the default identity changes: the sources set to <see cref="SecomIdentitySetting.Default"/>;</item>
/// <item>an identity is added, replaced or removed: the sources that resolve to it
/// (set to it, or following the default when it is the default);</item>
/// <item>the trusted authorities change: the sources whose server certificate was not trusted.</item>
/// </list>
/// A change to one source's own <see cref="SecomSource.Identity"/> reads that
/// source only, as any <see cref="CollectionLibrary.UpdateSource"/> does.
/// </summary>
/// <remarks>
/// Hosts create one per Library and keep it for its lifetime. It listens to
/// <see cref="SecomServerTrust.IdentitiesChanged"/> and
/// <see cref="SecomServerTrust.AnchorsChanged"/>, which may be raised on any
/// thread; <see cref="CollectionLibrary.Refresh"/> is safe to call from any.
/// </remarks>
public sealed class SecomTrustRefresh : IDisposable
{
    private readonly CollectionLibrary _library;
    private readonly SecomServerTrust _trust;

    /// <summary>Starts refreshing <paramref name="library"/>'s SECOM sources when <paramref name="trust"/> changes.</summary>
    public SecomTrustRefresh(CollectionLibrary library, SecomServerTrust trust)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(trust);
        _library = library;
        _trust = trust;
        _trust.IdentitiesChanged += OnIdentitiesChanged;
        _trust.AnchorsChanged += OnAnchorsChanged;
    }

    /// <summary>
    /// The SECOM sources whose last read failed with a
    /// <see cref="SecomDiagnosticCodes"/> code that <paramref name="matches"/>.
    /// </summary>
    public IReadOnlyList<(Guid CollectionId, Guid SourceId)> Affected(Func<string, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        return
        [
            .. _library.Collections.SelectMany(collection => collection.Sources
                .Where(source => source.Definition is SecomSource
                    && source.Index is { } index
                    && LibraryNodeText.SecomCode(index) is { } code
                    && matches(code))
                .Select(source => (collection.Id, source.Id))),
        ];
    }

    /// <summary>The SECOM sources an identity change affects (see the class remarks).</summary>
    public IReadOnlyList<(Guid CollectionId, Guid SourceId)> Affected(SecomIdentitiesChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var users = change.Change == SecomIdentityChange.DefaultChanged
            ? SecomIdentityUsage.FollowingDefault(_library.Collections)
            : change.Id is { } id ? SecomIdentityUsage.UsersOf(_library.Collections, id, change.DefaultId) : [];
        return [.. users.Select(u => (u.CollectionId, u.Source.Id))];
    }

    private void OnIdentitiesChanged(object? sender, SecomIdentitiesChangedEventArgs e)
    {
        foreach (var (collectionId, sourceId) in Affected(e))
            _library.Refresh(collectionId, sourceId);
    }

    private void OnAnchorsChanged(object? sender, EventArgs e)
    {
        foreach (var (collectionId, sourceId) in Affected(code => code == SecomDiagnosticCodes.ServerUntrusted))
            _library.Refresh(collectionId, sourceId);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _trust.IdentitiesChanged -= OnIdentitiesChanged;
        _trust.AnchorsChanged -= OnAnchorsChanged;
    }
}
