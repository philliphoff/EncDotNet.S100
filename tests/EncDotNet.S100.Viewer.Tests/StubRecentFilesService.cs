using System;
using System.Collections.Generic;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// In-memory <see cref="IRecentFilesService"/> for test view-model
/// construction. Does not touch the on-disk settings file.
/// </summary>
internal sealed class StubRecentFilesService : IRecentFilesService
{
    private readonly List<string> _items = new();

    public IReadOnlyList<string> Items => _items;

    public event Action? Changed;

    public void Add(string path)
    {
        _items.Remove(path);
        _items.Insert(0, path);
        Changed?.Invoke();
    }

    public void Remove(string path)
    {
        if (_items.Remove(path))
            Changed?.Invoke();
    }

    public void Clear()
    {
        if (_items.Count == 0)
            return;
        _items.Clear();
        Changed?.Invoke();
    }
}
