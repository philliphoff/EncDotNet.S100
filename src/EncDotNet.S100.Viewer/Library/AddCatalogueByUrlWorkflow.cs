using System.Globalization;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Adds an online catalogue or feed by URL to the user's own sources (#670,
/// #1399): checks the URL, finds it among the sources already listed, else
/// reads it, recognises its format (through the same
/// <see cref="CatalogueUrlRecognizer"/> as MCP <c>add_library_source
/// url=…</c>) and keeps it in the <see cref="UserCatalogueStore"/>. The
/// Online Catalogue directory's "Add by URL" form runs this.
/// </summary>
internal sealed class AddCatalogueByUrlWorkflow
{
    private readonly CatalogueUrlRecognizer _urls;
    private readonly UserCatalogueStore _store;

    /// <summary>Creates the workflow.</summary>
    /// <param name="urls">Reads a URL and recognises its format.</param>
    /// <param name="store">Keeps the sources the user added.</param>
    public AddCatalogueByUrlWorkflow(CatalogueUrlRecognizer urls, UserCatalogueStore store)
    {
        ArgumentNullException.ThrowIfNull(urls);
        ArgumentNullException.ThrowIfNull(store);
        _urls = urls;
        _store = store;
    }

    /// <summary>The sources the user added by URL, in the order added.</summary>
    public IReadOnlyList<KnownCatalogueSource> Added => _store.Sources;

    /// <summary>Removes the source with <paramref name="id"/> from the user's own.</summary>
    /// <param name="id">The source's id.</param>
    /// <returns>False when there is none.</returns>
    public bool Remove(string id) => _store.Remove(id);

    /// <summary>Adds the catalogue at <paramref name="request"/>'s URL, unless it is listed already.</summary>
    /// <param name="request">The URL as typed, and the sources already listed.</param>
    /// <param name="cancellationToken">Cancels reading the URL.</param>
    /// <returns>The source added or already listed, or why the URL can't be added.</returns>
    public async Task<AddCatalogueByUrlOutcome> RunAsync(AddCatalogueByUrlRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CatalogueUrlRecognizer.TryParse(request.Url, out var uri))
            return AddCatalogueByUrlOutcome.Fail(Strings.Library_DirectoryUrlInvalid);

        // A catalogue already listed is simply chosen.
        if (request.Listed.FirstOrDefault(s => s.CatalogUri == uri) is { } listed)
            return new AddCatalogueByUrlOutcome(listed, AlreadyListed: true, Error: null);

        var recognised = await _urls.RecognizeAsync(uri, cancellationToken).ConfigureAwait(true);
        if (recognised.Source is not { } source)
        {
            return AddCatalogueByUrlOutcome.Fail(recognised.Problem switch
            {
                CatalogueUrlProblem.Unreadable => recognised.Detail ?? string.Empty,
                CatalogueUrlProblem.Json => Strings.Library_DirectoryUrlJson,
                CatalogueUrlProblem.UnsupportedRoot => string.Format(CultureInfo.CurrentCulture, Strings.Library_DirectoryUrlUnsupportedFormat, recognised.Detail),
                _ => Strings.Library_DirectoryUrlNotXml,
            });
        }

        _store.Add(source);
        return new AddCatalogueByUrlOutcome(source, AlreadyListed: false, Error: null);
    }
}

/// <summary>What <see cref="AddCatalogueByUrlWorkflow"/> adds.</summary>
/// <param name="Url">The URL as typed.</param>
/// <param name="Listed">The sources already listed (curated and the user's own); a URL among them isn't read again.</param>
internal sealed record AddCatalogueByUrlRequest(string Url, IReadOnlyList<KnownCatalogueSource> Listed);

/// <summary>What <see cref="AddCatalogueByUrlWorkflow"/> did.</summary>
/// <param name="Source">The source added, or the listed one with the same URL; <see langword="null"/> on failure.</param>
/// <param name="AlreadyListed">True when the URL was already listed (nothing was read or added).</param>
/// <param name="Error">Why the URL can't be added, as the form shows it; <see langword="null"/> on success.</param>
internal sealed record AddCatalogueByUrlOutcome(KnownCatalogueSource? Source, bool AlreadyListed, string? Error)
{
    /// <summary>A failure, with the message the form shows.</summary>
    public static AddCatalogueByUrlOutcome Fail(string error) => new(null, AlreadyListed: false, error);
}
