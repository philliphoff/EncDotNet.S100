using System.Collections.Frozen;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// A per-dataset narrowing of viewing-group visibility, applied on top of the
/// map-wide display mode and the per-product hidden viewing groups.
/// </summary>
/// <remarks>
/// <para>
/// A filter can only hide more: a viewing group that the display mode or the
/// per-product settings already hide stays hidden. Use
/// <see cref="ShowOnly"/> to keep a few groups of one dataset (for example the
/// action points of an S-421 route while a planning tool draws its legs) and
/// <see cref="Hide"/> to drop a few.
/// </para>
/// <para>
/// Filters compare by value, so a host can skip a re-render when an equal
/// filter is set again.
/// </para>
/// </remarks>
public sealed class ViewingGroupFilter : IEquatable<ViewingGroupFilter>
{
    private ViewingGroupFilter(IEnumerable<int>? shownOnly, IEnumerable<int>? hidden)
    {
        ShownOnly = shownOnly?.ToFrozenSet();
        Hidden = (hidden ?? []).ToFrozenSet();
    }

    /// <summary>
    /// The only viewing groups the dataset may draw, or <c>null</c> when the
    /// filter doesn't restrict the dataset to a set.
    /// </summary>
    public IReadOnlySet<int>? ShownOnly { get; }

    /// <summary>Viewing groups the dataset doesn't draw.</summary>
    public IReadOnlySet<int> Hidden { get; }

    /// <summary>Creates a filter that draws only <paramref name="viewingGroups"/>.</summary>
    /// <param name="viewingGroups">The viewing groups to keep.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewingGroups"/> is <c>null</c>.</exception>
    public static ViewingGroupFilter ShowOnly(IEnumerable<int> viewingGroups)
    {
        ArgumentNullException.ThrowIfNull(viewingGroups);
        return new(viewingGroups, null);
    }

    /// <summary>Creates a filter that hides <paramref name="viewingGroups"/>.</summary>
    /// <param name="viewingGroups">The viewing groups to hide.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewingGroups"/> is <c>null</c>.</exception>
    public static ViewingGroupFilter Hide(IEnumerable<int> viewingGroups)
    {
        ArgumentNullException.ThrowIfNull(viewingGroups);
        return new(null, viewingGroups);
    }

    /// <summary>Whether the filter lets <paramref name="viewingGroup"/> draw.</summary>
    /// <param name="viewingGroup">A viewing group id.</param>
    /// <returns><c>true</c> when the group isn't filtered out.</returns>
    public bool Allows(int viewingGroup)
        => (ShownOnly is null || ShownOnly.Contains(viewingGroup)) && !Hidden.Contains(viewingGroup);

    /// <inheritdoc />
    public bool Equals(ViewingGroupFilter? other)
        => other is not null
           && (ReferenceEquals(this, other)
               || (ShownOnly is null ? other.ShownOnly is null : other.ShownOnly is not null && ShownOnly.SetEquals(other.ShownOnly))
                  && Hidden.SetEquals(other.Hidden));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ViewingGroupFilter);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ShownOnly is null);
        foreach (var id in (ShownOnly ?? FrozenSet<int>.Empty).Order())
            hash.Add(id);
        foreach (var id in Hidden.Order())
            hash.Add(~id);
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
        => ShownOnly is null
            ? $"hide [{string.Join(", ", Hidden.Order())}]"
            : $"only [{string.Join(", ", ShownOnly.Order())}]"
              + (Hidden.Count > 0 ? $", hide [{string.Join(", ", Hidden.Order())}]" : string.Empty);
}
