using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// The listening rules the HTTP-serving commands (<c>feed serve</c>,
/// <c>tiles serve</c>) share: localhost by default, and an access token in
/// every URL when serving beyond this machine.
/// </summary>
internal static class ServeHost
{
    /// <summary>Checks <c>--host</c>, <c>--port</c>, <c>--token</c> and <c>--no-token</c>.</summary>
    public static ValidationResult Validate(string host, int port, string? token, bool noToken)
    {
        if (!IPAddress.TryParse(host, out _))
            return ValidationResult.Error($"'{host}' is not an IP address.");
        if (port is < 0 or > 65535)
            return ValidationResult.Error("--port must be between 0 and 65535.");
        if (token is not null && noToken)
            return ValidationResult.Error("--token and --no-token are mutually exclusive.");
        if (token is not null && (token.Length == 0 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')))
            return ValidationResult.Error("--token may contain only letters, digits, '-' and '_'.");
        return ValidationResult.Success();
    }

    /// <summary>The given token; otherwise a random one when serving beyond this machine (unless <paramref name="noToken"/>).</summary>
    public static string? ResolveToken(string? token, bool noToken, IPAddress address)
    {
        if (token is not null)
            return token;
        if (noToken || IPAddress.IsLoopback(address))
            return null;

        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>The addresses to print: the address itself, or each of this machine's addresses when listening on all.</summary>
    public static IReadOnlyList<IPAddress> DisplayAddresses(IPAddress address)
    {
        if (!address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any))
            return [address];

        var local = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Distinct()
            .ToArray();
        return local.Length > 0 ? local : [IPAddress.Loopback];
    }

    /// <summary>The base URL for a host, port and token, e.g. <c>http://192.168.1.20:8100/&lt;token&gt;/</c>.</summary>
    public static Uri BaseUri(IPAddress address, int port, string? token)
    {
        var host = address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();
        var prefix = token is null ? string.Empty : Uri.EscapeDataString(token) + "/";
        return new Uri(string.Create(CultureInfo.InvariantCulture, $"http://{host}:{port}/{prefix}"));
    }
}
