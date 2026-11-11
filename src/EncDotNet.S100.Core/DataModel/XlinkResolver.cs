namespace EncDotNet.S100.DataModel;

/// <summary>
/// Resolves <c>xlink:href</c> cross-references against a pre-built index of a
/// dataset's features and information types, keyed by <c>gml:id</c>
/// (case-insensitive).
/// </summary>
/// <remarks>
/// <para>
/// Build an instance with <see cref="Build"/> at the top of a typed-model
/// projection, then use <see cref="Resolve{T}"/> to follow each
/// <c>xlink:href</c>. Unresolved references emit a
/// <see cref="DiagnosticSeverity.Warning"/> diagnostic with code
/// <c>"xlink.unresolved"</c>; the typed model never throws for a missing target.
/// </para>
/// <para>
/// The lookup table accepts heterogeneous object types — features and
/// information types from any S-100 product. The generic type parameter on
/// <see cref="Resolve{T}"/> filters by the expected destination type, so a
/// caller resolving a <c>routeWaypoint</c> reference can ask for <c>T =
/// S421Feature</c> and receive <c>null</c> (plus a diagnostic) if the target
/// happens to be an information type instead.
/// </para>
/// </remarks>
public sealed class XlinkResolver
{
    private readonly Dictionary<string, object> _byId;

    private XlinkResolver(Dictionary<string, object> byId)
    {
        _byId = byId;
    }

    /// <summary>
    /// Builds an xlink lookup from the supplied features and information
    /// types. Each object must expose a <c>gml:id</c>-style identifier; objects
    /// with a <c>null</c> or empty identifier are silently skipped (they are
    /// unreachable by xlink).
    /// </summary>
    /// <param name="objects">
    /// The objects to index. Pairs of (identifier, object) — typically built
    /// by enumerating the dataset's features and information types and
    /// projecting their <c>Id</c> property.
    /// </param>
    public static XlinkResolver Build(IEnumerable<KeyValuePair<string, object>> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var index = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in objects)
        {
            if (string.IsNullOrEmpty(kvp.Key)) continue;
            index[kvp.Key] = kvp.Value;
        }
        return new XlinkResolver(index);
    }

    /// <summary>
    /// Returns <c>true</c> if the supplied <c>xlink:href</c> target is present
    /// in the index. The leading <c>#</c> on a fragment identifier is
    /// stripped before lookup.
    /// </summary>
    public bool Contains(string href) => _byId.ContainsKey(Normalise(href));

    /// <summary>
    /// Resolves an <c>xlink:href</c> value to a typed object. Returns
    /// <c>null</c> and reports a <c>"xlink.unresolved"</c> diagnostic when the
    /// target is missing or has a different type than <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The expected destination object type.</typeparam>
    /// <param name="href">The raw <c>xlink:href</c> value.</param>
    /// <param name="role">The association role (used for diagnostic text).</param>
    /// <param name="context">The projection context for diagnostic reporting.</param>
    /// <param name="relatedId">The <c>gml:id</c> of the referring object, if any.</param>
    public T? Resolve<T>(string href, string role, ProjectionContext context, string? relatedId = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(href);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(context);

        var key = Normalise(href);
        if (_byId.TryGetValue(key, out var obj) && obj is T typed)
            return typed;

        context.Warn(
            $"Unresolved {role} reference '{href}'.",
            code: "xlink.unresolved",
            relatedId: relatedId,
            relatedAttribute: role);
        return null;
    }

    /// <summary>
    /// Resolves an <c>xlink:href</c> without filtering by type. Useful when the
    /// caller needs to dispatch on the runtime type of the target (e.g. for
    /// references that may point to either a feature or an information type).
    /// </summary>
    public object? ResolveAny(string href, string role, ProjectionContext context, string? relatedId = null)
    {
        ArgumentNullException.ThrowIfNull(href);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(context);

        if (_byId.TryGetValue(Normalise(href), out var obj))
            return obj;

        context.Warn(
            $"Unresolved {role} reference '{href}'.",
            code: "xlink.unresolved",
            relatedId: relatedId,
            relatedAttribute: role);
        return null;
    }

    private static string Normalise(string href) =>
        href.StartsWith('#') ? href[1..] : href;
}
