using System.Reflection;

namespace EncDotNet.S100.Collections.Net;

/// <summary>HTTP conventions of the Library's requests.</summary>
public static class LibraryHttp
{
    /// <summary>
    /// The <c>User-Agent</c> the Library identifies itself with when its client
    /// sets none: the library and its version, with the project's address as
    /// the contact (<c>EncDotNet.S100/1.2.3 (+https://github.com/philliphoff/EncDotNet.S100)</c>).
    /// The NWS API refuses requests without one and asks that it identify the
    /// application; no personal address is sent. Hosts set their own (the viewer's names SoundCharts).
    /// </summary>
    public static string DefaultUserAgent { get; } = ProductUserAgent("EncDotNet.S100", typeof(LibraryHttp).Assembly);

    /// <summary>The project's address, the contact in <see cref="DefaultUserAgent"/>.</summary>
    public const string ProjectUri = "https://github.com/philliphoff/EncDotNet.S100";

    /// <summary>A <c>User-Agent</c> naming <paramref name="product"/> at <paramref name="assembly"/>'s version, with the project's address.</summary>
    /// <param name="product">The product token (no spaces), e.g. <c>SoundCharts</c>.</param>
    /// <param name="assembly">The assembly whose informational version is the product version.</param>
    public static string ProductUserAgent(string product, Assembly assembly)
    {
        ArgumentException.ThrowIfNullOrEmpty(product);
        ArgumentNullException.ThrowIfNull(assembly);
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        // Drop the commit after '+', which isn't a product version.
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        if (plus > 0)
            version = version[..plus];
        return $"{product}/{version} (+{ProjectUri})";
    }
}
