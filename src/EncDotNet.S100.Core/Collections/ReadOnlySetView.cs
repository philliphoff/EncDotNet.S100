using System.Collections;

namespace EncDotNet.S100.Collections;

/// <summary>
/// A non-downcastable read-only view over a live <see cref="IReadOnlySet{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Exposing a mutable <see cref="HashSet{T}"/> (or any <see cref="ISet{T}"/>)
/// directly as an <see cref="IReadOnlySet{T}"/> is unsafe: a consumer can cast
/// the returned reference back to its concrete type and mutate it, bypassing
/// the owner's invariants (for controllers in this codebase, that means
/// mutating state without raising their <c>Changed</c> event). Wrapping the set
/// in this view closes that hole — the wrapper only implements
/// <see cref="IReadOnlySet{T}"/>, so there is no concrete mutable type to cast
/// to.
/// </para>
/// <para>
/// The view is a live projection, not a snapshot: it forwards every call to the
/// wrapped set, so changes made by the owner remain visible to holders of the
/// view. This is O(1) to construct and allocates only the small wrapper object.
/// </para>
/// <para>
/// A dedicated type is used rather than <c>ReadOnlySet&lt;T&gt;</c> /
/// <c>HashSet&lt;T&gt;.AsReadOnly()</c> because those are only available on
/// .NET 9+, whereas <c>EncDotNet.S100.Core</c> also targets .NET 8.
/// </para>
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class ReadOnlySetView<T>(IReadOnlySet<T> inner) : IReadOnlySet<T>
{
    /// <inheritdoc />
    public int Count => inner.Count;

    /// <inheritdoc />
    public bool Contains(T item) => inner.Contains(item);

    /// <inheritdoc />
    public bool IsProperSubsetOf(IEnumerable<T> other) => inner.IsProperSubsetOf(other);

    /// <inheritdoc />
    public bool IsProperSupersetOf(IEnumerable<T> other) => inner.IsProperSupersetOf(other);

    /// <inheritdoc />
    public bool IsSubsetOf(IEnumerable<T> other) => inner.IsSubsetOf(other);

    /// <inheritdoc />
    public bool IsSupersetOf(IEnumerable<T> other) => inner.IsSupersetOf(other);

    /// <inheritdoc />
    public bool Overlaps(IEnumerable<T> other) => inner.Overlaps(other);

    /// <inheritdoc />
    public bool SetEquals(IEnumerable<T> other) => inner.SetEquals(other);

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
}
