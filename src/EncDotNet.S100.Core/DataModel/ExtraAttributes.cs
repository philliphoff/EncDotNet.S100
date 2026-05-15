using System.Collections.Immutable;

namespace EncDotNet.S100.DataModel;

/// <summary>
/// Helpers for the <c>ExtraAttributes</c> dictionary that every typed object
/// exposes — a verbatim preservation of any attribute the typed projection did
/// not consume.
/// </summary>
/// <remarks>
/// Typed-model authors collect every attribute they consume into a known-keys
/// list, then call <see cref="ExcludeKnown"/> to obtain the
/// <see cref="ImmutableDictionary{TKey,TValue}"/> exposed on the typed object.
/// This preserves round-trip fidelity for extension and future-edition
/// attributes that the typed model does not yet model.
/// </remarks>
public static class ExtraAttributes
{
    /// <summary>
    /// Returns a copy of <paramref name="source"/> with every key in
    /// <paramref name="knownKeys"/> removed. Comparison is
    /// case-insensitive.
    /// </summary>
    public static ImmutableDictionary<string, string> ExcludeKnown(
        ImmutableDictionary<string, string> source, params string[] knownKeys)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(knownKeys);

        var known = new HashSet<string>(knownKeys, StringComparer.OrdinalIgnoreCase);
        var b = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var (k, v) in source)
            if (!known.Contains(k)) b[k] = v;
        return b.ToImmutable();
    }
}
