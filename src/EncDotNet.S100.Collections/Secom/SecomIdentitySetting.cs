using System.Diagnostics.CodeAnalysis;

namespace EncDotNet.S100.Collections.Secom;

/// <summary>
/// The values of a SECOM source's <see cref="SecomSource.Identity"/> setting
/// (#883): which MCP identity it presents to the service, and signs SECOM 2.0
/// requests with.
/// </summary>
/// <remarks>
/// The setting is a reference only, never a key, password or thumbprint copy:
/// <list type="bullet">
/// <item><see cref="Default"/> follows the default identity, resolved each time the source is read.</item>
/// <item><see cref="None"/> never presents a certificate and never signs.</item>
/// <item>A reference such as <c>sc-ident:91d0b3c5</c> names one identity (<see cref="ReferenceIdOf"/>).
/// When no identity has that reference, the source presents none.</item>
/// </list>
/// </remarks>
public static class SecomIdentitySetting
{
    /// <summary>Follow the default identity.</summary>
    public const string Default = "default";

    /// <summary>Never present a certificate or sign a request.</summary>
    public const string None = "none";

    /// <summary>The prefix of an identity's reference id, e.g. <c>sc-ident:4f9c2a7e</c>.</summary>
    public const string ReferencePrefix = "sc-ident:";

    /// <summary>The reference id for a certificate thumbprint: <c>sc-ident:</c> and its first 8 hex digits, lower case.</summary>
    public static string ReferenceIdOf(string thumbprint)
    {
        ArgumentException.ThrowIfNullOrEmpty(thumbprint);
        return ReferencePrefix + thumbprint[..Math.Min(8, thumbprint.Length)].ToLowerInvariant();
    }

    /// <summary>True when <paramref name="value"/> is a reference id: <c>sc-ident:</c> and 1–64 hex digits.</summary>
    public static bool IsReference([NotNullWhen(true)] string? value) =>
        value is not null
        && value.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase)
        && value.Length > ReferencePrefix.Length
        && value.Length <= ReferencePrefix.Length + 64
        && value.AsSpan(ReferencePrefix.Length).ContainsAnyExcept("0123456789abcdefABCDEF") is false;

    /// <summary>
    /// Reads a setting as a user or tool gave it: <c>default</c>, <c>none</c>
    /// or a reference id, trimmed and in lower case.
    /// </summary>
    /// <param name="value">The value given.</param>
    /// <param name="setting">The setting in canonical form.</param>
    /// <returns>False when <paramref name="value"/> is none of the three.</returns>
    public static bool TryParse(string? value, [NotNullWhen(true)] out string? setting)
    {
        var trimmed = value?.Trim();
        setting = trimmed?.ToLowerInvariant() switch
        {
            Default => Default,
            None => None,
            { } other when IsReference(other) => other,
            _ => null,
        };
        return setting is not null;
    }
}
