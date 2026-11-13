using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using EncDotNet.S100.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// <c>s100 health</c> asks a running <c>tiles serve</c>, <c>feed serve</c> or <c>mcp serve --transport http</c>
/// for its <c>/healthz</c> health check and exits 0 when it answers. It exists
/// for container health checks (issue #1464): the s100 image has no shell or
/// curl, so Docker and Compose run this instead.
/// </summary>
internal sealed class HealthCommand : AsyncCommand<HealthCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--host <ADDRESS>")]
        [Description("The server's address (default 127.0.0.1).")]
        public string Host { get; init; } = "127.0.0.1";

        [CommandOption("--port <PORT>")]
        [Description("The server's port (default 8200, the tiles serve default; feed serve uses 8100 and mcp serve --transport http 8300).")]
        public int Port { get; init; } = 8200;

        [CommandOption("--timeout <SECONDS>")]
        [Description("How long to wait for an answer, in seconds (default 5).")]
        public int TimeoutSeconds { get; init; } = 5;

        public override ValidationResult Validate()
        {
            if (!IPAddress.TryParse(Host, out _))
                return ValidationResult.Error($"'{Host}' is not an IP address.");
            if (Port is < 1 or > 65535)
                return ValidationResult.Error("--port must be between 1 and 65535.");
            if (TimeoutSeconds < 1)
                return ValidationResult.Error("--timeout must be at least 1.");
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (healthy, message) = await ProbeAsync(
                IPAddress.Parse(settings.Host),
                settings.Port,
                TimeSpan.FromSeconds(settings.TimeoutSeconds),
                cancellationToken)
            .ConfigureAwait(false);
        AnsiConsole.MarkupLine(healthy ? Markup.Escape(message) : $"[red]{Markup.Escape(message)}[/]");
        return healthy ? 0 : 1;
    }

    /// <summary>
    /// Asks the server at <paramref name="address"/>:<paramref name="port"/>
    /// for <see cref="ServeHost.HealthPath"/>. Healthy only on <c>200 OK</c>;
    /// the message says what was asked and what came back.
    /// </summary>
    internal static async Task<(bool Healthy, string Message)> ProbeAsync(
        IPAddress address,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        var url = new Uri(ServeHost.BaseUri(address, port, token: null), ServeHost.HealthPath.TrimStart('/'));
        using var client = new HttpClient { Timeout = timeout };
        try
        {
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK
                ? (true, $"Healthy: {url}")
                : (false, string.Create(CultureInfo.InvariantCulture, $"Unhealthy: {url} answered {(int)response.StatusCode} {response.ReasonPhrase}."));
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException)
        {
            return (false, $"Unhealthy: nothing is listening at {url}.");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Unhealthy: {url}: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, string.Create(CultureInfo.InvariantCulture, $"Unhealthy: {url} didn't answer within {timeout.TotalSeconds:0} s."));
        }
    }
}
