using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using EncDotNet.S100.Cli.Infrastructure.Feeds;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 feed serve</c> publishes a folder, exchange set or dataset as an
/// S-100 feed over HTTP (issue #680), so another machine's viewer can add the
/// printed URL (Library → Online Catalogue → Add URL), see the datasets and
/// their coverage, and download them.
/// </summary>
/// <remarks>
/// The path is indexed in place and re-checked at most every
/// <c>--refresh</c> seconds, so datasets added or changed while serving
/// appear. Nothing is written. By default the server listens on localhost
/// only; with <c>--host</c> set to another address a random access token is
/// generated (unless <c>--token</c> or <c>--no-token</c> is given) and becomes
/// part of every URL.
/// </remarks>
internal sealed class FeedServeCommand : AsyncCommand<FeedServeCommand.Settings>
{
    private const int MaxShownProblems = 5;

    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("A folder (scanned recursively), an exchange set (folder, CATALOG.XML, CATALOG.031 or .zip), or a single dataset.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("--host <ADDRESS>")]
        [Description("The address to listen on (default 127.0.0.1). Use 0.0.0.0 to serve other machines.")]
        public string Host { get; init; } = "127.0.0.1";

        [CommandOption("--port <PORT>")]
        [Description("The port to listen on (default 8100; 0 picks a free port).")]
        public int Port { get; init; } = 8100;

        [CommandOption("--token <TOKEN>")]
        [Description("An access token that becomes part of every URL. Generated automatically when --host is not a loopback address.")]
        public string? Token { get; init; }

        [CommandOption("--no-token")]
        [Description("Serve without a token even on a non-loopback address.")]
        public bool NoToken { get; init; }

        [CommandOption("--title <TITLE>")]
        [Description("The feed's title (default: the folder or file name).")]
        public string? Title { get; init; }

        [CommandOption("--refresh <SECONDS>")]
        [Description("How often, at most, to re-check the path for changes (default 10).")]
        public int RefreshSeconds { get; init; } = 10;

        public override ValidationResult Validate()
        {
            if (!Directory.Exists(Path) && !File.Exists(Path))
                return ValidationResult.Error($"'{Path}' does not exist.");
            if (!IPAddress.TryParse(Host, out _))
                return ValidationResult.Error($"'{Host}' is not an IP address.");
            if (Port is < 0 or > 65535)
                return ValidationResult.Error("--port must be between 0 and 65535.");
            if (Token is not null && NoToken)
                return ValidationResult.Error("--token and --no-token are mutually exclusive.");
            if (Token is { } token && (token.Length == 0 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')))
                return ValidationResult.Error("--token may contain only letters, digits, '-' and '_'.");
            if (RefreshSeconds < 1)
                return ValidationResult.Error("--refresh must be at least 1 second.");
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var address = IPAddress.Parse(settings.Host);
        var token = ResolveToken(settings, address);

        var publisher = new FeedPublisher(settings.Path, settings.Title, TimeSpan.FromSeconds(settings.RefreshSeconds));
        AnsiConsole.MarkupLine($"Indexing [bold]{Markup.Escape(publisher.Path)}[/]…");
        var feed = await publisher.GetAsync(force: true).ConfigureAwait(false);
        // Files that could not be read are left out; show a few rather than flood the console.
        var problems = publisher.Diagnostics.Where(d => d.Severity != Collections.IndexDiagnosticSeverity.Info).ToArray();
        foreach (var diagnostic in problems.Take(MaxShownProblems))
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(diagnostic.Message)}[/] [grey]{Markup.Escape(diagnostic.Path ?? string.Empty)}[/]");
        if (problems.Length > MaxShownProblems)
            AnsiConsole.MarkupLine(string.Create(CultureInfo.CurrentCulture, $"[yellow]…and {problems.Length - MaxShownProblems:N0} more problem(s).[/]"));

        await using var server = await FeedServer.StartAsync(
            publisher, address, settings.Port, token,
            line => AnsiConsole.MarkupLine($"[grey]{DateTime.Now:HH:mm:ss}[/] {Markup.Escape(line)}")).ConfigureAwait(false);

        AnsiConsole.MarkupLine(string.Create(CultureInfo.CurrentCulture,
            $"Serving [bold]{feed.Document.Items.Count:N0}[/] dataset(s) as [bold]{Markup.Escape(feed.Document.Title ?? string.Empty)}[/]:"));
        foreach (var url in Urls(address, server.Port, token))
            AnsiConsole.MarkupLine($"  [link]{Markup.Escape(url.AbsoluteUri)}[/]");
        AnsiConsole.MarkupLine("Add the URL in the viewer under Library → Online Catalogue → Add URL. Ctrl-C to stop.");

        // The web host handles Ctrl-C and SIGTERM and stops itself.
        await server.WaitForShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        return 0;
    }

    /// <summary>The given token; otherwise a random one when serving beyond this machine (unless --no-token).</summary>
    internal static string? ResolveToken(Settings settings, IPAddress address)
    {
        if (settings.Token is { } token)
            return token;
        if (settings.NoToken || IPAddress.IsLoopback(address))
            return null;

        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>The URLs to print: the address itself, or each of this machine's addresses when listening on all.</summary>
    private static IEnumerable<Uri> Urls(IPAddress address, int port, string? token)
    {
        if (!address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any))
            return [FeedServer.FeedUriFor(address, port, token)];

        var local = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Distinct()
            .Select(a => FeedServer.FeedUriFor(a, port, token))
            .ToArray();
        return local.Length > 0 ? local : [FeedServer.FeedUriFor(IPAddress.Loopback, port, token)];
    }
}
