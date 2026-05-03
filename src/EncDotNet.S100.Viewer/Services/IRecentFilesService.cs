using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Tracks the most-recently-opened dataset paths and persists them via
/// <see cref="ViewerSettings"/>. Raises <see cref="Changed"/> after any
/// mutation so menu builders can rebuild without polling.
/// </summary>
internal interface IRecentFilesService
{
    IReadOnlyList<string> Items { get; }

    event Action? Changed;

    void Add(string path);

    void Remove(string path);

    void Clear();
}
