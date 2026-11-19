using EncDotNet.S100.Collections.Net;
using EncDotNet.S100.Datasets.Pipelines;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Live sources in <c>s100</c> and its MCP server (data source design note D9):
/// the creation context they're built with.
/// </summary>
internal static class HeadlessSources
{
    /// <summary>How long a source's web request may take.</summary>
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The context a headless host creates sources with: a hardened HTTP
    /// client, and secrets from environment variables only (no secret store,
    /// D7), with <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The source's options, or <see langword="null"/> for its defaults.</param>
    /// <param name="loggerFactory">The loggers, or <see langword="null"/> for none.</param>
    /// <param name="timeProvider">The clock, or <see langword="null"/> for the system's.</param>
    public static SourceCreationContext Context(
        object? options = null,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? timeProvider = null) => new()
        {
            Options = options,
            CreateHttpClient = static () => new HttpClient(HardenedTls.CreateHandler()) { Timeout = HttpTimeout },
            LoggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            TimeProvider = timeProvider ?? TimeProvider.System,
            ReadSecret = null,
        };
}
