using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// Reads SECOM sources again when what they were refused for may have changed
/// (#880): when the MCP identity in use changes, those whose last read failed
/// for want of an identity or because the service refused it; when the trusted
/// authorities change, those whose server certificate was not trusted. Sources
/// that read fine are left alone.
/// </summary>
/// <remarks>
/// Hosts create one per Library and keep it for its lifetime. It listens to
/// <see cref="SecomServerTrust.IdentityChanged"/> and
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
        _trust.IdentityChanged += OnIdentityChanged;
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

    private void OnIdentityChanged(object? sender, EventArgs e) =>
        Refresh(SecomDiagnosticCodes.IsIdentityMatter);

    private void OnAnchorsChanged(object? sender, EventArgs e) =>
        Refresh(code => code == SecomDiagnosticCodes.ServerUntrusted);

    private void Refresh(Func<string, bool> matches)
    {
        foreach (var (collectionId, sourceId) in Affected(matches))
            _library.Refresh(collectionId, sourceId);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _trust.IdentityChanged -= OnIdentityChanged;
        _trust.AnchorsChanged -= OnAnchorsChanged;
    }
}
