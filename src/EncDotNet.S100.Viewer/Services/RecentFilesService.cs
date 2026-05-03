using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IRecentFilesService"/> backed by
/// <see cref="ViewerSettings.RecentDatasetPaths"/>. Each mutation persists
/// settings and raises <see cref="Changed"/>.
/// </summary>
internal sealed class RecentFilesService : IRecentFilesService
{
    private readonly ViewerSettings _settings;

    public RecentFilesService(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public IReadOnlyList<string> Items => _settings.RecentDatasetPaths;

    public event Action? Changed;

    public void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        _settings.AddRecentDataset(path);
        _settings.Save();
        Changed?.Invoke();
    }

    public void Remove(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        var removed = _settings.RecentDatasetPaths.RemoveAll(
            p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            return;
        _settings.Save();
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_settings.RecentDatasetPaths.Count == 0)
            return;
        _settings.ClearRecentDatasets();
        _settings.Save();
        Changed?.Invoke();
    }
}
