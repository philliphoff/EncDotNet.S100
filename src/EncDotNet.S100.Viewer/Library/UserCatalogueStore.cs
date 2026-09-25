using System.Text.Json;
using EncDotNet.S100.Collections.KnownSources;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// The online catalogues the user added by URL (issue #670), persisted as a
/// known-sources document (<c>catalogues.json</c>, next to
/// <c>collections.json</c>) and listed in the directory alongside the
/// curated ones.
/// </summary>
internal sealed class UserCatalogueStore
{
    private readonly string _path;
    private readonly bool _readOnly;
    private readonly ILogger? _logger;
    private readonly object _gate = new();
    private List<KnownCatalogueSource>? _sources;

    public UserCatalogueStore(string path, bool readOnly = false, ILogger<UserCatalogueStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
        _readOnly = readOnly;
        _logger = logger;
    }

    public UserCatalogueStore(ViewerDataPaths paths, ViewerSettings settings, ILogger<UserCatalogueStore>? logger = null)
        : this(paths.UserCataloguesFilePath, settings.IsReadOnly, logger)
    {
    }

    /// <summary>The user's catalogues, in the order added.</summary>
    public IReadOnlyList<KnownCatalogueSource> Sources
    {
        get
        {
            lock (_gate)
                return Loaded().ToArray();
        }
    }

    /// <summary>Adds <paramref name="source"/>, replacing any earlier entry with the same id (the same URL).</summary>
    public void Add(KnownCatalogueSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            var sources = Loaded();
            var index = sources.FindIndex(s => s.Id == source.Id);
            if (index >= 0)
                sources[index] = source;
            else
                sources.Add(source);
            Save(sources);
        }
    }

    /// <summary>Removes the catalogue with <paramref name="id"/>; returns false when there is none.</summary>
    public bool Remove(string id)
    {
        lock (_gate)
        {
            var sources = Loaded();
            if (sources.RemoveAll(s => s.Id == id) == 0)
                return false;
            Save(sources);
            return true;
        }
    }

    private List<KnownCatalogueSource> Loaded()
    {
        if (_sources is not null)
            return _sources;

        _sources = [];
        try
        {
            if (File.Exists(_path))
            {
                using var stream = File.OpenRead(_path);
                _sources.AddRange(KnownCatalogueSources.Read(stream));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.LogWarning(ex, "Could not read the user's catalogues from {Path}", _path);
        }

        return _sources;
    }

    private void Save(List<KnownCatalogueSource> sources)
    {
        if (_readOnly)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temp = _path + ".tmp";
            using (var stream = File.Create(temp))
                KnownCatalogueSources.Write(stream, sources);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not save the user's catalogues to {Path}", _path);
        }
    }
}
