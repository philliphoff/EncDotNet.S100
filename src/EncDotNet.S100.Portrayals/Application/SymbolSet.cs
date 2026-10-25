using System.Reflection;

namespace EncDotNet.S100.Portrayals.Application;

/// <summary>
/// The S-100 SVG symbols of a portrayal defined in code, read from embedded
/// resources and served by name through <see cref="Provider"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each <c>.svg</c> resource whose manifest name starts with the resource
/// prefix is one symbol, named by the rest of the resource name without the
/// extension: <c>EncDotNet.S100.Datasets.S104.Symbols.S104GAUGE_RISING.svg</c>
/// under the prefix <c>EncDotNet.S100.Datasets.S104.Symbols</c> is the symbol
/// <c>S104GAUGE_RISING</c>. Embed them with an explicit <c>LogicalName</c> so
/// the names don't depend on the project's root namespace:
/// </para>
/// <code>
/// &lt;EmbeddedResource Include="Symbols\*.svg"
///     LogicalName="My.Product.Symbols.%(Filename)%(Extension)" /&gt;
/// </code>
/// <para>
/// The symbols follow the S-100 Part 9 SVG conventions, so the renderers
/// treat them like catalogue symbols: the pivot point at the origin, sizes in
/// millimetres, and colours as palette classes (<c>fTOKEN</c> fill,
/// <c>sTOKEN</c> stroke) resolved against the portrayal's
/// <see cref="ApplicationPalette"/>. A symbol is read once, on first use.
/// </para>
/// </remarks>
public sealed class SymbolSet
{
    private const string Extension = ".svg";

    private readonly Dictionary<string, Lazy<string>> _symbols = new(StringComparer.Ordinal);

    /// <summary>Creates a symbol set from <paramref name="assembly"/>'s embedded SVG resources.</summary>
    /// <param name="assembly">The assembly holding the resources.</param>
    /// <param name="resourcePrefix">
    /// The manifest resource name prefix, with or without the trailing <c>.</c>,
    /// e.g. <c>"EncDotNet.S100.Datasets.S104.Symbols"</c>.
    /// </param>
    /// <exception cref="ArgumentException">No SVG resource has the prefix.</exception>
    public SymbolSet(Assembly assembly, string resourcePrefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePrefix);

        string prefix = resourcePrefix.EndsWith('.') ? resourcePrefix : resourcePrefix + ".";
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(prefix, StringComparison.Ordinal)
                || !resource.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                || resource.Length == prefix.Length + Extension.Length)
            {
                continue;
            }

            string name = resource[prefix.Length..^Extension.Length];
            _symbols[name] = new Lazy<string>(() => Read(assembly, resource));
        }

        if (_symbols.Count == 0)
        {
            throw new ArgumentException(
                $"Assembly '{assembly.GetName().Name}' has no embedded SVG resources under '{prefix}'.",
                nameof(resourcePrefix));
        }

        Provider = GetSymbol;
    }

    /// <summary>The symbol names.</summary>
    public IReadOnlyCollection<string> Names => _symbols.Keys;

    /// <summary>
    /// Resolves a symbol name to its SVG, or <see langword="null"/> for a name
    /// the set doesn't hold. Pass it as a sub-layer's symbol provider. It is the
    /// same delegate on every call.
    /// </summary>
    public Func<string, string?> Provider { get; }

    /// <summary>Whether the set holds the symbol <paramref name="name"/>.</summary>
    /// <param name="name">The symbol name.</param>
    public bool Contains(string name) => _symbols.ContainsKey(name);

    /// <summary>
    /// The SVG of the symbol <paramref name="name"/>, or <see langword="null"/>
    /// for a name the set doesn't hold.
    /// </summary>
    /// <param name="name">The symbol name, matched ordinally.</param>
    public string? GetSymbol(string name) =>
        _symbols.TryGetValue(name, out var svg) ? svg.Value : null;

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource '{resource}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
