using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that adds a bulk-insert operation
/// raising a single <see cref="NotifyCollectionChangedAction.Reset"/> instead of
/// one <see cref="NotifyCollectionChangedAction.Add"/> per item. This keeps
/// registering a very large exchange set (thousands of cells) from triggering
/// O(N²) work in every collection subscriber (grouping rebuilds, extent-overlay
/// rebuilds, etc.) — the pathology that froze the UI when opening a 7,000-cell
/// S-57 set. See issue #458.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Inserts every item in <paramref name="items"/> at
    /// <paramref name="index"/> (preserving their order), then raises a single
    /// collection-changed <see cref="NotifyCollectionChangedAction.Reset"/> plus
    /// the <c>Count</c>/indexer property notifications. Subscribers that handle
    /// Reset by resynchronising therefore run once for the whole batch rather
    /// than once per item. No event is raised for an empty batch.
    /// </summary>
    /// <param name="index">Zero-based insert position.</param>
    /// <param name="items">The items to insert.</param>
    public void InsertRange(int index, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = items as IReadOnlyList<T> ?? items.ToList();
        if (list.Count == 0)
            return;

        CheckReentrancy();

        for (var i = 0; i < list.Count; i++)
            Items.Insert(index + i, list[i]);

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
